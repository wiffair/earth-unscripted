using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Pose=EarthUnscripted.Candidates.Articulated.Pose;
using EarthUnscripted.Candidates.Articulated;
using EarthUnscripted.Candidates.Compatibility;
using NativeWorld=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted
{
    public sealed partial class RailTrainSystem
    {
        bool ClearSpawn(Pose pose)
        {
            if(pose.status!="active")return false;
            for(int car=0;car<pose.cars.Count;car++)
            {
                var spec=formation.Vehicles[car];var frame=pose.cars[car].body;
                var origin=RailTrainVisual.ToUnity(frame.origin);var rotation=Quaternion.LookRotation(RailTrainVisual.ToUnity(frame.forward),RailTrainVisual.ToUnity(frame.up));
                int count=Physics.OverlapBoxNonAlloc(origin+rotation*new Vector3(0,(spec.Height+.2f)*.5f,0),new Vector3(spec.Width*.5f,(spec.Height-.2f)*.5f,spec.Length*.5f+.15f),overlaps,rotation,~0,QueryTriggerInteraction.Ignore);
                if(count>=overlaps.Length)return false;
                for(int i=0;i<count;i++)
                {
                    var collider=overlaps[i];if(!collider)continue;
                    if(visual!=null&&collider.transform.IsChildOf(visual.Root.transform))continue;
                    return false;
                }
            }
            return true;
        }
        public static bool BuildServiceRoute(TrainServicePlan service,IEnumerable<NativeWorld.Feature> features,out RailRoute path)
        {
            path=null;var sources=features.Where(f=>f!=null&&!string.IsNullOrEmpty(f.sourceId)&&f.category=="rail"&&f.kind=="rail"&&!f.tunnel&&f.points?.Length>=2)
                .GroupBy(f=>f.sourceId,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderByDescending(f=>f.points.Length).First(),StringComparer.Ordinal);
            var points=new List<Vec3>();
            foreach(string requested in service.SourceWays)
            {
                bool reverse=requested.StartsWith("~",StringComparison.Ordinal);string id=reverse?requested.Substring(1):requested;
                if(!sources.TryGetValue(id,out var feature))return false;
                if(!string.IsNullOrWhiteSpace(feature.gauge)&&(!float.TryParse(feature.gauge,NumberStyles.Float,CultureInfo.InvariantCulture,out float gauge)||Mathf.Abs(gauge-(TrainStock.Find(service.StockId)?.Gauge??0)*1000)>.1f))return false;
                var section=reverse?feature.points.Reverse().ToArray():feature.points;
                if(points.Count>0)
                {
                    var previous=RailTrainVisual.ToUnity(points[points.Count-1]);if(Vector3.Distance(previous,section[0])>.6f)return false;
                    if(points.Count>1&&Vector3.Angle(previous-RailTrainVisual.ToUnity(points[points.Count-2]),section[1]-section[0])>60)return false;
                }
                foreach(var point in section)
                {
                    if(!float.IsFinite(point.sqrMagnitude))return false;
                    if(points.Count>0)
                    {
                        var delta=point-RailTrainVisual.ToUnity(points[points.Count-1]);float flat=new Vector2(delta.x,delta.z).magnitude;
                        if(delta.sqrMagnitude<.000001f)continue;if(flat<.001f||Mathf.Abs(delta.y)/flat>.08f)return false;
                    }
                    points.Add(new Vec3(point.x,point.y,point.z));
                }
            }
            if(points.Count<2)return false;var result=new RailRoute(points);
            if(service.Calls.Any(c=>c.Metres<0||c.Metres>result.Length)||service.Reports.Any(r=>r.Metres<0||r.Metres>result.Length))return false;
            path=result;return true;
        }
        IEnumerator PrepareService()
        {
            var snapshot=master.feed.Snapshot;
            if(snapshot==null){Status=master.feed.Status;yield break;}
            var features=renderer.ResidentAreas.SelectMany(a=>a.features??Array.Empty<NativeWorld.Feature>()).ToArray();
            foreach(var plan in snapshot.Services)
            {
                if(plan.Cancelled||All.Any(t=>t!=this&&t.service?.Id==plan.Id))continue;
                var stock=TrainStock.Find(plan.StockId);var position=plan.Sample(TrainServiceFeed.Now,session.Profile.trainServiceMode==2,stock.MaximumSpeed);
                if(!position.Available)continue;
                if(plan.PublishedPath!=null){formation=stock;if(TryPublishedService(plan,position))yield break;continue;}
                if(!BuildServiceRoute(plan,features,out var trial))continue;
                formation=stock;double station=position.Metres+(plan.Direction<0?formation.Length:0);
                var pose=formation.Pose(trial,station);if(pose.status!="active")continue;
                var centre=RailTrainVisual.ToUnity(pose.cars[0].body.origin);
                if((centre-session.Car.Body.position).sqrMagnitude>900*900)continue;
                if(pose.cars.Any(car=>!renderer.HasGeometryAt(RailTrainVisual.ToUnity(car.body.origin)))||!ClearSpawn(pose))continue;
                route=trial;service=plan;travelDirection=plan.Direction;PositionQuality=position.Quality;
                head=publishedHead=station;speed=position.Speed;SourceWay=string.Join(",",plan.SourceWays);
                visual=new RailTrainVisual(transform,formation);visual.SetPose(pose,true);nextPose=Time.time;
                Status=plan.Id+" · "+formation.Name+" · "+position.Quality.ToString().ToLowerInvariant();yield break;
            }
            Status="Waiting for an active service on clear, loaded track";
        }
    }
}
