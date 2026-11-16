using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EarthUnscripted.Packs
{
    /// <summary>One immutable, layout-bound service anchor. This is separate from
    /// racing rules: different events can use different pits on the same layout.</summary>
    public sealed class CircuitPitComplex
    {
        public readonly string Id, Name, PitRoadId;
        public readonly Vector3 Bay, Forward;
        internal CircuitPitComplex(string id,string name,string road,Vector3 bay,Vector3 forward)
        {Id=id;Name=name;PitRoadId=road;Bay=bay;Forward=forward;}
    }

    public sealed class CircuitPitComplexPlan
    {
        public const int MaximumBytes=64*1024;
        public readonly string DefaultId;
        public readonly IReadOnlyList<CircuitPitComplex> Complexes;
        CircuitPitComplexPlan(string id,List<CircuitPitComplex> rows)
        {DefaultId=id;Complexes=rows.AsReadOnly();}
        static void Require(bool ok,string message)=>WorldPackPolicy.Require(ok,"circuit pit complex "+message);
        static string Text(JObject o,string key,int maximum)
        {
            var t=o[key];Require(t?.Type==JTokenType.String,"typed "+key);string s=(string)t;
            Require(!string.IsNullOrWhiteSpace(s)&&s.Length<=maximum&&!s.Any(char.IsControl),"bounded "+key);return s;
        }
        static Vector3 Vector(JToken token)
        {
            Require(token is JArray a&&a.Count==3,"vector arity");var values=new float[3];
            for(int i=0;i<3;i++){Require(token[i].Type==JTokenType.Integer||token[i].Type==JTokenType.Float,"numeric vector");values[i]=(float)token[i];Require(!float.IsNaN(values[i])&&!float.IsInfinity(values[i]),"finite vector");}
            return new Vector3(values[0],values[1],values[2]);
        }
        public CircuitPitComplex Select(string id)
        {var selected=Complexes.SingleOrDefault(c=>c.Id==(id??DefaultId));Require(selected!=null,"selected identity absent");return selected;}
        public static CircuitPitComplexPlan Parse(string text,GeoRegion region,string layoutHash,CircuitRoadSurfacePlan surfaces)
        {
            Require(region!=null&&region.circuit&&surfaces!=null,"authored pavement required");
            var value=RacingRuleSetJson.ReadStrictObject(Encoding.UTF8.GetBytes(text??""),MaximumBytes);
            string[] fields={"format","schema","layoutSha256","defaultId","complexes"};
            Require(value.Count==fields.Length&&fields.All(f=>value.Property(f)!=null),"root shape");
            Require(Text(value,"format",64)=="EarthUnscriptedCircuitPitComplexes"&&value["schema"]?.Type==JTokenType.Integer&&(long)value["schema"]==1,"format/schema");
            Require(Text(value,"layoutSha256",64)==layoutHash,"layout hash binding");
            string defaultId=Text(value,"defaultId",64);Require(WorldPackPolicy.SafeId(defaultId),"default identity");
            var rows=value["complexes"] as JArray;Require(rows!=null&&rows.Count>0&&rows.Count<=16,"list budget");
            var result=new List<CircuitPitComplex>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var token in rows)
            {
                Require(token is JObject,"complex object");var row=(JObject)token;
                string[] keys={"id","name","pitRoadId","bay","bayForward"};Require(row.Count==keys.Length&&keys.All(k=>row.Property(k)!=null),"complex shape");
                string id=Text(row,"id",64),name=Text(row,"name",120),roadId=Text(row,"pitRoadId",64);
                Require(WorldPackPolicy.SafeId(id)&&ids.Add(id)&&WorldPackPolicy.SafeId(roadId),"unique safe identity");
                var road=region.roads.SingleOrDefault(r=>r.id==roadId);
                Require(road!=null&&road.kind=="service"&&road.points!=null&&road.points.Length>=3&&surfaces.Covers(roadId),"mapped authored service lane");
                var bay=Vector(row["bay"]);var forward=Vector(row["bayForward"]);
                Require(Mathf.Abs(bay.x)<=6000&&Mathf.Abs(bay.z)<=6000&&bay.y>=-100&&bay.y<=1000&&Mathf.Abs(forward.y)<.001f&&Mathf.Abs(forward.magnitude-1)<.01f,"anchor bounds/direction");
                Require(surfaces.TryHeight(bay.x,bay.z,out float y)&&Mathf.Abs(y-bay.y)<=.005f,"anchor pavement height");
                float closest=float.MaxValue;Vector3 laneDirection=Vector3.zero;
                for(int i=1;i<road.points.Length;i++)
                {var a=road.points[i-1];var v=road.points[i]-a;float t=Mathf.Clamp01(Vector3.Dot(bay-a,v)/Mathf.Max(.0001f,v.sqrMagnitude));float distance=Vector3.Distance(bay,a+v*t);if(distance<closest){closest=distance;laneDirection=new Vector3(v.x,0,v.z).normalized;}}
                // The road width is a navigation hint for authored surfaces, not
                // evidence that the apron must be symmetric about its centreline.
                Require(closest<=Mathf.Max(1,road.width*.5f),"anchor off selected lane");
                Require(Vector3.Dot(laneDirection,forward)>.5f,"service faces against lane direction");
                var right=Vector3.Cross(Vector3.up,forward).normalized;
                // Car, stop box and mechanics working at the wheels must fit.
                // Waiting crew/tool homes may be on a separate apron/terrain;
                // their visual grounding is checked in the Player, not a reason
                // to invent a wider asphalt lane. Includes capsule/body margin.
                for(float x=-1.9f;x<=1.91f;x+=.2f)for(float z=-5.2f;z<=5.21f;z+=.4f)
                {var p=bay+right*x+forward*z;Require(surfaces.TryHeight(p.x,p.z,out _),"working area outside pavement");}
                result.Add(new CircuitPitComplex(id,name,roadId,bay,forward));
            }
            Require(ids.Contains(defaultId),"default complex absent");return new CircuitPitComplexPlan(defaultId,result);
        }
    }

    public sealed partial class WorldPackCircuitReader
    {
        readonly Dictionary<string,PackFile> pitComplexFiles=new Dictionary<string,PackFile>();
        void ReadPitComplexCatalogue(WorldPackManifest manifest,PackCompatibility compatible)
        {
            foreach(var decision in compatible.Decisions.Where(d=>d.Loaded&&d.Variant.Feature=="circuit-pit-complexes"&&d.Variant.Schema==1))
            {
                var owner=Layouts.SingleOrDefault(l=>decision.Element.Dependencies.Contains(l.ElementId));
                WorldPackPolicy.Require(owner!=null&&decision.Element.AffectsGameplay&&decision.Variant.Files.Count==1&&!pitComplexFiles.ContainsKey(owner.ElementId),"pit complex layout ownership");
                var file=manifest.Files.Single(f=>f.Path==decision.Variant.Files[0]);
                WorldPackPolicy.Require(file.Bytes>0&&file.Bytes<=CircuitPitComplexPlan.MaximumBytes,"pit complex payload budget");pitComplexFiles.Add(owner.ElementId,file);
            }
        }
        public CircuitSceneryPlan ResolvePitComplex(string layoutId,GeoRegion region,CircuitSceneryPlan scenery,CircuitRoadSurfacePlan surfaces,string selectedId,CancellationToken token)
        {
            var layout=Layout(layoutId);
            if(!pitComplexFiles.TryGetValue(layout.ElementId,out var file))
            {WorldPackPolicy.Require(selectedId==null,"event requires unavailable pit complex definitions");return scenery;}
            WorldPackPolicy.Require(scenery!=null,"pit complexes require owned circuit service/scenery");
            var definitions=CircuitPitComplexPlan.Parse(ReadChecked(file,CircuitPitComplexPlan.MaximumBytes,token),region,layout.Payload.Sha256,surfaces);
            token.ThrowIfCancellationRequested();return scenery.WithServiceAnchor(definitions.Select(selectedId));
        }
    }
}
