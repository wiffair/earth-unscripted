using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class GroundingVerification
    {
        public static void Run()
        {
            int checks=0;void Require(bool condition,string message){if(!condition)throw new Exception("GROUND FAIL: "+message);checks++;Debug.Log("GROUND PASS: "+message);}
            try
            {
                ProjectSetup.Configure();
                var fixture=new GeoRegion{minX=-100,minZ=-100,width=200,depth=200,nx=2,nz=2,heights=new float[4],landNx=2,landNz=2,landCover=new int[4],waters=Array.Empty<GeoWater>(),roads=new[]{
                    new GeoRoad{id="lower",start="a",end="b",width=9,points=new[]{new Vector3(0,0,-100),new Vector3(0,0,100)}},
                    new GeoRoad{id="upper",start="c",end="d",width=9,points=new[]{new Vector3(30,12,-100),new Vector3(30,12,100)}}}};
                fixture.Prepare();Require(Mathf.Abs(fixture.TerrainHeight(30,0)-11.45f)<.01f,"a nearby low road does not excavate underneath an uphill road");
                var r=GeoRegion.Load();var world=new GameObject("Grounding actual mesh verification").AddComponent<WorldBuilder>();world.Build("dee-valley");r=world.Route.Region;
                var start=world.Route.At(35,world.Route.LaneOffset(35));world.GeographicWorld.Refresh(start,true);Physics.SyncTransforms();
                int samples=0,missing=0,covered=0,raised=0;float worstCover=-100,worstGap=0;Vector3 worst=Vector3.zero;
                foreach(var road in r.roads)
                {
                    if(road.bridge||road.tunnel)continue;
                    for(int i=0;i<road.points.Length-1;i++)
                    {
                        var a=road.points[i];var b=road.points[i+1];var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;
                        for(int j=0;j<3;j++)foreach(float lane in new[]{-.38f,0,.38f})
                        {
                            var p=Vector3.Lerp(a,b,(j+.5f)/3)+side*lane*road.width;if(!world.GeographicWorld.HasSurface(p))continue;
                            float expected=r.RoadSurfaceHeight(p.x,p.z);float asphalt=float.MinValue,terrain=float.MinValue;
                            foreach(var hit in Physics.RaycastAll(new Vector3(p.x,expected+80,p.z),Vector3.down,180))
                            {
                                string name=hit.collider.name;
                                if((name=="Mapped road"||name=="Junction apron")&&Mathf.Abs(hit.point.y-expected)<4)asphalt=Mathf.Max(asphalt,hit.point.y);
                                if(name=="Terrain")terrain=Mathf.Max(terrain,hit.point.y);
                            }
                            samples++;if(asphalt==float.MinValue){missing++;if(missing<10)Debug.Log("GROUND MISSING asphalt "+road.id+" "+p);continue;}if(terrain==float.MinValue){missing++;if(missing<10)Debug.Log("GROUND MISSING terrain "+road.id+" "+p);continue;}
                            float cover=terrain-asphalt,gap=asphalt-terrain;if(cover>worstCover){worstCover=cover;worst=p;}
                            worstGap=Mathf.Max(worstGap,gap);if(cover>.04f)covered++;if(gap>1.2f)raised++;
                        }
                    }
                }
                int piers=0;
                foreach(var box in world.GetComponentsInChildren<BoxCollider>())if(box.name=="Bridge pier")
                {
                    float distance=float.MaxValue,deck=0;var centre=box.bounds.center;
                    foreach(var bridge in r.roads)if(bridge.bridge)for(int i=0;i<bridge.points.Length-1;i++)
                    {
                        var a=bridge.points[i];var v=bridge.points[i+1]-a;float f=Mathf.Clamp01(((centre.x-a.x)*v.x+(centre.z-a.z)*v.z)/Mathf.Max(.001f,v.x*v.x+v.z*v.z));var point=a+v*f;float d=Vector2.Distance(new Vector2(centre.x,centre.z),new Vector2(point.x,point.z));if(d<distance){distance=d;deck=point.y;}
                    }
                    Require(box.bounds.max.y<deck-.75f,"bridge pier top stays beneath the deck rather than breaking through asphalt");piers++;
                }
                Require(piers>0,"loaded bridge supports verified");
                int riverSamples=0,buriedRiver=0;float worstRiver=-100;
                foreach(var water in r.waters)for(int i=0;i<water.points.Length-1;i++)
                {
                    var p=Vector3.Lerp(water.points[i],water.points[i+1],.5f);var nearby=r.Nearest(p,out _,out float separation,false);
                    if(nearby!=null&&separation<nearby.width*.5f+5)continue;
                    float over=world.GeographicWorld.MeshSurfaceHeight(p.x,p.z)-(p.y-3);riverSamples++;worstRiver=Mathf.Max(worstRiver,over);if(over>-.02f)buriedRiver++;
                }
                Debug.Log($"RIVER METRICS samples={riverSamples} buried={buriedRiver} worstGroundAboveWater={worstRiver:F3}");
                Require(riverSamples>100&&buriedRiver==0,"rendered channel terrain stays below river water outside at-grade road supports");
                Debug.Log($"GROUND METRICS samples={samples} missing={missing} covered={covered} raised={raised} worstCover={worstCover:F3} worstGap={worstGap:F3} worst={worst}");
                Require(samples>2000,"actual loaded terrain and road colliders sampled across both driving lanes");
                Require(missing==0,"all sampled lanes have both asphalt and supporting terrain collision");
                Require(covered==0,"terrain triangles never cover sampled asphalt");
                Require(raised==0,"at-grade asphalt stays within1.2m of its terrain support");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/grounding.txt",$"PASS {checks} checks\nsamples={samples}\nworstCover={worstCover}\nworstGap={worstGap}\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
