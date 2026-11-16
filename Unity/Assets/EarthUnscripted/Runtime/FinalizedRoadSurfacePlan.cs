using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;
namespace EarthUnscripted
{
    // CPU-only immutable surface descriptors. Build after field admission, before
    // publishing region. Host AND guest use the finalized float DTOs, never solve.
    public sealed class FinalizedRoadSurfacePlan
    {
        public sealed class Part{public readonly GeoRoad Road;public readonly RoadWidthStripGeometry.Data Surface;public Part(GeoRoad road,RoadWidthStripGeometry.Data surface){Road=road;Surface=surface;}}
        public readonly Dictionary<string,Part> Parts=new Dictionary<string,Part>(StringComparer.Ordinal);
        public GeoRegion Owner{get;private set;}
        public EffectiveRoadWidthField WidthField{get;private set;}
        public RoadBankExteriorClip.SpatialIndex Pavement{get;private set;}
        static IEnumerable<RoadBankExteriorClip.Footprint> Triangles(Part p)
        {var m=p.Surface;for(int i=0;i<m.Triangles.Length;i+=3)yield return new RoadBankExteriorClip.Footprint{Id=p.Road.id,Layer=p.Road.layer,Bridge=p.Road.bridge,Polygon=new[]{new Vector2(m.Vertices[m.Triangles[i]].x,m.Vertices[m.Triangles[i]].z),new Vector2(m.Vertices[m.Triangles[i+1]].x,m.Vertices[m.Triangles[i+1]].z),new Vector2(m.Vertices[m.Triangles[i+2]].x,m.Vertices[m.Triangles[i+2]].z)}};}
        public static IEnumerable<int> PrepareSteps(GeoRegion region,RoadMarkingPlan topology,RoadWidthProfileDto[] profiles,Func<float,float,float> surface,Action<FinalizedRoadSurfacePlan> finish)
        {
            if(region.PreparedRoadWidths==null)throw new InvalidDataException("Finalized surface requires installed admitted field");RoadWidthProfileDto.Validate(profiles,region.roads);long preparedVertices=0,preparedIndices=0;var byId=profiles.ToDictionary(p=>p.roadId,StringComparer.Ordinal);var result=new FinalizedRoadSurfacePlan{Owner=region,WidthField=region.PreparedRoadWidths};
            foreach(var chain in topology.Continuations)
            {
                bool changed=chain.Parts.Any(part=>{var dto=byId[part.Road.id];return dto.bevelBoundary||dto.widths.Any(w=>Math.Abs(w-part.Road.width)>.0001f)||dto.derivatives.Any(d=>Math.Abs(d)>.00001f);});if(!changed||chain.Parts.Any(p=>p.Road.bridge))continue;float maximum=chain.Parts.Max(p=>region.PreparedRoadWidths.Maximum(p.Road));
                foreach(var part in chain.Parts){var mesh=RoadWidthStripGeometry.Create(part.Road,byId[part.Road.id],topology.Frames[part.Road.id],maximum,(x,z)=>region.HasBridgeApproach(part.Road,x,z)?region.RoadSurfaceHeightForRoad(part.Road,x,z):surface(x,z));if((preparedVertices+=mesh.Vertices.Length)>1000000||(preparedIndices+=mesh.Triangles.Length)>6000000)throw new InvalidDataException("Prepared road surface array budget");result.Parts.Add(part.Road.id,new Part(part.Road,mesh));yield return 0;}
            }
            // Include unchanged strips/bridges plus ALL effective node/corner caps.
            // For replaced roads discard old strips AND old wide-radius caps.
            var pavement=new List<RoadBankExteriorClip.Footprint>();int primitiveWork=0;
            void AddPavement(RoadBankExteriorClip.Footprint footprint){if(pavement.Count>=RoadBankExteriorClip.MaximumPavementPrimitives)throw new InvalidDataException("Prepared pavement primitive budget");pavement.Add(footprint);}
            foreach(var footprint in LegacyRoadPavementFootprints.BuildStrips(region)){if(!result.Parts.ContainsKey(footprint.Id))AddPavement(footprint);if(++primitiveWork%64==0)yield return 0;}
            foreach(var part in result.Parts.Values)foreach(var footprint in Triangles(part)){AddPavement(footprint);if(++primitiveWork%64==0)yield return 0;}
            var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var road in region.roads)if(!road.bridge&&!road.tunnel)
            {
                float arc=0;for(int i=0;i<road.points.Length;i++){if(i>0)arc+=Vector3.Distance(road.points[i-1],road.points[i]);bool endpoint=i==0||i==road.points.Length-1;bool sharp=!endpoint&&Vector3.Angle(road.points[i]-road.points[i-1],road.points[i+1]-road.points[i])>60;bool forced=!endpoint&&result.Parts.TryGetValue(road.id,out var part)&&part.Surface.Corners.Any(c=>Math.Abs(c.Distance-arc)<.01);if(!endpoint&&!sharp&&!forced)continue;string id=endpoint?(i==0?road.start:road.end):road.id+"/"+i;string key=road.layer+"/"+id;if(!seen.Add(key))continue;float radius=endpoint?region.JunctionRadius(id,region.EffectiveWidth(road,arc)*.5f,road.layer):region.EffectiveWidth(road,arc)*.5f;var p=road.points[i];for(int n=0;n<32;n++){double a=n*Math.PI/16,b=(n+1)*Math.PI/16;AddPavement(new RoadBankExteriorClip.Footprint{Id="cap/"+key,Layer=road.layer,Polygon=new[]{new Vector2(p.x,p.z),new Vector2(p.x+(float)Math.Cos(a)*radius,p.z+(float)Math.Sin(a)*radius),new Vector2(p.x+(float)Math.Cos(b)*radius,p.z+(float)Math.Sin(b)*radius)}});}yield return 0;}
            }
            result.Pavement=new RoadBankExteriorClip.SpatialIndex(pavement);finish(result);
        }
    }
}
