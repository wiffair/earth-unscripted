using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class DrivingDataPriorityVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool condition,string reason){if(!condition)throw new Exception("DRIVING DATA PRIORITY FAIL "+reason);checks++;}
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Hidden).SetValue(target,value);
        static object Candidate(Native world,string method)=>typeof(Native).GetMethod(method,Hidden).Invoke(world,null);
        static Vector2Int Key(object candidate)=>new Vector2Int((int)candidate.GetType().GetField("tileX").GetValue(candidate),(int)candidate.GetType().GetField("tileZ").GetValue(candidate));
        public static void Run()
        {
            checks=0;var root=new GameObject("Actual source-area priority verification");
            try
            {
                var world=root.AddComponent<Native>();world.enabled=false;
                var areas=(Dictionary<Vector2Int,Native.Map>)typeof(Native).GetField("areas",Hidden).GetValue(world);
                var retries=(Dictionary<Vector2Int,double>)typeof(Native).GetField("areaRetryAfter",Hidden).GetValue(world);
                var position=new Vector3(-790,0,-1507);Set(world,"observer",position);
                areas[new Vector2Int(-1,-1)]=new Native.Map();
                var corridor=Enumerable.Range(0,41).Select(i=>position+Vector3.back*i*8).ToList();
                world.SetDrivingCorridor(corridor,0);
                world.SetPlannedRoute(new[]{new Vector3(800,0,-1507),new Vector3(800,0,-4500)});
                Check(Key(Candidate(world,"PlannedDataCandidate"))==new Vector2Int(0,-1),"off-route navigation independently selects a different source core");
                Check(world.PendingGeometryChunks==0,"missing-source reproduction has no queued geometry");
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(-1,-2),"stopped car's first missing tunnel core precedes navigation with zero road jobs");
                Check(!world.HasPreparedAreaAt(corridor[4])&&world.HasPreparedAreaAt(corridor[3]),"negative boundary belongs to the next actual source core");
                // The same preferred-candidate selector drives both the request
                // pump and cooperative pending-job supersession.
                var urgent=Candidate(world,"PreferredDataCandidate");
                Check(Key(urgent)==new Vector2Int(-1,-2),"pending tunnel request is not replaced by the off-route planned candidate");
                retries[new Vector2Int(-1,-2)]=double.MaxValue;
                Check(Candidate(world,"DrivingDataCandidate")==null,"first missing source honours retry cooldown");
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(0,-1),"optional route work can progress during urgent retry cooldown");
                retries.Clear();
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(-1,-2),"eligible urgent source supersedes optional work after cooldown");
                areas[new Vector2Int(-1,-2)]=new Native.Map();
                Check(Candidate(world,"DrivingDataCandidate")==null,"resident core is not repeatedly downloaded for an unfinished collider");
                Check(!world.HasDrivingSurfaceAt(corridor[4]),"source residency never substitutes for physical road support");
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(0,-1),"route prefetch resumes once actual corridor source is resident");
                world.SetPlannedRoute(null);Check(Candidate(world,"PreferredDataCandidate")==null,"no unnecessary source request when unplanned corridor is resident");
                areas.Remove(new Vector2Int(-1,-2));
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(-1,-2),"unplanned driving also requests its missing source first");
                foreach(var direction in new[]{Vector3.left,Vector3.right,Vector3.forward,Vector3.back})
                {
                    areas.Clear();areas[Vector2Int.zero]=new Native.Map();Set(world,"observer",new Vector3(768,0,768));
                    var points=Enumerable.Range(0,130).Select(i=>new Vector3(768,0,768)+direction*i*8).ToList();world.SetDrivingCorridor(points,30);
                    var expected=new Vector2Int(Mathf.FloorToInt((768+direction.x*1024)/1536),Mathf.FloorToInt((768+direction.z*1024)/1536));
                    Check(Key(Candidate(world,"PreferredDataCandidate"))==expected,"source priority covers "+direction+" boundary without coordinate-specific rules");
                }
                world.SetDrivingCorridor(new List<Vector3>(),0);Check(Candidate(world,"PreferredDataCandidate")==null,"empty forecast has no urgent source request");
                Set(world,"embedded",true);Set(world,"embeddedSpeed",0f);Check(world.SourcePlanningMetres>=3072,"idle time plans farther source cache without growing geometry window");
                Set(world,"embeddedSpeed",30f);Set(world,"longestSourcePreparationSeconds",180f);Check(world.SourcePlanningMetres>6000,"speed and measured source preparation time expand lookahead");
                Set(world,"embeddedSpeed",100f);Check(world.SourcePlanningMetres==7680,"source speculation remains bounded");
                Check(world.PlannedSourcePlanningMetres==15360,"active-route disk speculation has its own hard15.36km bound");
                Set(world,"embeddedSpeed",0f);Set(world,"longestSourcePreparationSeconds",90f);Set(world,"observer",new Vector3(768,0,768));areas.Clear();
                for(int i=0;i<3;i++)areas[new Vector2Int(i,0)]=new Native.Map();
                world.SetPlannedRoute(new[]{new Vector3(768,0,768),new Vector3(16000,0,768)},true);
                Check(world.SourcePlanningMetres==3072&&world.PlannedSourcePlanningMetres==6144,"longer route cache lead never expands general branch forecast");
                var farther=Candidate(world,"PreferredDataCandidate");Check(Key(farther)==new Vector2Int(3,0),"idle worker can prepare route cores beyond the general source horizon");
                bool Resident(object value)=>(bool)typeof(Native).GetMethod("NeedsResidentSource",Hidden).Invoke(world,new[]{value});
                Check(!Resident(farther)&&areas.Count==3&&!world.HasPreparedAreaAt(new Vector3(4700,0,768)),"far planned core is disk-only and cannot advertise residency or driving support");
                var cached=(HashSet<Vector2Int>)typeof(Native).GetField("preparedOnlyTiles",Hidden).GetValue(world);cached.Add(new Vector2Int(3,0));
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(4,0),"completed distant cache core is not repeatedly prepared");
                Set(world,"observer",new Vector3(4500,0,768));var promotion=Candidate(world,"PreferredDataCandidate");
                Check(Key(promotion)==new Vector2Int(3,0)&&Resident(promotion),"cached route core promotes through the original resident window");
                world.SetDrivingCorridor(new List<Vector3>{new Vector3(4500,0,768),new Vector3(4500,0,-1)},0);
                Check(Key(Candidate(world,"PreferredDataCandidate"))==new Vector2Int(2,-1),"actual off-route driving still preempts farther planned cache work");
                world.SetDrivingCorridor(new List<Vector3>(),0);world.SetPlannedRoute(null,true);
                Check(Candidate(world,"PlannedDataCandidate")==null,"cancelling navigation removes the extended cache plan immediately");
                Debug.Log("DRIVING DATA PRIORITY PASS "+checks+"; actual selectors, negative boundaries, route conflict and retry lifecycle");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
