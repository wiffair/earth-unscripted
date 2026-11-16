using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using EarthUnscripted.Candidates.Articulated;
using EarthUnscripted.Candidates.Compatibility;
using NativeWorld=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    /// <summary>
    /// Bounded local fleet and optional connected service playback. Every consist
    /// remains on admitted standard-gauge source geometry. Timetable estimates and
    /// received-position interpolation are explicitly distinguished from simulation.
    /// </summary>
    public sealed partial class RailTrainSystem : MonoBehaviour
    {
        const float PoseIntervalSeconds=.05f;
        const double EndMarginMetres=3;
        double ConsistLengthMetres=>formation.Length;
        float ServiceAcceleration=>formation.Acceleration;
        float ServiceBrake=>formation.Brake;
        float MaximumSpeedMps=>Mathf.Min(formation.MaximumSpeed,35);
        readonly Collider[] overlaps=new Collider[64];
        GameSession session;RailTrainVisual visual;RailRoute route;
        RailTrainSystem master;readonly List<RailTrainSystem> members=new List<RailTrainSystem>();
        TrainFormation formation;TrainServicePlan service;TrainServiceFeed feed;int ordinal,activeMode=-1;double publishedHead;
        public int ActiveFleetCount=>(master==this?members.Prepend(this):master.members.Prepend(master)).Count(t=>t&&t.Running);
        public TrainFormation Formation=>formation;
        public string FeedStatus=>(master==this?feed:master.feed)?.Status??"No rail data connected";
        public IReadOnlyList<RailTrainSystem> Members=>members;
        public TrainPositionQuality PositionQuality{get;private set;}
        public string ServiceId=>service?.Id??"";
        bool wasPaused;
        NativeWorld renderer;Coroutine preparing;float retryAt,nextPose,speed,dwell;
        double head;int travelDirection=1;bool disposed,checking;
        public string Status{get;private set;}="Looking for a clear nearby railway section";
        public bool Running=>visual!=null&&visual.Root.activeSelf;
        public double HeadMetres=>head;
        public double TravelledMetres{get;private set;}
        public double SectionLengthMetres=>route?.Length??0;
        public int TravelDirection=>travelDirection;
        public float SpeedMps=>speed;
        public string SourceWay{get;private set;}="";
        public RailTrainVisual Visual=>visual;
        public void Initialize(GameSession owner){Initialize(owner,this,0);}
        void Initialize(GameSession owner,RailTrainSystem root,int index)
        {
            session=owner;renderer=owner.World.AutomaticWorld.Renderer;master=root;ordinal=index;formation=TrainStock.Local(index);
            if(root==this){feed=gameObject.AddComponent<TrainServiceFeed>();feed.Initialize(owner);}
        }
        void Fleet()
        {
            int count=Mathf.Clamp(session.Profile.trainFleetLimit,1,8)-1;
            while(members.Count>count){var last=members[members.Count-1];members.RemoveAt(members.Count-1);if(last)Destroy(last.gameObject);}
            while(members.Count<count){var child=new GameObject("Owned local train service "+(members.Count+1));child.transform.SetParent(transform,false);var member=child.AddComponent<RailTrainSystem>();member.Initialize(session,this,members.Count+1);members.Add(member);}
        }
        IEnumerable<RailTrainSystem> All=>master.members.Prepend(master).Where(t=>t);
        bool Reserved(string way)=>All.Any(t=>t!=this&&t.route!=null&&t.SourceWay.Split(',').Contains(way));

        void Update()
        {
            using var timing=WorkTelemetry.Measure("frame.trains",null,8);
            if(!session||!session.BootReady||disposed)return;
            if(master==this)Fleet();
            int mode=session.Profile.trainServiceMode;
            if(activeMode!=mode){activeMode=mode;if(mode==0)formation=TrainStock.Local(ordinal);Retire("Train operating mode changed");retryAt=Time.time+ordinal*.5f;}
            bool allowed=session.Profile.simulatedTrains&&!session.Network.Active;
            if(!allowed){if(preparing!=null){StopCoroutine(preparing);preparing=null;checking=false;}visual?.Hide();return;}
            if(session.Menu){wasPaused=true;visual?.Draw(speed,session.Profile.volume,travelDirection,true);return;}
            if(route==null)
            {
                if(!checking&&Time.time>=retryAt){retryAt=Time.time+12;checking=true;preparing=StartCoroutine(WorkTelemetry.WrapCoroutine(Prepare()));}
                return;
            }
            if(service!=null)
            {
                var latest=master.feed.Snapshot?.Services.FirstOrDefault(p=>p.Id==service.Id);
                if(latest==null||latest.Cancelled||latest.PathId!=service.PathId||latest.StockId!=service.StockId){Retire("Service cancelled, removed or changed");return;}
                service=latest;
            }
            float seconds=Mathf.Min(Time.deltaTime,.1f);
            if(service!=null)
            {
                var sample=LocalSample(service.Sample(TrainServiceFeed.Now,mode==2,formation.MaximumSpeed));
                if(!sample.Available){Retire("Service is outside its operating time");return;}
                PositionQuality=sample.Quality;travelDirection=service.Direction;
                double wanted=sample.Metres+(travelDirection<0?ConsistLengthMetres:0),error=(wanted-head)*travelDirection;
                if(wasPaused&&Math.Abs(error)>15){wasPaused=false;Retire("Resynchronising the paused service on clear track");retryAt=Time.time+1;return;}
                if(publishedMap!=null&&((travelDirection>0&&head>=route.Length-4&&wanted>route.Length)||(travelDirection<0&&head<=ConsistLengthMetres+4&&wanted<ConsistLengthMetres))){Retire("Published service leaving this prepared railway section");retryAt=Time.time+1;return;}
                if(Math.Abs(error)>180&&Vector3.Distance(RailTrainVisual.ToUnity(route.At(Math.Max(0,Math.Min(route.Length,head)))),session.Car.Body.position)>120){Retire("Rebinding a distant service location");retryAt=Time.time+1;return;}
                float target=Mathf.Clamp(sample.Speed+(float)error*.12f,0,Mathf.Min(CurveSpeed(),formation.MaximumSpeed));
                double edge=travelDirection>0?route.Length-EndMarginMetres-head:head-ConsistLengthMetres-EndMarginMetres;
                double stop=double.IsFinite(sample.NextStop)?(sample.NextStop+(travelDirection<0?ConsistLengthMetres:0)-head)*travelDirection:edge;
                target=Mathf.Min(target,Mathf.Sqrt((float)Math.Max(0,2*ServiceBrake*Math.Min(edge,stop))));
                speed=Mathf.MoveTowards(speed,target,seconds*(target<speed?ServiceBrake:ServiceAcceleration));head+=travelDirection*Math.Min(Math.Max(0,edge),speed*seconds);TravelledMetres+=speed*seconds;
            }
            else if(dwell>0){dwell-=seconds;speed=0;if(dwell<=0){if(formation.Id.Contains("freight")){Retire("Simulated freight left its loaded section");return;}travelDirection=-travelDirection;}}
            else
            {
                double remaining=travelDirection>0?route.Length-EndMarginMetres-head:head-ConsistLengthMetres-EndMarginMetres;
                float target=Mathf.Min(CurveSpeed(),Mathf.Sqrt((float)Math.Max(0,2*ServiceBrake*(remaining-.3))));
                speed=Mathf.MoveTowards(speed,target,seconds*(target<speed?ServiceBrake:ServiceAcceleration));
                double step=Math.Min(Math.Max(0,remaining),speed*seconds);head+=travelDirection*step;TravelledMetres+=step;
                if(remaining<.5&&speed<.8f){speed=0;dwell=8;}
            }
            wasPaused=false;
            if(Time.time>=nextPose)
            {
                nextPose=Time.time+PoseIntervalSeconds;
                var pose=formation.Pose(route,head);
                if(pose.status!="active"){Retire("End of supported train path");return;}
                bool supported=true;
                foreach(var car in pose.cars)if(!renderer.HasGeometryAt(RailTrainVisual.ToUnity(car.body.origin))){supported=false;break;}
                if(!supported)
                {
                    head=publishedHead;speed=0;Status="Waiting for rendered railway coverage";
                    // Never display a train beyond the prepared environment. Keep
                    // its copied path and wait; no per-frame acquisition is added.
                    visual.Hide();return;
                }
                if(!visual.Root.activeSelf&&!ClearSpawn(pose)){speed=0;head=publishedHead;Status="Train waiting for a clear re-entry";return;}
                visual.SetPose(pose,!visual.Root.activeSelf);publishedHead=head;
                string source=service==null?"local simulation":PositionQuality==TrainPositionQuality.Reported?"reported position":PositionQuality==TrainPositionQuality.Interpolated?"between reported positions":(service?.PublishedPath!=null?"published timetable estimate":"timetable estimate");
                Status=formation.Name+(service!=null&&!service.StockConfirmed?" (estimated type)":"")+" · "+source+" · "+(dwell>0?"waiting at section end":Mathf.RoundToInt(speed*2.2369363f)+" mph");
            }
            visual.Draw(speed,session.Profile.volume,travelDirection,false);
            var position=RailTrainVisual.ToUnity(route.At(head));
            if(Vector3.Distance(position,session.Car.Body.position)>1400)Retire("Train retired beyond local scenery range");
        }

        /// <summary>Sample a supported rail point ahead in metres; never extrapolate past a section end.</summary>
        public bool TryPointAhead(float metres,out Vector3 point,out Quaternion rotation)
        {
            point=Vector3.zero;rotation=Quaternion.identity;if(route==null||!float.IsFinite(metres))return false;
            double station=(travelDirection>0?head:head-ConsistLengthMetres)+travelDirection*metres;if(station<2||station>route.Length-2)return false;
            var frame=route.AtFrame(station,1,1);point=RailTrainVisual.ToUnity(frame.origin);
            rotation=Quaternion.LookRotation(RailTrainVisual.ToUnity(frame.forward),RailTrainVisual.ToUnity(frame.up));return true;
        }
        void FixedUpdate(){if(session&&!session.Menu&&visual!=null&&!visual.FixedTick())Retire("Train retired: unsupported physics pose change");}
        float CurveSpeed()
        {
            double centre=Math.Max(11,Math.Min(route.Length-11,head-formation.Length*.5));
            var before=route.At(centre-10);var middle=route.At(centre);var after=route.At(centre+10);
            var a=RailTrainVisual.ToUnity(Vec.Sub(middle,before));var b=RailTrainVisual.ToUnity(Vec.Sub(after,middle));
            float turn=Vector3.Angle(a,b)*Mathf.Deg2Rad;
            return Mathf.Min(MaximumSpeedMps,Mathf.Sqrt(10/Mathf.Max(.001f,turn)*.7f));
        }
        sealed class Candidate{public NativeWorld.Feature Feature;public Vector3[] Points;public float Distance;}
        IEnumerable<Candidate> Candidates()
        {
            var observer=session.Car.Body.position;
            foreach(var group in renderer.ResidentAreas.SelectMany(area=>area.features??Array.Empty<NativeWorld.Feature>())
                .Where(feature=>feature.category=="rail"&&feature.kind=="rail"&&!feature.tunnel&&feature.points!=null)
                .GroupBy(feature=>feature.sourceId,StringComparer.Ordinal))
            {
                // Halo copies are not concatenated. Choose one complete source
                // section; absent or ambiguous connectivity remains unsupported.
                foreach(var feature in group.OrderByDescending(feature=>feature.points.Length).Take(1))
                {
                    if(!string.IsNullOrEmpty(feature.gauge)&&(!float.TryParse(feature.gauge,NumberStyles.Float,CultureInfo.InvariantCulture,out float gauge)||Mathf.Abs(gauge-1435)>.1f))continue;
                    var points=new List<Vector3>();List<Vector3> longest=null;float bestDistance=float.PositiveInfinity;
                    foreach(var point in feature.points)
                    {
                        if(!renderer.HasGeometryAt(point))
                        {if(points.Count>1&&(longest==null||points.Count>longest.Count))longest=points;points=new List<Vector3>();continue;}
                        if(points.Count==0||Vector3.Distance(points[points.Count-1],point)>.001f)points.Add(point);
                    }
                    if(points.Count>1&&(longest==null||points.Count>longest.Count))longest=points;
                    if(longest==null||longest.Count<20)continue;
                    float length=0;bool valid=true;
                    for(int index=1;index<longest.Count;index++)
                    {
                        var delta=longest[index]-longest[index-1];float flat=new Vector2(delta.x,delta.z).magnitude;
                        if(flat<.001f||Mathf.Abs(delta.y)/flat>.08f){valid=false;break;}
                        length+=delta.magnitude;bestDistance=Mathf.Min(bestDistance,Vector3.ProjectOnPlane(longest[index]-observer,Vector3.up).sqrMagnitude);
                    }
                    if(valid&&length>Math.Max(180,ConsistLengthMetres+65)&&bestDistance<900*900&&!Reserved(feature.sourceId))yield return new Candidate{Feature=feature,Points=longest.ToArray(),Distance=bestDistance};
                }
            }
        }
        IEnumerator Prepare()
        {
            try
            {
                string country=session.World.Route.Region.countryCode??"";
                if(session.Profile.trainServiceMode==0&&country!="GB"&&country!="UK"){Status="Included train types currently support UK standard-gauge sections";yield break;}
                if(session.Profile.trainServiceMode!=0){yield return PrepareService();yield break;}
                PositionQuality=TrainPositionQuality.Unavailable;
                foreach(var candidate in Candidates().OrderBy(item=>item.Distance).Take(8))
                {
                    var trial=new RailRoute(candidate.Points.Select(point=>new Vec3(point.x,point.y,point.z)));
                    bool clear=true;int work=0;
                    for(double at=15;at<trial.Length-15;at+=8)
                    {
                        var frame=trial.AtFrame(at,8,1);Vector3 origin=RailTrainVisual.ToUnity(frame.origin);
                        Quaternion rotation=Quaternion.LookRotation(RailTrainVisual.ToUnity(frame.forward),RailTrainVisual.ToUnity(frame.up));
                        int count=Physics.OverlapBoxNonAlloc(origin+rotation*new Vector3(0,2.125f,0),new Vector3(1.5f,1.975f,Mathf.Max(11.7f,formation.Vehicles.Max(v=>v.Length)*.5f)),overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
                        if(count==overlaps.Length){clear=false;break;}
                        for(int index=0;index<count;index++)
                        {
                            var collider=overlaps[index];if(collider.attachedRigidbody||collider.transform.IsChildOf(transform))continue;
                            clear=false;break;
                        }
                        if(!clear)break;
                        if(++work%4==0)yield return null;
                    }
                    if(!clear)continue;
                    double station=0,bestStation=0;float best=float.PositiveInfinity;
                    for(int index=1;index<candidate.Points.Length;index++)
                    {
                        station+=Vector3.Distance(candidate.Points[index-1],candidate.Points[index]);
                        float distance=(candidate.Points[index]-session.Car.Body.position).sqrMagnitude;
                        if(distance<best){best=distance;bestStation=station;}
                    }
                    double startHead=Math.Max(ConsistLengthMetres+EndMarginMetres,Math.Min(trial.Length-EndMarginMetres,bestStation+80));
                    var initialPose=formation.Pose(trial,startHead);
                    if(initialPose.status!="active"||!ClearSpawn(initialPose))continue;
                    route=trial;SourceWay=candidate.Feature.sourceId;head=publishedHead=startHead;
                    visual=new RailTrainVisual(transform,formation);visual.SetPose(initialPose,true);
                    nextPose=Time.time;Status=formation.Name+" · local simulation ready";yield break;
                }
                Status="No clear continuous railway section nearby; retrying as scenery loads";
            }
            finally{preparing=null;checking=false;}
        }
        void Retire(string reason)
        {if(preparing!=null){StopCoroutine(preparing);preparing=null;checking=false;}visual?.Dispose();visual=null;route=null;service=null;publishedMap=localMap=null;speed=0;dwell=0;retryAt=Time.time+12;Status=reason;}
        void OnDestroy(){disposed=true;if(preparing!=null)StopCoroutine(preparing);visual?.Dispose();visual=null;if(master==this){foreach(var member in members)if(member)Destroy(member.gameObject);if(feed)Destroy(feed);}}

    }
}
