using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        [Serializable] public sealed class JunctionGeometryEvidence
        {
            public int roadColliderSamples,groundSupportSamples,paintVertices,junctions,pavingSamples,islandSamples;
            public float maximumRoadErrorMetres,maximumGroundGapMetres;
            public List<string> failures=new List<string>();
        }

        /// <summary>
        /// Explicit QA only: inspect the published physical meshes around a
        /// parked car, plus the exact paint batches produced for those roads.
        /// Does not move the vehicle, mutate geometry, or run during normal play.
        /// </summary>
        public JunctionGeometryEvidence InspectJunctionGeometry(Vector3 centre,float radius,bool junctionMouthsOnly=false)
        {
            var evidence=new JunctionGeometryEvidence();
            var source=AreaAt(centre.x,centre.z)??workingMap;
            if(source==null){evidence.failures.Add("No owned map");return evidence;}
            var emptyRoad=new Batch("QA asphalt",asphalt,false);
            var emptyVerge=new Batch("QA verge",gravel,false);
            foreach(var feature in source.features)
            {
                if(feature.category!="road")continue;
                float station=0;
                var paintBatch=new Batch("QA paint",paint,false);
                bool inspected=false;
                for(int index=1;index<feature.points.Length;index++)
                {
                    var a=feature.points[index-1];var b=feature.points[index];
                    float length=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude;
                    var midpoint=(a+b)*.5f;
                    bool inMouth=!junctionMouthsOnly || feature.junctions!=null && feature.junctions.Any(join=>Vector2.Distance(new Vector2(midpoint.x,midpoint.z),new Vector2(join.x,join.z))<=join.radius);
                    if(inMouth && Vector3.ProjectOnPlane(midpoint-centre,Vector3.up).magnitude<radius)
                    {
                        var rightA=Join(feature.points,index-1);var rightB=Join(feature.points,index);
                        RoadSurface(emptyRoad,emptyVerge,paintBatch,feature,a,b,rightA,rightB,station);
                        inspected=true;
                        var right=(rightA+rightB)*.5f;
                        foreach(float fraction in new[]{-.7f,0,.7f})
                        {
                            var point=RoadSurfacePoint(feature,midpoint+right*(feature.width*.5f*fraction+feature.roadCentreOffset));
                            bool supported=false,ground=false;
                            float roadError=float.MaxValue,groundGap=float.MaxValue;
                            foreach(var hit in Physics.RaycastAll(point+Vector3.up*1.5f,Vector3.down,4))
                            {
                                if(!hit.collider.transform.IsChildOf(transform))continue;
                                if(hit.collider.name=="Road asphalt")
                                {
                                    roadError=Mathf.Min(roadError,Mathf.Abs(hit.point.y-point.y));
                                    supported|=Mathf.Abs(hit.point.y-point.y)<.16f;
                                }
                                if(hit.collider.name=="Terrain")
                                {
                                    groundGap=Mathf.Min(groundGap,point.y-hit.point.y);
                                    ground=true;
                                }
                            }
                            evidence.roadColliderSamples++;
                            if(roadError<float.MaxValue)evidence.maximumRoadErrorMetres=Mathf.Max(evidence.maximumRoadErrorMetres,roadError);
                            if(!supported && evidence.failures.Count<80)evidence.failures.Add("Road support "+feature.sourceId+" at "+point+" error "+roadError);
                            if(!feature.bridge&&!feature.tunnel)
                            {
                                evidence.groundSupportSamples++;
                                if(ground)evidence.maximumGroundGapMetres=Mathf.Max(evidence.maximumGroundGapMetres,groundGap);
                                if((!ground||groundGap>.65f||groundGap<-.16f)&&evidence.failures.Count<80)
                                    evidence.failures.Add("Ground support "+feature.sourceId+" at "+point+" gap "+groundGap);
                            }
                        }
                    }
                    station+=length;
                }
                if(!inspected)continue;
                evidence.junctions+=feature.junctions?.Length??0;
                // Inspect emitted geometry, not only a visibility predicate.
                // Each paint vertex must lie outside every owning mouth ribbon.
                foreach(var point in paintBatch.Vertices)
                {
                    evidence.paintVertices++;
                    if(feature.junctions==null)continue;
                    foreach(var join in feature.junctions)
                    {
                        if(!OwnerSurface(join,point,out float distance,out _))continue;
                        if(distance<join.owner.width*.5f+.10f && evidence.failures.Count<80)
                            evidence.failures.Add("Paint enters owner "+feature.sourceId+" -> "+join.ownerId+" at "+point);
                    }
                }
            }
            if(source.paving!=null)foreach(var key in source.paving.Keys)
            {
                if(Vector2.Distance(new Vector2((key.x+.5f)*ChunkSize,(key.y+.5f)*ChunkSize),new Vector2(centre.x,centre.z))>radius+ChunkSize)continue;
                var roadPaving=new Batch("QA junction pavement",asphalt,false);var islandPaving=new Batch("QA island pavement",path,false);
                foreach(var row in JunctionPavingRows(roadPaving,islandPaving,key.x*ChunkSize,key.y*ChunkSize)){}
                foreach(var batch in new[]{roadPaving,islandPaving})for(int i=0;i<batch.Indices.Count;i+=3)
                {
                    var a=batch.Vertices[batch.Indices[i]];var b=batch.Vertices[batch.Indices[i+1]];var c=batch.Vertices[batch.Indices[i+2]];
                    // Vertical kerb walls and sub-centimetre slivers do not
                    // provide a wheel-sized driving surface to probe.
                    if(Vector3.Cross(b-a,c-a).y<.004f)continue;var point=(a+b+c)/3;
                    if(Vector2.Distance(new Vector2(point.x,point.z),new Vector2(centre.x,centre.z))>radius)continue;
                    bool island=ReferenceEquals(batch,islandPaving),supported=false,obstructed=false;
                    foreach(var hit in Physics.RaycastAll(point+Vector3.up*1.5f,Vector3.down,3))
                    {
                        if(!hit.collider.transform.IsChildOf(transform))continue;
                        if(hit.collider.name==(island?"Footpaths":"Road asphalt")&&Mathf.Abs(hit.point.y-point.y)<.03f)supported=true;
                        if(hit.collider.name=="Terrain"&&hit.point.y>point.y+.015f)obstructed=true;
                    }
                    evidence.pavingSamples++;if(island)evidence.islandSamples++;
                    if((!supported||obstructed)&&evidence.failures.Count<80)evidence.failures.Add("Published paving support/terrain at "+point+" supported="+supported+" grass="+obstructed);
                }
            }
            return evidence;
        }
    }
}
