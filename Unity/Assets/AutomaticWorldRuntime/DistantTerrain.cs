using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace RoadRush.NewbridgePrototype
{
    public sealed partial class NewbridgeWorld
    {
        [Serializable] sealed class FarRequest { public float x,z,headingX,headingZ; public Vector3[] anchors; }
        [Serializable] public sealed class FarPatch { public float minX,minZ,step,verticalOffset; public int nx,nz; public float[] heights; public bool cached; public double seconds; }
        [Serializable] sealed class FarEvidence
        {
            public int meshes,visibleQuads,hiddenQuads,vertices,triangles,failedRequests;
            public float radiusMetres=8192,gridMetres=64,cameraFarMetres=12000;
            public bool hasColliders;
            public double latestBuildMilliseconds,latestFetchSeconds;
            public float verticalOffset;
            public float[] visibleDistanceByDirection;
        }
        readonly FarEvidence farEvidence=new FarEvidence();
        GameObject farRoot;
        FarPatch farPatch;
        Vector3 lastFarCentre=new Vector3(float.MaxValue,0,0);

        float CoarseHeight(float x,float z)
        {
            float gx=Mathf.Clamp((x-farPatch.minX)/farPatch.step,0,farPatch.nx-1),gz=Mathf.Clamp((z-farPatch.minZ)/farPatch.step,0,farPatch.nz-1);
            int ix=Mathf.Min((int)gx,farPatch.nx-2),iz=Mathf.Min((int)gz,farPatch.nz-2);
            float value=Mathf.Lerp(Mathf.Lerp(farPatch.heights[iz*farPatch.nx+ix],farPatch.heights[iz*farPatch.nx+ix+1],gx-ix),Mathf.Lerp(farPatch.heights[(iz+1)*farPatch.nx+ix],farPatch.heights[(iz+1)*farPatch.nx+ix+1],gx-ix),gz-iz);
            return value+farPatch.verticalOffset-(map.frame!=null?map.frame.height_origin:100);
        }

        bool FineNearVertex(float x,float z,out Map owner)
        {
            int ix=Mathf.FloorToInt(x/ChunkSize),iz=Mathf.FloorToInt(z/ChunkSize);
            for(int dz=-1;dz<=0;dz++)for(int dx=-1;dx<=0;dx++)
            {
                var key=new Vector2Int(ix+dx,iz+dz);
                if(!chunks.ContainsKey(key))continue;
                owner=AreaAt((key.x+.5f)*ChunkSize,(key.y+.5f)*ChunkSize);return true;
            }
            owner=null;return false;
        }

        float SceneryHeight(float x,float z)
        {
            var source=AreaAt(x,z);
            if(FineNearVertex(x,z,out var adjacent))
            {
                var previous=workingMap;workingMap=source??adjacent;
                try {return Ground(x,z);} finally {workingMap=previous;}
            }
            if(source!=null)return RawHeight(x,z);
            return CoarseHeight(x,z);
        }

        IEnumerator DistantTerrainLoop()
        {
            view.farClipPlane=12000;
            RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=2500;RenderSettings.fogEndDistance=11000;
            int lastRevision=-1;Vector3 lastEye=new Vector3(float.MaxValue,0,0);
            while(ready)
            {
                // The normal host may replace its loading camera with the
                // driving camera, without transferring either one's ownership.
                if(!view){yield return null;continue;}
                if(farPatch==null || Vector2.Distance(new Vector2(observer.x,observer.z),new Vector2(lastFarCentre.x,lastFarCentre.z))>512)
                {
                    var anchors=new List<Vector3>();
                    for(int z=-384;z<=384;z+=256)for(int x=-384;x<=384;x+=256)
                    {
                        float east=observer.x+x,north=observer.z+z;
                        if(AreaAt(east,north)!=null)anchors.Add(new Vector3(east,RawHeight(east,north)+(map.frame!=null?map.frame.height_origin:100),north));
                    }
                    var position=observer;
                    using(var exchange=new UnityWebRequest(endpoint+"/far","POST"))
                    {
                        exchange.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new FarRequest{x=position.x,z=position.z,anchors=anchors.ToArray()})));
                        exchange.downloadHandler=new DownloadHandlerBuffer();exchange.SetRequestHeader("Content-Type","application/json");exchange.timeout=40;
                        yield return exchange.SendWebRequest();
                        if(exchange.result==UnityWebRequest.Result.Success)
                        {
                            string text=exchange.downloadHandler.text;
                            var parsing=Task.Run(()=>JsonUtility.FromJson<FarPatch>(text));
                            while(!parsing.IsCompleted)yield return null;
                            if(!parsing.IsFaulted && parsing.Result.heights.Length==parsing.Result.nx*parsing.Result.nz)
                            {
                                farPatch=parsing.Result;lastFarCentre=position;
                                farEvidence.latestFetchSeconds=farPatch.seconds;farEvidence.verticalOffset=farPatch.verticalOffset;
                            }
                            else farEvidence.failedRequests++;
                        }
                        else farEvidence.failedRequests++;
                    }
                }
                int revision=report.generatedChunks+report.retiredChunks+areas.Count;
                if(farPatch!=null && (revision!=lastRevision || Vector3.Distance(view.transform.position,lastEye)>64))
                {
                    lastRevision=revision;lastEye=view.transform.position;
                    yield return BuildDistantTerrain(lastEye);
                }
                yield return new WaitForSecondsRealtime(.5f);
            }
        }

        IEnumerator BuildDistantTerrain(Vector3 eye)
        {
            var total=Stopwatch.StartNew();var slice=Stopwatch.StartNew();
            const int Directions=72,Rings=128;const float RingMetres=64;
            var horizon=new float[Directions,Rings+1];
            for(int direction=0;direction<Directions;direction++)
            {
                float angle=direction*Mathf.PI*2/Directions;float maximum=-Mathf.PI*.5f;
                horizon[direction,0]=maximum;
                for(int ring=1;ring<=Rings;ring++)
                {
                    if(ring%16==0 && slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant horizon",slice.Elapsed.TotalMilliseconds);yield return null;slice.Restart();}
                    float distance=ring*RingMetres;
                    float x=eye.x+Mathf.Sin(angle)*distance,z=eye.z+Mathf.Cos(angle)*distance;
                    maximum=Mathf.Max(maximum,Mathf.Atan2(SceneryHeight(x,z)-eye.y,distance));horizon[direction,ring]=maximum;
                }
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
            }
            var vertices=new List<Vector3>(farPatch.nx*farPatch.nz);var triangles=new List<int>();
            for(int z=0;z<farPatch.nz;z++)
            {
                for(int x=0;x<farPatch.nx;x++)
                {
                    if(x%16==0 && slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant vertices",slice.Elapsed.TotalMilliseconds);yield return null;slice.Restart();}
                    float east=farPatch.minX+x*farPatch.step,north=farPatch.minZ+z*farPatch.step;
                    vertices.Add(new Vector3(east,SceneryHeight(east,north),north));
                }
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
            }
            int visible=0,hidden=0;var visibleDistance=new float[Directions];
            bool HorizonVisible(Vector3 point)
            {
                var offset=point-eye;float distance=new Vector2(offset.x,offset.z).magnitude;
                if(distance<1600)return true;
                int direction=Mathf.RoundToInt(Mathf.Atan2(offset.x,offset.z)*Directions/(2*Mathf.PI));direction=(direction%Directions+Directions)%Directions;
                int ring=Mathf.Clamp(Mathf.FloorToInt(distance/RingMetres)-2,0,Rings);
                bool result=Mathf.Atan2(offset.y,distance)>=horizon[direction,ring]-1.5f*Mathf.Deg2Rad;
                if(result)visibleDistance[direction]=Mathf.Max(visibleDistance[direction],distance);
                return result;
            }
            for(int z=0;z<farPatch.nz-1;z++)
            {
                for(int x=0;x<farPatch.nx-1;x++)
                {
                    if(x%16==0 && slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant boundary",slice.Elapsed.TotalMilliseconds);yield return null;slice.Restart();}
                    int a=z*farPatch.nx+x,b=(z+1)*farPatch.nx+x,c=b+1,d=a+1;
                    var centre=(vertices[a]+vertices[c])*.5f;var key=ChunkKey(centre);
                    if(chunks.ContainsKey(key))continue;
                    if(!HorizonVisible(vertices[a])&&!HorizonVisible(vertices[b])&&!HorizonVisible(vertices[c])&&!HorizonVisible(vertices[d])){hidden++;continue;}
                    visible++;
                    // The ordinary far cell needs no temporary arrays. Only
                    // fine-terrain boundary cells require edge subdivision.
                    if(chunks.ContainsKey(key+Vector2Int.left)||chunks.ContainsKey(key+Vector2Int.up)||chunks.ContainsKey(key+Vector2Int.right)||chunks.ContainsKey(key+Vector2Int.down))
                    {
                        var corners=new[]{a,b,c,d};
                        var neighbours=new[]{key+Vector2Int.left,key+Vector2Int.up,key+Vector2Int.right,key+Vector2Int.down};
                        var boundary=new List<int>();
                        for(int edge=0;edge<4;edge++)
                        {
                            var first=vertices[corners[edge]];var last=vertices[corners[(edge+1)%4]];
                            int segments=chunks.ContainsKey(neighbours[edge])?32:1;
                            for(int step=0;step<segments;step++)
                            {
                                var point=Vector3.Lerp(first,last,(float)step/segments);point.y=SceneryHeight(point.x,point.z);
                                boundary.Add(vertices.Count);vertices.Add(point);
                            }
                        }
                        centre.y=SceneryHeight(centre.x,centre.z);int middle=vertices.Count;vertices.Add(centre);
                        for(int edge=0;edge<boundary.Count;edge++){triangles.Add(middle);triangles.Add(boundary[edge]);triangles.Add(boundary[(edge+1)%boundary.Count]);}
                    }
                    else {triangles.Add(a);triangles.Add(b);triangles.Add(c);triangles.Add(a);triangles.Add(c);triangles.Add(d);}
                }
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;slice.Restart();}
            }
            slice.Restart();
            var mesh=new Mesh{name="Visible distant terrain",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var root=new GameObject("Distant terrain — visual only");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<RoadRush.GeneratedMeshOwner>().Mesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=grass;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(farRoot)DestroyChunk(farRoot);farRoot=root;ownedRoots.Add(root);
            RecordGenerationSlice("Distant mesh publication",slice.Elapsed.TotalMilliseconds);
            farEvidence.meshes++;farEvidence.visibleQuads=visible;farEvidence.hiddenQuads=hidden;farEvidence.vertices=vertices.Count;farEvidence.triangles=triangles.Count/3;
            farEvidence.hasColliders=root.GetComponentsInChildren<Collider>().Length>0;farEvidence.latestBuildMilliseconds=total.Elapsed.TotalMilliseconds;farEvidence.visibleDistanceByDirection=visibleDistance;
            if(capturing)File.WriteAllText(Path.Combine(output,"far-terrain.json"),JsonUtility.ToJson(farEvidence,true));
        }
    }
}
