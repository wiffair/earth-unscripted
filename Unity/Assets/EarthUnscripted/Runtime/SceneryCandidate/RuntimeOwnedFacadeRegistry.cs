using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Security.Cryptography;using System.Text;using UnityEngine;
namespace EarthUnscripted.SceneryCandidate
{
    // Root-owned LOCAL runtime identity, not provider/source-cache identity.
    // Public construction grants nothing; only Root's typed prepared-owner seam
    // can attach a lease. No payload flags, arbitrary hashes or ContentKey mint it.
    public sealed class RuntimeOwnedFacadeRegistry:IOwnedFacadeRegistry
    {
        readonly GeoWorld world;readonly PreparedWorldData prepared;readonly OwnedFacadeRegistryKernel kernel;
        public RuntimeOwnedFacadeRegistry(){}
        RuntimeOwnedFacadeRegistry(GeoWorld world,PreparedWorldData prepared,OwnedFacadeRegistryKernel kernel){this.world=world;this.prepared=prepared;this.kernel=kernel;}
        internal static RuntimeOwnedFacadeRegistry IssueForPreparedWorld(GeoWorld world,PreparedWorldData prepared)
        {
            if(!world||world.ScopedMeshPlan!=null||prepared?.Region==null||!ReferenceEquals(world.Region,prepared.Region)||world.BuildingInputs==null||!ReferenceEquals(world.FacadePreparedGroundOwner,prepared.RoadSurfaces))throw new InvalidOperationException("Exact Root-owned prepared source/ground required");
            var snapshot=Snapshot(world,prepared);return new RuntimeOwnedFacadeRegistry(world,prepared,new OwnedFacadeRegistryKernel(snapshot));
        }
        static OwnedFacadeOwnerSnapshot Snapshot(GeoWorld world,PreparedWorldData prepared)
        {
            var matrix=world.transform.localToWorldMatrix;var bits=new float[16];for(int i=0;i<16;i++)bits[i]=matrix[i];
            return new OwnedFacadeOwnerSnapshot(world,prepared,world.Region,world.BuildingInputs,world.SurfaceGeneration,world.FacadePreparedGroundOwner??(object)prepared,bits);
        }
        public bool TryGetOwnedFacadeBinding(GeoWorld activeWorld,BuildingRenderInput.Entry entry,GeoBuilding sectionGeometry,out OwnedFacadeBinding binding)
        {
            binding=null;if(kernel==null||!activeWorld||!ReferenceEquals(activeWorld,world)||prepared==null)return false;
            try{if(activeWorld.ScopedMeshPlan!=null||!ReferenceEquals(activeWorld.Region,prepared.Region)||!ReferenceEquals(activeWorld.FacadePreparedGroundOwner,prepared.RoadSurfaces))return false;return kernel.TryGet(Snapshot(activeWorld,prepared),entry,sectionGeometry,out binding);}catch{return false;}
        }
    }
    // Internal value/reference snapshot. This is not a public source grant or
    // provider proof. Root wrapper supplies actual source and staged world refs.
    internal sealed class OwnedFacadeOwnerSnapshot
    {
        internal readonly object World,Prepared,Ground;internal readonly GeoRegion Region;internal readonly BuildingRenderInput.Plan Plan;internal readonly int Generation;internal readonly float[] Frame;
        internal OwnedFacadeOwnerSnapshot(object world,object prepared,GeoRegion region,BuildingRenderInput.Plan plan,int generation,object ground,float[] frame){World=world;Prepared=prepared;Region=region;Plan=plan;Generation=generation;Ground=ground;Frame=(float[])frame.Clone();}
    }
    internal sealed class OwnedFacadeRegistryKernel
    {
        sealed class Record{internal BuildingRenderInput.Entry Entry;internal GeoBuilding Geometry,Representative;internal GeoBuilding[] Sources;internal int[] SourceIndices;internal int RenderIndex;internal string Handle,Render;internal string[] Raw,Footprint;}
        readonly OwnedFacadeOwnerSnapshot captured;readonly GeoBuilding[] sourceArray;readonly BuildingRenderInput.Entry[] renderArray;readonly Dictionary<BuildingRenderInput.Entry,Record> records=new Dictionary<BuildingRenderInput.Entry,Record>();
        readonly string sourceGeneration,frameGeneration,groundGeneration;
        internal OwnedFacadeRegistryKernel(OwnedFacadeOwnerSnapshot owner)
        {
            if(owner==null||owner.World==null||owner.Prepared==null||owner.Region==null||owner.Plan==null||owner.Ground==null||owner.Frame==null||owner.Frame.Length!=16||owner.Frame.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))throw new InvalidDataException("Owned facade Root snapshot required");
            captured=owner;sourceArray=owner.Region.buildings;renderArray=owner.Plan.Renderable;if(sourceArray==null||sourceArray.Length>2500||renderArray==null||renderArray.Length>2500)throw new InvalidDataException("Owned facade source budget");
            var expectedNormalization=BuildingRenderInput.Create(owner.Region);
            string nonce=Guid.NewGuid().ToString("N");sourceGeneration=Commit("RuntimeOwnedFacadeSourceGeneration1",nonce,owner.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));frameGeneration=FloatCommit(owner.Frame);groundGeneration=Commit("RuntimeOwnedFacadeGroundGeneration1",nonce,sourceGeneration);
            for(int index=0;index<renderArray.Length;index++)
            {
                var entry=renderArray[index];if(entry?.Geometry==null||entry.OriginalSources==null||entry.OriginalSources.Length==0||entry.OriginalSources.Length>sourceArray.Length)continue;
                var sources=(GeoBuilding[])entry.OriginalSources.Clone();var indices=sources.Select(s=>Array.FindIndex(sourceArray,p=>ReferenceEquals(p,s))).ToArray();if(indices.Any(i=>i<0)||sources.Any(source=>!SupportedRawSource(source))||sources.Distinct().Count()!=sources.Length||!sources.Contains(entry.OriginalSource))continue;
                try
                {
                    if(sources.Any(s=>!owner.Plan.TryGet(s,out var same)||!ReferenceEquals(same,entry)))continue;
                    if(!expectedNormalization.TryGet(entry.OriginalSource,out var fresh)||fresh.Geometry==null||!ReferenceEquals(fresh.OriginalSource,entry.OriginalSource)||fresh.OriginalSources.Length!=sources.Length||!fresh.OriginalSources.Select((s,i)=>ReferenceEquals(s,sources[i])).All(v=>v)||OwnedFacadeHash.RenderGeometry(fresh.Geometry)!=OwnedFacadeHash.RenderGeometry(entry.Geometry))continue;
                    var r=new Record{Entry=entry,Geometry=entry.Geometry,Representative=entry.OriginalSource,Sources=sources,SourceIndices=indices,RenderIndex=index,Handle="rr-runtime-body/"+Guid.NewGuid().ToString("N"),Render=OwnedFacadeHash.RenderGeometry(entry.Geometry),Raw=sources.Select(OwnedFacadeHash.RawSource).ToArray(),Footprint=sources.Select(OwnedFacadeHash.RawFootprint).ToArray()};records.Add(entry,r);
                }catch(InvalidDataException){/* procedural fallback for unsupported source */}
            }
        }
        static bool SupportedRawSource(GeoBuilding source)
        {if(source?.points==null||source.points.Length<3||source.points.Length>4096||float.IsNaN(source.height)||float.IsInfinity(source.height))return false;try{var utf8=new UTF8Encoding(false,true);foreach(string text in new[]{source.id,source.kind,source.roof})if(text!=null&&utf8.GetByteCount(text)>4096)return false;}catch(EncoderFallbackException){return false;}return source.points.All(p=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z));}
        static string Commit(params string[] values){using(var m=new MemoryStream())using(var w=new BinaryWriter(m,Encoding.UTF8,true)){foreach(string value in values)w.Write(value);w.Flush();using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(m.ToArray())).Replace("-","").ToLowerInvariant();}}
        static string FloatCommit(float[] values){using(var m=new MemoryStream())using(var w=new BinaryWriter(m,Encoding.UTF8,true)){w.Write("RuntimeOwnedFacadeFrame1");foreach(float f in values)w.Write(f);w.Flush();using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(m.ToArray())).Replace("-","").ToLowerInvariant();}}
        static bool SameFrame(float[] a,float[] b)=>a!=null&&b!=null&&a.Length==16&&b.Length==16&&Enumerable.Range(0,16).All(i=>BitConverter.SingleToInt32Bits(a[i])==BitConverter.SingleToInt32Bits(b[i]));
        internal bool TryGet(OwnedFacadeOwnerSnapshot now,BuildingRenderInput.Entry entry,GeoBuilding section,out OwnedFacadeBinding binding)
        {
            binding=null;try
            {
                if(now==null||!ReferenceEquals(now.World,captured.World)||!ReferenceEquals(now.Prepared,captured.Prepared)||!ReferenceEquals(now.Ground,captured.Ground)||!ReferenceEquals(now.Region,captured.Region)||!ReferenceEquals(now.Plan,captured.Plan)||now.Generation!=captured.Generation||!SameFrame(now.Frame,captured.Frame)||!ReferenceEquals(now.Region.buildings,sourceArray)||!ReferenceEquals(now.Plan.Renderable,renderArray)||entry==null||!records.TryGetValue(entry,out var r)||!ReferenceEquals(section,r.Geometry)||!ReferenceEquals(entry.Geometry,r.Geometry)||!ReferenceEquals(entry.OriginalSource,r.Representative)||r.RenderIndex>=renderArray.Length||!ReferenceEquals(renderArray[r.RenderIndex],entry)||entry.OriginalSources==null||entry.OriginalSources.Length!=r.Sources.Length)return false;
                if(OwnedFacadeHash.RenderGeometry(entry.Geometry)!=r.Render)return false;
                for(int i=0;i<r.Sources.Length;i++)if(!ReferenceEquals(entry.OriginalSources[i],r.Sources[i])||!ReferenceEquals(sourceArray[r.SourceIndices[i]],r.Sources[i])||!now.Plan.TryGet(r.Sources[i],out var active)||!ReferenceEquals(active,entry)||!SupportedRawSource(r.Sources[i])||OwnedFacadeHash.RawSource(r.Sources[i])!=r.Raw[i]||OwnedFacadeHash.RawFootprint(r.Sources[i])!=r.Footprint[i])return false;
                binding=new OwnedFacadeBinding{RuntimeBodyHandle=r.Handle,OriginalSources=(GeoBuilding[])r.Sources.Clone(),RawSourceSha256=(string[])r.Raw.Clone(),RawFootprintSha256=(string[])r.Footprint.Clone(),RenderGeometrySha256=r.Render,SourceGenerationSha256=sourceGeneration,FrameGenerationSha256=frameGeneration,GroundGenerationSha256=groundGeneration};return true;
            }catch{return false;}
        }
    }
}
