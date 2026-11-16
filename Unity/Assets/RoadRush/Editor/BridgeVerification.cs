using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    public static class BridgeVerification
    {
        public static void Run()
        {
            int checks=0;void Check(bool value,string text){if(!value)throw new Exception("BRIDGE FAIL: "+text);checks++;Debug.Log("BRIDGE PASS: "+text);}
            try
            {
                ProjectSetup.Configure();var world=new GameObject("Bridge structure proof").AddComponent<WorldBuilder>();world.Build("dee-valley");var region=world.Route.Region;var bridge=Array.Find(region.roads,r=>r.bridge&&r.name=="Llangollen Bridge");
                Check(bridge!=null,"mapped Llangollen road bridge available");var centre=region.RoadPoint(bridge,bridge.length*.5f,out var forward);world.GeographicWorld.Refresh(centre,true);Physics.SyncTransforms();var side=Vector3.Cross(Vector3.up,forward).normalized;
                bool Surface(string name,Ray ray,float distance,out RaycastHit result)
                {
                    result=default;float nearest=float.MaxValue;bool found=false;
                    foreach(var collider in world.GetComponentsInChildren<MeshCollider>())if(collider.name==name&&collider.Raycast(ray,out var hit,distance)&&hit.distance<nearest){nearest=hit.distance;result=hit;found=true;}
                    return found;
                }
                Check(Surface("Bridge deck",new Ray(centre+Vector3.up*3,Vector3.down),4,out var top),"asphalt top has collision");
                Check(Surface("Bridge concrete structure",new Ray(centre-Vector3.up*2,Vector3.up),3,out var underside)&&Mathf.Abs(top.point.y-underside.point.y-.75f)<.035f&&underside.normal.y<-.8f,"closed underside is0.75m below asphalt with an outward downward face");
                foreach(int sign in new[]{-1,1})
                {
                    var outer=centre+side*sign*(bridge.width*.5f+2)-Vector3.up*.35f;
                    Check(Surface("Bridge concrete structure",new Ray(outer,-side*sign),3,out var wall)&&Vector3.Dot(wall.normal,side*sign)>.8f,"deck side face is closed and faces outward");
                    Check(Surface("Bridge safety barriers",new Ray(centre+Vector3.up*.63f,side*sign),bridge.width,out var rail)&&rail.distance>=bridge.width*.5f-.2f&&rail.distance<bridge.width*.5f+.5f,"physical safety rail exists beyond the clear driving lane");
                    var beam=centre+side*sign*bridge.width*.28f-Vector3.up*2;
                    Check(Surface("Bridge concrete structure",new Ray(beam,Vector3.up),2,out var beamHit)&&beamHit.point.y<centre.y-1.25f,"longitudinal structural beam projects below the deck slab");
                }
                var start=bridge.points[0];var startForward=(bridge.points[1]-start).normalized;
                Check(Surface("Bridge concrete structure",new Ray(start-startForward*2-Vector3.up*.35f,startForward),3,out var end)&&Vector3.Dot(end.normal,-startForward)>.8f,"start end cap closes the slab volume");
                int supports=0;foreach(var pier in world.GetComponentsInChildren<BoxCollider>())if(pier.name=="Bridge pier")
                {Check(pier.bounds.min.y<=world.GeographicWorld.MeshSurfaceHeight(pier.bounds.center.x,pier.bounds.center.z)-.4f,"support footing embeds in the actual rendered terrain");supports++;}Check(supports>0,"bridge support footings exist");
                Directory.CreateDirectory("Builds/Verification");File.WriteAllText("Builds/Verification/bridge-structure.txt","PASS "+checks+" actual underside/depth/face/barrier/beam checks\n");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
