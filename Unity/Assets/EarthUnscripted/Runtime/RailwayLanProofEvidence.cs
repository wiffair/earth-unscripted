using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
namespace EarthUnscripted
{
    // Explicit LAN verification only. Never changes profiles, meshes or train/road geometry.
    public sealed class RailwayLanProofEvidence
    {
        [Serializable] public sealed class Crossing
        {public string railId,roadId,collider;public Vector3 point,underside,normal;public int triangle,hostUnderSamples,guestUnderSamples;public float railHead,roadY,postedHeight,requiredClearance,measuredClearance,deckDepth,minHostCollisionGap=1000,minGuestCollisionGap=1000;}
        [Serializable] public sealed class Evidence
        {public string compatibility,mapId,epoch,hostFingerprint,datasetVersion,datasetSource,railDigest,roadDigest,limitations;public bool profilesPrepared,hostPhysics;public int count,samples,failures,criticalSamples,criticalFailures;public float initialLocalDamage,initialOtherDamage;public GeoRailway[] rails;public GeoRoad[] crossingRoads;public Crossing[] crossings;}
        public bool TraversalObserved=>crossings.Count==2&&crossings.All(c=>c.hostUnderSamples>0&&c.guestUnderSamples>0);
        public readonly Evidence Data=new Evidence();readonly int expected;readonly List<Crossing> crossings=new List<Crossing>();float next;bool initialized;
        public RailwayLanProofEvidence(int expectedCount){expected=expectedCount;}
        public static string RailDigest(GeoRegion region)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,new UTF8Encoding(false,true)))
            {
                w.Write(region.railwayProfilesPrepared);var rails=region.railways??Array.Empty<GeoRailway>();w.Write(rails.Length);
                foreach(var r in rails.OrderBy(r=>r.id,StringComparer.Ordinal))
                {foreach(string text in new[]{r.id,r.start,r.end,r.name,r.kind,r.operatorName,r.electrified})w.Write(text??"");w.Write(r.width);w.Write(r.gauge);w.Write(r.tracks);w.Write(r.layer);w.Write(r.bridge);w.Write(r.tunnel);w.Write(r.estimatedProfile);w.Write(r.nodeIds?.Length??0);foreach(string id in r.nodeIds??Array.Empty<string>())w.Write(id??"");w.Write(r.points?.Length??0);foreach(var p in r.points??Array.Empty<Vector3>()){w.Write(p.x);w.Write(p.y);w.Write(p.z);}}
                w.Flush();return Core.DataPackageRules.Hash(stream.ToArray());
            }
        }
        public static string RoadDigest(GeoRoad[] roads)
        {var b=new StringBuilder();foreach(var road in roads.OrderBy(r=>r.id,StringComparer.Ordinal))b.Append(JsonUtility.ToJson(road));return Core.DataPackageRules.Hash(Encoding.UTF8.GetBytes(b.ToString()));}
        static bool Intersect(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out float s,out float t)
        {var u=new Vector2(b.x-a.x,b.z-a.z);var v=new Vector2(d.x-c.x,d.z-c.z);var w=new Vector2(c.x-a.x,c.z-a.z);float den=u.x*v.y-u.y*v.x;s=t=0;if(Mathf.Abs(den)<.00001f)return false;s=(w.x*v.y-w.y*v.x)/den;t=(w.x*u.y-w.y*u.x)/den;return s>=0&&s<=1&&t>=0&&t<=1;}
        void Initialize(GameSession session)
        {
            var region=session.World.Route.Region;Data.compatibility=WorldSnapshotCodec.Compatibility;Data.mapId=WorldSnapshotCodec.NetworkId(region);Data.epoch=WorldSnapshotCodec.Epoch(region);Data.hostFingerprint=session.Network.HostFingerprint;Data.datasetVersion=region.datasetVersion;Data.datasetSource=region.datasetSource;Data.count=region.railways.Length;Data.profilesPrepared=region.railwayProfilesPrepared;Data.hostPhysics=session.Network.Hosting;Data.railDigest=RailDigest(region);Data.crossingRoads=region.roads.Where(r=>r.id!=null&&r.id.StartsWith("1109998597-",StringComparison.Ordinal)).OrderBy(r=>r.id,StringComparer.Ordinal).ToArray();Data.roadDigest=RoadDigest(Data.crossingRoads);Data.rails=region.railways.OrderBy(r=>r.id,StringComparer.Ordinal).ToArray();Data.initialLocalDamage=session.Car.Damage;Data.initialOtherDamage=session.Network.Other.Damage;
            foreach(string prefix in new[]{"railway-256150001","railway-1108948870"})
            {
                Crossing found=null;
                foreach(var rail in region.railways.Where(r=>r.id.StartsWith(prefix,StringComparison.Ordinal)))for(int i=1;i<rail.points.Length;i++)foreach(var road in Data.crossingRoads)for(int j=1;j<road.points.Length;j++)
                {
                    if(!Intersect(rail.points[i-1],rail.points[i],road.points[j-1],road.points[j],out float s,out float t))continue;var p=Vector3.Lerp(road.points[j-1],road.points[j],t);p.y=region.RoadSurfaceHeight(p.x,p.z);found=new Crossing{railId=rail.id,roadId=road.id,point=p,roadY=p.y,railHead=Vector3.Lerp(rail.points[i-1],rail.points[i],s).y,postedHeight=road.maxVehicleHeight,requiredClearance=road.maxVehicleHeight>0?Mathf.Max(3,road.maxVehicleHeight+.25f):RailwayMetrics.MinimumRoadClearance};break;
                }
                if(found==null){Data.failures++;continue;}if(crossings.Any(c=>Vector3.Distance(c.point,found.point)<.25f))Data.failures++;crossings.Add(found);
            }
            if(Data.count!=expected||!Data.profilesPrepared||crossings.Count!=2)Data.failures++;initialized=true;
            Data.limitations="Host simulates both physical bodies; guest renders authoritative kinematic replicas. Collision agreement is host physical traversal plus guest reconstructed collider/replica agreement. No live trains, independent guest physics, surveyed clearance, Windows or global resident-count equivalence claim.";
        }
        static MeshCollider Under(GameSession session,Vector3 origin,out RaycastHit hit)
        {MeshCollider found=null;hit=default;float best=30;foreach(var c in session.World.GetComponentsInChildren<MeshCollider>())if(c.name.StartsWith("Railway closed bridge deck",StringComparison.Ordinal)&&c.Raycast(new Ray(origin,Vector3.up),out var h,30)&&h.distance<best){hit=h;best=h.distance;found=c;}return found;}
        public void Observe(GameSession session)
        {
            if(!session.Network.Joined||!session.Network.Other||session.World.Route.Region==null)return;
            if(!initialized)Initialize(session);if(Time.unscaledTime<next)return;next=Time.unscaledTime+.25f;Data.samples++;
            var r=session.World.Route.Region;if(Data.epoch!=WorldSnapshotCodec.Epoch(r)||Data.railDigest!=RailDigest(r)||Data.roadDigest!=RoadDigest(r.roads.Where(road=>road.id!=null&&road.id.StartsWith("1109998597-",StringComparison.Ordinal)).ToArray()))Data.failures++;
            if(session.Car.Damage>Data.initialLocalDamage+.001f||session.Network.Other.Damage>Data.initialOtherDamage+.001f)Data.failures++;
            foreach(var crossing in crossings)
            {
                if(!session.World.GeographicWorld.HasSurface(crossing.point))continue;
                if(!Under(session,crossing.point+Vector3.up*.1f,out var hit)){Data.failures++;continue;}
                var owner=Under(session,crossing.point+Vector3.up*.1f,out hit);crossing.underside=hit.point;crossing.normal=hit.normal;crossing.collider=owner.name;crossing.triangle=hit.triangleIndex;crossing.measuredClearance=hit.point.y-crossing.roadY;
                if(!owner.Raycast(new Ray(hit.point+Vector3.up*3,Vector3.down),out var top,5))Data.failures++;else crossing.deckDepth=top.point.y-hit.point.y;
                if(crossing.measuredClearance+.02f<crossing.requiredClearance||crossing.deckDepth<.70f||Mathf.Abs(crossing.postedHeight-4.0386f)>.002f||Mathf.Abs(hit.point.y-(crossing.railHead-RailwayMetrics.UnderDeckDrop))>.12f)Data.failures++;
            }
        }
        public void FixedSample(GameSession session)
        {
            if(!initialized||!session.Network.Joined||!session.Network.Other||Time.timeScale==0)return;
            foreach(var car in new[]{session.Car,session.Network.Other})
            {
                var p=car.Body.position;for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){Data.criticalSamples++;if(!session.World.GeographicWorld.HasSurface(p+new Vector3(x*320,0,z*320)))Data.criticalFailures++;}
                bool hostCar=session.Network.Hosting?car==session.Car:car==session.Network.Other;
                foreach(var c in crossings)if(Vector2.Distance(new Vector2(c.point.x,c.point.z),new Vector2(p.x,p.z))<5&&Under(session,p+Vector3.up*.3f,out var h))
                {float roof=car.GetComponentsInChildren<Collider>().Where(co=>!co.isTrigger).Select(co=>co.bounds.max.y).DefaultIfEmpty(p.y+1.5f).Max();float gap=h.point.y-roof;if(hostCar){c.hostUnderSamples++;c.minHostCollisionGap=Mathf.Min(c.minHostCollisionGap,gap);}else{c.guestUnderSamples++;c.minGuestCollisionGap=Mathf.Min(c.minGuestCollisionGap,gap);}}
            }
        }
        public bool Finish(GameSession session,string path)
        {
            Observe(session);Data.crossings=crossings.ToArray();bool pass=initialized&&Data.samples>10&&Data.failures==0&&Data.criticalSamples>0&&Data.criticalFailures==0&&crossings.Count==2&&crossings.All(c=>c.hostUnderSamples>0&&c.guestUnderSamples>0&&c.minHostCollisionGap>0&&c.minGuestCollisionGap>0);
            File.WriteAllText(path,JsonUtility.ToJson(Data,true));return pass;
        }
    }
}
