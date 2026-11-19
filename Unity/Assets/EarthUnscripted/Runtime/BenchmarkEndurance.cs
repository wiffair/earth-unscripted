using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace EarthUnscripted
{
    // The fixed route is a horizontal steering plan, never a road-height authority.
    // Physical support, source acquisition and streaming brakes remain normal game logic.
    public sealed class BenchmarkEndurance
    {
        [Serializable] sealed class Spec {public Vector3[] points;public float distanceMetres,m32StartMetres;}
        static bool? active;static Spec spec;
        public static bool Active=>active??(active=BenchmarkDrives.Endurance).Value;
        static Spec Specification=>spec??(spec=JsonUtility.FromJson<Spec>(Resources.Load<TextAsset>("Benchmarks/M4M32Route").text));
        public static float RouteLength=>Specification.distanceMetres;
        public static float TargetMph(float station)=>station<Specification.m32StartMetres?100:70;
        public static RoutePath Guide {get;private set;}
        readonly GameSession session;
        readonly HashSet<string> bridgeWays=new HashSet<string>(),buildingWays=new HashSet<string>(),overheadCells=new HashSet<string>(),travelCells=new HashSet<string>();
        float nextObservation,nextAim;Vector3 cachedAim;bool cachedAimReady;
        double seconds60,seconds68,metres60,metres68,metres95M4,metres65M32,bridgeMetres,brakeSeconds;
        int poseJumps;float maxOffRoute;Vector3 lastObserved;bool haveObservation;
        public BenchmarkEndurance(GameSession owner)
        {
            session=owner;var region=owner.World.Route.Region;var old=region.route;
            try{region.route=Specification.points;Guide=new RoutePath(region);}finally{region.route=old;}
            // Route Y is deliberately absent. Use it for source/geometry priority only;
            // the normal physical corridor follows the streamed 3D road graph.
            owner.World.AutomaticWorld.Renderer.SetPlannedRoute(Guide.Points,true);
        }
        public bool TryLane(float station,out Vector3 point)
        {
            var raw=Guide.At(station);var preferred=Guide.PlanarForward(station);var region=session.World.AutomaticWorld.Region;
            GeoRoad selected=null;Vector3 centre=default;float score=float.PositiveInfinity;
            // Bounded resident graph only; this opt-in driver does not request the whole route.
            foreach(var road in region.roads)
            {
                if(road.points==null)continue;
                for(int i=1;i<road.points.Length;i++)
                {
                    var a=road.points[i-1];var delta=road.points[i]-a;delta.y=0;
                    float t=Mathf.Clamp01(Vector3.Dot(raw-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                    var q=Vector3.Lerp(a,road.points[i],t);var gap=Vector3.ProjectOnPlane(q-raw,Vector3.up).sqrMagnitude;if(gap>20*20)continue;
                    var direction=delta.normalized;float alignment=Vector3.Dot(direction,preferred);if(road.oneWay<0)alignment=-alignment;else if(road.oneWay==0)alignment=Mathf.Abs(alignment);
                    if(alignment<.65f)continue;
                    float candidate=gap+(1-alignment)*25+(road.kind=="motorway"||road.kind=="motorway_link"||road.kind=="trunk"?0:25);
                    if(candidate>=score)continue;score=candidate;selected=road;centre=q;
                }
            }
            point=default;if(selected==null)return false;
            point=region.DrivingLanePoint(selected,centre,preferred,out _);return true;
        }
        public bool Aim(float station,out Vector3 point)
        {
            if(Time.unscaledTime>=nextAim){nextAim=Time.unscaledTime+.1f;cachedAimReady=TryLane(station,out cachedAim);}
            point=cachedAim;return cachedAimReady;
        }
        public void Observe(float distance,float simulationSeconds,Vector3 previous)
        {
            float mph=session.Car.AbsoluteSpeed*2.2369363f;
            if(mph>=60){seconds60+=simulationSeconds;metres60+=distance;}
            if(mph>=68){seconds68+=simulationSeconds;metres68+=distance;}
            if(session.Car.StreamingBrake)brakeSeconds+=Time.unscaledDeltaTime;
            if(distance>Mathf.Max(40,session.Car.AbsoluteSpeed*(simulationSeconds+.1f)*2))poseJumps++;
            var p=session.Car.Body.position;float station=Guide.Closest(p,out _,out float gap);if(TargetMph(station)==100&&mph>=95)metres95M4+=distance;if(TargetMph(station)==70&&mph>=65)metres65M32+=distance;maxOffRoute=Mathf.Max(maxOffRoute,gap);
            if(session.CurrentRoad?.bridge==true){bridgeMetres+=distance;bridgeWays.Add(session.CurrentRoad.NativeSourceWay??session.CurrentRoad.id);}
            travelCells.Add(Mathf.FloorToInt(p.x/256)+":"+Mathf.FloorToInt(p.z/256));
            if(Time.unscaledTime<nextObservation)return;nextObservation=Time.unscaledTime+.25f;
            var root=session.World.AutomaticWorld.Renderer;
            // Ray down from above to hit front-facing road decks; an upward ray can
            // miss one-sided mesh triangles. Sweep at 4m spacing between observations.
            if(!haveObservation){lastObserved=previous;haveObservation=true;}
            float length=Vector3.Distance(lastObserved,p);int samples=Mathf.Clamp(Mathf.CeilToInt(length/4),1,16);
            for(int i=0;i<=samples;i++)
            {
                var at=Vector3.Lerp(lastObserved,p,(float)i/samples);
                foreach(var hit in Physics.RaycastAll(at+Vector3.up*40,Vector3.down,37,~0,QueryTriggerInteraction.Ignore))
                    if(hit.collider.transform.IsChildOf(root.transform)&&hit.collider.name=="Bridge decks / barriers"&&hit.point.y-at.y>3)
                        overheadCells.Add(Mathf.FloorToInt(hit.point.x/64)+":"+Mathf.FloorToInt(hit.point.z/64));
            }
            lastObserved=p;
            // This counts published nearby building source features, not collision jobs.
            foreach(var map in root.ResidentAreas)foreach(var feature in map.buildings)
            {
                if(feature.points==null||feature.points.Length==0)continue;
                var c=feature.points[0];if(Vector3.ProjectOnPlane(c-p,Vector3.up).sqrMagnitude>120*120||!root.HasBuildingVisualAt(c))continue;
                buildingWays.Add(feature.id);
            }
        }
        public bool Qualified=>metres95M4>=10000&&metres65M32>=2000&&bridgeMetres>=100&&overheadCells.Count>0&&buildingWays.Count>0&&poseJumps==0&&maxOffRoute<20;
        public JObject Report()=>new JObject{["routeName"]="M4 J23A Magor → M4 J19 → M32 → Newfoundland Way, Bristol",["plannedMetres"]=RouteLength,["secondsAt60MphPlus"]=seconds60,["metresAt60MphPlus"]=metres60,["secondsAt68MphPlus"]=seconds68,["metresAt68MphPlus"]=metres68,["metresAt95MphPlusOnM4"]=metres95M4,["metresAt65MphPlusOnM32"]=metres65M32,["targetMphM4"]=100,["targetMphM32"]=70,["onBridgeMetres"]=bridgeMetres,["bridgeSourceWays"]=JArray.FromObject(bridgeWays),["overheadBridge64mCells"]=overheadCells.Count,["publishedBuildingSourceFeaturesWithin120m"]=buildingWays.Count,["visited256mCells"]=travelCells.Count,["streamingBrakeSeconds"]=brakeSeconds,["poseJumps"]=poseJumps,["maxOffPlanMetres"]=maxOffRoute,["coverageQualified"]=Qualified,["qualification"]="10 km physically driven at >=95 mph on M4, 2 km at >=65 mph on M32, >=100m on a mapped bridge, a physical bridge overhead, published roadside buildings, no pose jump or >20m deviation. Cell/source-feature counts are not individual structure counts."};
    }
}
