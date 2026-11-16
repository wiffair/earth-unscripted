using System;
using System.Collections.Generic;
using UnityEngine;
using RoadRush.NewbridgePrototype;
namespace RoadRush
{
    public sealed partial class TrafficCar
    {
        float controlAdvance=float.PositiveInfinity,stopWait;
        string waitingControl;
        readonly HashSet<string> completedStops=new HashSet<string>();
        
        void ResetControlStops(){completedStops.Clear();waitingControl=null;stopWait=0;}
        float ControlSpeed(float desired,float dt)
        {
            controlAdvance=float.PositiveInfinity;
            if(networkRoad.NativeControls==null)return desired;
            var region=Route.Region;
            foreach(var c in networkRoad.NativeControls)
            {
                if(c.kind!="traffic_signals"&&c.kind!="stop"&&c.kind!="give_way")continue;
                float station=region.RoadProgress(networkRoad,c.Position);
                Vector3 projected=region.RoadPoint(networkRoad,station,out var tangent);
                if(Vector3.Distance(projected,c.Position)>1.5f||Vector3.Dot(tangent*Direction,c.Forward)<.4f)continue;
                float ahead=(Direction>0?station:networkRoad.length-station)-networkDistance;
                string identity=networkRoad.id+":"+Direction+":"+c.id;
                if(ahead< -15){completedStops.Remove(identity);continue;}
                float distance=ahead-3.5f; // front bumper waits behind the mapped stop position
                if(distance< -1||distance>100)continue;
                bool hold=false;
                if(c.kind=="traffic_signals")hold=TrafficControlPhase.State(c,Time.time,region.countryCode)!=2;
                else if(c.kind=="stop"&&!completedStops.Contains(identity))
                {
                    hold=true;
                    if(waitingControl!=identity){waitingControl=identity;stopWait=0;}
                    if(distance<.35f&&Speed<.2f){stopWait+=dt;if(stopWait>=1.2f){completedStops.Add(identity);hold=false;}}
                }
                
                if(c.kind=="give_way" || c.kind=="stop"&&completedStops.Contains(identity))
                {
                    // Mapped minor-road control takes precedence over inferred
                    // road-class priority, including controls inside source ways.
                    bool Conflict(Vector3 p,Vector3 velocity)
                    {
                        var delta=c.Position-p;float gap=new Vector2(delta.x,delta.z).magnitude;
                        if(gap>40||Mathf.Abs(p.y-c.y)>2.5f)return false;
                        if(Mathf.Abs(Vector3.Dot(velocity.normalized,c.Forward))>.85f)return false;
                        return gap<7 || Vector3.Dot(velocity,delta)>0&&gap/Mathf.Max(1,velocity.magnitude)<4;
                    }
                    hold=Conflict(Player.Body.position,Player.Body.linearVelocity);
                    if(!hold&&Fleet!=null)foreach(var other in Fleet)if(other&&other!=this&&other.gameObject.activeInHierarchy&&Conflict(other.PhysicsPosition,other.transform.forward*other.Speed)){hold=true;break;}
                }
                if(hold){controlAdvance=Mathf.Min(controlAdvance,Mathf.Max(0,distance));desired=Mathf.Min(desired,Mathf.Sqrt(Mathf.Max(0,distance)*8));}
            }
            return desired;
        }
    }
}
