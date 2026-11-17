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

namespace EarthUnscripted.NewbridgePrototype
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
        bool farPatchIsFallback,publishedFarFallback;
        double nextFarAttempt;bool farFetching;int farPatchRevision;
        FarPatch pendingFarPatch;Vector3 pendingFarCentre;
        int publishedCoastRevision=-1;
        public bool DistantTerrainCoastReady=>publishedCoastRevision==coastalRevision;
        public bool DistantTerrainReady=>farRoot&&farRoot.activeInHierarchy;
        public bool DistantTerrainUsingResidentFallback=>publishedFarFallback;
        public int DistantTerrainVisibleQuads=>farEvidence.visibleQuads;
        public int DistantTerrainFailedRequests=>farEvidence.failedRequests;
        public int DistantTerrainBuildCount=>farEvidence.meshes;
        public double DistantTerrainLatestBuildMilliseconds=>farEvidence.latestBuildMilliseconds;
        public string DistantTerrainBuildPhase{get;private set;}="not-started";
        public int DistantTerrainBuildRow{get;private set;}
        public int DistantTerrainAbortedBuilds{get;private set;}
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
                if(!PublishedPhysicalTerrain(key,out _))continue;
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
            // Coarse products can contain land interpolation over a loch. The
            // same mapped-coast authority used by full chunks must carve these
            // visual floors too; polygon holes retain real islands.
            return LowerSeaBed(x,z,source!=null?RawHeight(x,z):CoarseHeight(x,z));
        }

        static bool ValidFarPatch(FarPatch patch)
        {
            if(patch==null||patch.nx<2||patch.nz<2||patch.nx>1025||patch.nz>1025||patch.step<=0||
                float.IsNaN(patch.minX)||float.IsNaN(patch.minZ)||float.IsInfinity(patch.minX)||float.IsInfinity(patch.minZ)||
                float.IsNaN(patch.step)||float.IsInfinity(patch.step)||float.IsNaN(patch.verticalOffset)||float.IsInfinity(patch.verticalOffset)||
                patch.heights==null||patch.heights.Length!=patch.nx*patch.nz)return false;
            foreach(float height in patch.heights)if(float.IsNaN(height)||float.IsInfinity(height))return false;
            return true;
        }

        // Render already prepared relief immediately while distant source data
        // is fetched. Otherwise distant buildings/trees appear without their
        // ground for the full duration of an optional network request.
        FarPatch ResidentTerrainPatch()
        {
            if(areas.Count==0)return null;
            const float step=64;
            float west=Mathf.Floor(areas.Values.Min(m=>m.coreMinX)/step)*step;
            float south=Mathf.Floor(areas.Values.Min(m=>m.coreMinZ)/step)*step;
            float east=Mathf.Ceil(areas.Values.Max(m=>m.coreMaxX)/step)*step;
            float north=Mathf.Ceil(areas.Values.Max(m=>m.coreMaxZ)/step)*step;
            int nx=Mathf.CeilToInt((east-west)/step)+1,nz=Mathf.CeilToInt((north-south)/step)+1;
            if(nx<2||nz<2||nx*nz>300000)return null;
            var patch=new FarPatch{minX=west,minZ=south,step=step,nx=nx,nz=nz,heights=new float[nx*nz]};
            float datum=map.frame!=null?map.frame.height_origin:100;
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                float px=west+x*step,pz=south+z*step;
                var source=AreaAt(px,pz)??areas.Values.FirstOrDefault(m=>px>=m.minX&&px<=m.maxX&&pz>=m.minZ&&pz<=m.maxZ);
                var previous=workingMap;
                try{workingMap=source;patch.heights[z*nx+x]=source!=null?RawHeight(px,pz)+datum:datum;}
                finally{workingMap=previous;}
            }
            return patch;
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
                view.farClipPlane=12000;
                // The fetch coroutine only stages immutable data. Adopt it
                // between complete mesh builds so sampling never mixes grids.
                if(pendingFarPatch!=null)
                {
                    farPatch=pendingFarPatch;pendingFarPatch=null;farPatchIsFallback=false;lastFarCentre=pendingFarCentre;farPatchRevision++;
                    farEvidence.latestFetchSeconds=farPatch.seconds;farEvidence.verticalOffset=farPatch.verticalOffset;
                }
                if(!farFetching&&Time.realtimeSinceStartupAsDouble>=nextFarAttempt&&(farPatch==null||farPatchIsFallback||Vector2.Distance(new Vector2(observer.x,observer.z),new Vector2(lastFarCentre.x,lastFarCentre.z))>512))
                {
                    StartCoroutine(FetchDistantPatch(observer));
                }
                if(farPatch==null)
                {
                    farPatch=ResidentTerrainPatch();farPatchIsFallback=farPatch!=null;
                    if(farPatchIsFallback)yield return BuildDistantTerrain(view.transform.position);
                }
                if(!view){yield return null;continue;}
                if(coastalVisualSuppressionPending){yield return null;continue;}
                int revision=fineTerrainVisibilityRevision+roadWaterSourceRevision+coastalRevision+farPatchRevision;
                if(farPatch!=null && (revision!=lastRevision || Vector3.Distance(view.transform.position,lastEye)>64))
                {
                    lastRevision=revision;lastEye=view.transform.position;
                    if(farPatchIsFallback)farPatch=ResidentTerrainPatch()??farPatch;
                    yield return BuildDistantTerrain(lastEye);
                }
                yield return new WaitForSecondsRealtime(.5f);
            }
        }

        IEnumerator FetchDistantPatch(Vector3 position)
        {
            farFetching=true;
            try
            {
                    var anchors=new List<Vector3>();
                    for(int z=-384;z<=384;z+=256)for(int x=-384;x<=384;x+=256)
                    {
                        float east=observer.x+x,north=observer.z+z;
                        if(AreaAt(east,north)!=null)anchors.Add(new Vector3(east,RawHeight(east,north)+(map.frame!=null?map.frame.height_origin:100),north));
                    }

                    using(var exchange=EarthUnscripted.WorkerHttp.Authorize(new UnityWebRequest(endpoint+"/far","POST")))
                    {
                        exchange.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new FarRequest{x=position.x,z=position.z,anchors=anchors.ToArray()})));
                        exchange.downloadHandler=new DownloadHandlerBuffer();exchange.SetRequestHeader("Content-Type","application/json");exchange.timeout=40;
                        yield return exchange.SendWebRequest();
                        if(exchange.result==UnityWebRequest.Result.Success)
                        {
                            string text=exchange.downloadHandler.text;
                            var parsing=Task.Run(()=>JsonUtility.FromJson<FarPatch>(text));
                            while(!parsing.IsCompleted)yield return null;
                            if(!parsing.IsFaulted && ValidFarPatch(parsing.Result))
                            {
                                pendingFarPatch=parsing.Result;pendingFarCentre=position;
                            }
                            else farEvidence.failedRequests++;
                        }
                        else farEvidence.failedRequests++;
                    }
                    nextFarAttempt=Time.realtimeSinceStartupAsDouble+5;
            }
            finally{farFetching=false;nextFarAttempt=Time.realtimeSinceStartupAsDouble+5;}
        }

        IEnumerator BuildDistantTerrain(Vector3 eye)
        {
            var total=Stopwatch.StartNew();var slice=Stopwatch.StartNew();int coastAtStart=coastalRevision;var patch=farPatch;bool residentFallback=farPatchIsFallback;
            DistantTerrainBuildPhase="vertices";DistantTerrainBuildRow=0;
            const int Directions=72;
            // Keep a continuous low-detail surface. Sparse radial horizon tests
            // can discard visible slopes between rays and leave scenery in air.
            // The camera's frustum and depth buffer perform visibility culling.
            var vertices=new List<Vector3>(patch.nx*patch.nz);var triangles=new List<int>();
            for(int z=0;z<patch.nz;z++)
            {
                if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}
                DistantTerrainBuildRow=z;
                for(int x=0;x<patch.nx;x++)
                {
                    if(slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant vertices",slice.Elapsed.TotalMilliseconds);yield return null;if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}slice.Restart();}
                    float east=patch.minX+x*patch.step,north=patch.minZ+z*patch.step;
                    vertices.Add(new Vector3(east,SceneryHeight(east,north),north));
                }
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}slice.Restart();}
            }
            DistantTerrainBuildPhase="triangles";
            var groundCells=new int[(patch.nx-1)*(patch.nz-1)*2];
            int visible=0,hidden=0;var visibleDistance=new float[Directions];
            for(int z=0;z<patch.nz-1;z++)
            {
                if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}
                DistantTerrainBuildRow=z;
                for(int x=0;x<patch.nx-1;x++)
                {
                    if(slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant boundary",slice.Elapsed.TotalMilliseconds);yield return null;if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}slice.Restart();}
                    int a=z*patch.nx+x,b=(z+1)*patch.nx+x,c=b+1,d=a+1;
                    var centre=(vertices[a]+vertices[c])*.5f;var key=ChunkKey(centre);
                    if(PublishedFineTerrain(key,out _))continue;
                    if(residentFallback&&AreaAt(centre.x,centre.z)==null)continue;
                    int groundCell=(z*(patch.nx-1)+x)*2;groundCells[groundCell]=triangles.Count;
                    visible++;
                    var offset=centre-eye;
                    int direction=(Mathf.RoundToInt(Mathf.Atan2(offset.x,offset.z)*Directions/(2*Mathf.PI))+Directions)%Directions;
                    visibleDistance[direction]=Mathf.Max(visibleDistance[direction],new Vector2(offset.x,offset.z).magnitude);
                    // The ordinary far cell needs no temporary arrays. Only
                    // fine-terrain boundary cells require edge subdivision.
                    if(PublishedFineTerrain(key+Vector2Int.left,out _)||PublishedFineTerrain(key+Vector2Int.up,out _)||PublishedFineTerrain(key+Vector2Int.right,out _)||PublishedFineTerrain(key+Vector2Int.down,out _))
                    {
                        var corners=new[]{a,b,c,d};
                        var neighbours=new[]{key+Vector2Int.left,key+Vector2Int.up,key+Vector2Int.right,key+Vector2Int.down};
                        var boundary=new List<int>();
                        for(int edge=0;edge<4;edge++)
                        {
                            var first=vertices[corners[edge]];var last=vertices[corners[(edge+1)%4]];
                            int segments=PublishedFineTerrain(neighbours[edge],out _)?32:1;
                            for(int step=0;step<segments;step++)
                            {
                                if(slice.Elapsed.TotalMilliseconds>2){RecordGenerationSlice("Distant edge sample",slice.Elapsed.TotalMilliseconds);yield return null;if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}slice.Restart();}
                                var point=Vector3.Lerp(first,last,(float)step/segments);point.y=SceneryHeight(point.x,point.z);
                                boundary.Add(vertices.Count);vertices.Add(point);
                            }
                        }
                        centre.y=SceneryHeight(centre.x,centre.z);int middle=vertices.Count;vertices.Add(centre);
                        for(int edge=0;edge<boundary.Count;edge++){triangles.Add(middle);triangles.Add(boundary[edge]);triangles.Add(boundary[(edge+1)%boundary.Count]);}
                    }
                    else {triangles.Add(a);triangles.Add(b);triangles.Add(c);triangles.Add(a);triangles.Add(c);triangles.Add(d);}
                    groundCells[groundCell+1]=triangles.Count-groundCells[groundCell];
                }
                if(slice.Elapsed.TotalMilliseconds>2){yield return null;if(coastAtStart!=coastalRevision){DistantTerrainAbortedBuilds++;DistantTerrainBuildPhase="coast-restart";yield break;}slice.Restart();}
            }
            // A coast can arrive while the frame-budgeted vertex loop yields.
            // Do not publish a mesh mixing pre-coast and carved elevations.
            if(coastAtStart!=coastalRevision||!ReferenceEquals(patch,farPatch))yield break;
            DistantTerrainBuildPhase="publication";slice.Restart();
            var mesh=new Mesh{name="Visible distant terrain",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var root=new GameObject("Distant terrain — visual only");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<EarthUnscripted.GeneratedMeshOwner>().Mesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=grass;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            if(farRoot){ownedRoots.Remove(farRoot);DestroyChunk(farRoot);}farRoot=root;publishedFarFallback=residentFallback;publishedCoastRevision=coastAtStart;ownedRoots.Add(root);
            publishedGroundVertices=vertices;publishedGroundTriangles=triangles;publishedGroundCells=groundCells;publishedGroundGrid=patch;
            sceneryGroundRevision++;
            RecordGenerationSlice("Distant mesh publication",slice.Elapsed.TotalMilliseconds);
            DistantTerrainBuildPhase="ready";farEvidence.meshes++;farEvidence.visibleQuads=visible;farEvidence.hiddenQuads=hidden;farEvidence.vertices=vertices.Count;farEvidence.triangles=triangles.Count/3;
            farEvidence.hasColliders=root.GetComponentsInChildren<Collider>().Length>0;farEvidence.latestBuildMilliseconds=total.Elapsed.TotalMilliseconds;farEvidence.visibleDistanceByDirection=visibleDistance;
            if(capturing)File.WriteAllText(Path.Combine(output,"far-terrain.json"),JsonUtility.ToJson(farEvidence,true));
        }
    }
}
