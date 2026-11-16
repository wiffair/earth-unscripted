using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
namespace RoadRush
{
    // Local rendering interpretation only. Never replace Region.buildings with these
    // copies or include local keys in a canonical/network/save snapshot.
    public static class BuildingRenderInput
    {
        [System.Flags] public enum Flags
        {None=0,ClosedOpenRing=1,SnappedClosingPoint=2,RemovedZeroEdge=4,ZeroHeight=8,Degenerate=16,UnsupportedBudget=32,InvalidGeometry=64,LocalIdentity=128,ConflictingIdentity=256,CoalescedDuplicate=512,LargeFootprint=1024}
        public sealed class Entry
        {
            public GeoBuilding OriginalSource;public GeoBuilding[] OriginalSources;
            public GeoBuilding Geometry;public string ContentKey;public Flags Diagnostics;
            public bool Skipped=>Geometry==null;
            public string Diagnostic=>Diagnostics.ToString();
            public bool Owns(GeoBuilding source)=>Array.IndexOf(OriginalSources,source)>=0||ReferenceEquals(Geometry,source);
        }
        public sealed class Plan
        {
            public Entry[] Entries,Renderable;
            public int SourceCount,SkippedCount,CoalescedCount;
            readonly Dictionary<GeoBuilding,Entry> originals=new Dictionary<GeoBuilding,Entry>();
            internal void Register(GeoBuilding original,Entry entry){if(original!=null)originals[original]=entry;}
            public bool TryGet(GeoBuilding original,out Entry entry){entry=null;return original!=null&&originals.TryGetValue(original,out entry);}
        }
        public static Plan Create(GeoRegion region)
        {
            if(region==null)throw new ArgumentNullException(nameof(region));return Create(region.buildings);
        }
        // Detached window inputs never replace a canonical region's building array.
        public static Plan Create(GeoBuilding[] buildings)
        {
            var source=buildings??Array.Empty<GeoBuilding>();
            var groups=new Dictionary<string,List<GeoBuilding>>(StringComparer.Ordinal);var plan=new Plan{SourceCount=source.Length};
            long total=0;foreach(var b in source){total+=b?.points?.Length??0;if(total>400000)break;}
            bool budget=source.Length<=2500&&total<=400000;
            if(!budget)
            {plan.SkippedCount=source.Length;plan.Entries=new[]{new Entry{OriginalSources=Array.Empty<GeoBuilding>(),ContentKey="unsupported-region-budget",Diagnostics=Flags.UnsupportedBudget}};plan.Renderable=Array.Empty<Entry>();return plan;}
            foreach(var b in source)
            {string key=Descriptor(b);if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<GeoBuilding>();list.Add(b);}
            var entries=new List<Entry>();
            foreach(var pair in groups.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                var original=pair.Value[0];var entry=new Entry{OriginalSource=original,OriginalSources=pair.Value.Distinct().ToArray(),ContentKey=pair.Key};
                if(pair.Value.Count>1){entry.Diagnostics|=Flags.CoalescedDuplicate;plan.CoalescedCount+=pair.Value.Count-1;}
                if(!budget)entry.Diagnostics|=Flags.UnsupportedBudget;
                else entry.Geometry=Normalize(original,ref entry.Diagnostics);
                entries.Add(entry);foreach(var b in pair.Value)plan.Register(b,entry);
                if(entry.Skipped)plan.SkippedCount+=pair.Value.Count;
            }
            var ids=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var entry in entries){string id=entry.OriginalSource?.id??"";if(id.Length>0)ids[id]=ids.TryGetValue(id,out int count)?count+1:1;}
            var desired=new Dictionary<string,List<Entry>>(StringComparer.Ordinal);
            foreach(var entry in entries)
            {
                if(entry.Skipped)continue;string id=entry.OriginalSource.id??"";
                bool conflict=id.Length>0&&ids[id]>1;
                if(string.IsNullOrEmpty(id)||id.Length>100||conflict)
                {entry.Diagnostics|=Flags.LocalIdentity;if(conflict)entry.Diagnostics|=Flags.ConflictingIdentity;id="rr-render-"+entry.ContentKey;}
                entry.Geometry.id=id;if(!desired.TryGetValue(id,out var same))desired[id]=same=new List<Entry>();same.Add(entry);
            }
            // A provider could itself use a string resembling our generated prefix.
            // Resolve that namespace collision symmetrically, never by arrival order.
            foreach(var pair in desired)if(pair.Value.Count>1)foreach(var entry in pair.Value)
            {entry.Geometry.id=pair.Key+"~"+entry.ContentKey;entry.Diagnostics|=Flags.LocalIdentity|Flags.ConflictingIdentity;}
            foreach(var entry in entries)if(!entry.Skipped)plan.Register(entry.Geometry,entry);
            plan.Entries=entries.ToArray();plan.Renderable=entries.Where(e=>!e.Skipped).OrderBy(e=>e.Geometry.id,StringComparer.Ordinal).ThenBy(e=>e.ContentKey,StringComparer.Ordinal).ToArray();return plan;
        }
        static GeoBuilding Normalize(GeoBuilding source,ref Flags flags)
        {
            if(source==null||source.points==null){flags|=Flags.InvalidGeometry;return null;}
            var input=source.points;if(input.Length<3||input.Length>4096){flags|=Flags.UnsupportedBudget;return null;}
            if(!Finite(source.height)||source.height<0||source.height>500){flags|=Flags.InvalidGeometry;return null;}
            foreach(var p in input)if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)){flags|=Flags.InvalidGeometry;return null;}
            if(source.height==0){flags|=Flags.ZeroHeight;return null;}
            var points=new List<Vector3>(input.Length+1);
            foreach(var p in input)
            {
                if(points.Count>0&&points[points.Count-1].x==p.x&&points[points.Count-1].z==p.z){flags|=Flags.RemovedZeroEdge;continue;}points.Add(p);
            }
            if(points.Count<3){flags|=Flags.Degenerate;return null;}
            var first=points[0];var last=points[points.Count-1];float dx=last.x-first.x,dz=last.z-first.z;
            if((double)dx*dx+(double)dz*dz<=.000001)
            {if(!last.Equals(first))flags|=Flags.SnappedClosingPoint;points[points.Count-1]=first;}
            else{points.Add(first);flags|=Flags.ClosedOpenRing;}
            if(points.Count<4){flags|=Flags.Degenerate;return null;}
            double area=0;for(int i=0;i<points.Count-1;i++)area+=(double)points[i].x*points[i+1].z-(double)points[i+1].x*points[i].z;
            if(Math.Abs(area)<.0001){flags|=Flags.Degenerate;return null;}if(points.Count-1>256)flags|=Flags.LargeFootprint;
            return new GeoBuilding{id=source.id,kind=source.kind,roof=source.roof,height=source.height,measuredHeight=source.measuredHeight,points=points.ToArray()};
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static void Number(BinaryWriter writer,float v)=>writer.Write(v==0?0:BitConverter.SingleToInt32Bits(v));
        static string Descriptor(GeoBuilding source)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {
                writer.Write(source!=null);
                if(source!=null)
                {
                    writer.Write(source.id??"");writer.Write(source.kind??"");writer.Write(source.roof??"");Number(writer,source.height);writer.Write(source.measuredHeight);
                    writer.Write(source.points?.Length??-1);if(source.points!=null)foreach(var p in source.points){Number(writer,p.x);Number(writer,p.y);Number(writer,p.z);}
                }
                writer.Flush();using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
        }
    }
}
