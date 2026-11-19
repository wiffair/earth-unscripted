using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Native=EarthUnscripted.NewbridgePrototype.NewbridgeWorld;
namespace EarthUnscripted.Editor
{
    public static class GeometryPriorityVerification
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static void Check(bool valid,string why){if(!valid)throw new Exception("GEOMETRY PRIORITY FAIL "+why);checks++;}
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
        sealed class Fixture:IDisposable
        {
            public readonly GameObject Root;public readonly Native World;public readonly Dictionary<Vector2Int,Native.Map> Areas;
            public readonly Vector3 Branch=new Vector3(2600,0,256);
            public Fixture()
            {
                Root=new GameObject("Actual geometry queue priority fixture");
                try
                {
                    World=Root.AddComponent<Native>();World.enabled=false;Set(World,"embedded",true);Set(World,"embeddedSpeed",31f);
                    Areas=(Dictionary<Vector2Int,Native.Map>)typeof(Native).GetField("areas",Hidden).GetValue(World);
                    for(int z=-2;z<=2;z++)for(int x=-2;x<=26;x++)Areas.Add(new Vector2Int(x,z),new Native.Map{tileX=x,tileZ=z,coreMinX=x*1536,coreMinZ=z*1536,coreMaxX=(x+1)*1536,coreMaxZ=(z+1)*1536});
                    Set(World,"map",new Native.Map{routes=new[]{new Native.DriveRoute{points=new[]{new Vector3(-512,0,32),new Vector3(40000,0,32)},stations=new[]{0f,40512f}}}});
                    var type=typeof(Native).GetNestedType("Corridor",BindingFlags.NonPublic);var branch=Activator.CreateInstance(type);type.GetField("point").SetValue(branch,Branch);type.GetField("distanceMetres").SetValue(branch,400f);var branches=Array.CreateInstance(type,1);branches.SetValue(branch,0);Set(World,"branchWindows",branches);
                }
                catch{UnityEngine.Object.DestroyImmediate(Root);throw;}
            }
            public Dictionary<Vector2Int,float> Plan(Vector3 position,Vector3[] corridor)
            {World.SetDrivingCorridor(corridor.ToList(),31);return (Dictionary<Vector2Int,float>)typeof(Native).GetMethod("GeometryPriorities",Hidden).Invoke(World,new object[]{position,Vector3.right});}
            public bool Resident(Vector2Int key)=>Areas.ContainsKey(new Vector2Int(Mathf.FloorToInt((key.x*64+32)/1536f),Mathf.FloorToInt((key.y*64+32)/1536f)));
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Root);
        }
        // Independent pre-change window planner: retains all old broad, branch
        // and planned keys/priorities for comparison with the production helper.
        static Dictionary<Vector2Int,float> Original(Fixture fixture,Vector3 position,Vector3[] corridor,Vector3[] planned=null,bool fly=false)
        {
            var result=new Dictionary<Vector2Int,float>();
            void Add(Vector3 point,float radius,float along,bool driving=false)
            {
                int cells=Mathf.CeilToInt(radius/64),cx=Mathf.FloorToInt(point.x/64),cz=Mathf.FloorToInt(point.z/64);
                for(int z=cz-cells;z<=cz+cells;z++)for(int x=cx-cells;x<=cx+cells;x++)
                {
                    var key=new Vector2Int(x,z);if(!fixture.Resident(key))continue;
                    float distance=Vector2.Distance(new Vector2(x*64+32,z*64+32),new Vector2(point.x,point.z));if(distance>radius+32)continue;
                    float score=driving?-1000+along*.4f+distance*1.5f:along*.55f+distance*1.5f;
                    if(!result.TryGetValue(key,out float old)||score<old)result[key]=score;
                }
            }
            Add(position,224,0);if(!fly)
            {
                for(int i=0;i<corridor.Length;i+=2)Add(corridor[i],48,i*8,true);
                Add(fixture.Branch,96,400);
                if(planned!=null)
                {
                    var route=new EarthUnscripted.NewbridgePrototype.RoutePreparation(planned);float at=route.Progress(position);
                    for(float ahead=0;ahead<=1600&&at+ahead<=route.Length;ahead+=32)Add(route.At(at+ahead),48,(-150+ahead*.02f)/.55f);
                }
            }
            return result;
        }
        static void Compare(Fixture fixture,Vector3[] points,string label,Vector3[] planned=null)
        {
            var actual=fixture.Plan(points[0],points);var original=Original(fixture,points[0],points,planned);
            Check(original.Keys.All(actual.ContainsKey),label+" retains every original broad/branch/planned queue key");
            Check(original.All(row=>actual[row.Key]<=row.Value),label+" never demotes original queue work");
            var critical=actual.Where(row=>row.Value< -1000).ToArray();var ordinary=actual.Where(row=>row.Value>=-1000).ToArray();
            Check(critical.Length>0&&ordinary.Length>0&&critical.Max(row=>row.Value)<ordinary.Min(row=>row.Value),label+" every driving footprint cell precedes broad-only queue entries");
            Check(ordinary.All(row=>original.TryGetValue(row.Key,out float old)&&old==row.Value),label+" broad-only priorities unchanged");
            // Independent dense circle probes check actual wheel/body vicinity,
            // including odd-index samples skipped by the broad16m sampling.
            foreach(var point in points.Take(256))for(int angle=0;angle<360;angle+=15)
            {
                float radians=angle*Mathf.Deg2Rad;var key=new Vector2Int(Mathf.FloorToInt((point.x+Mathf.Cos(radians)*3.99f)/64),Mathf.FloorToInt((point.z+Mathf.Sin(radians)*3.99f)/64));
                if(fixture.Resident(key))Check(actual.TryGetValue(key,out float score)&&score< -1000,label+" physical footprint probe is in primary tier");
            }
            Check(actual.Keys.All(fixture.Resident),label+" never queues chunks without resident source owner");
            var branchKey=new Vector2Int(Mathf.FloorToInt(fixture.Branch.x/64),Mathf.FloorToInt(fixture.Branch.z/64));Check(actual.ContainsKey(branchKey),label+" branch work remains queued");
        }
        public static void Run()
        {
            checks=0;using(var fixture=new Fixture())
            {
                var straight=Enumerable.Range(0,80).Select(i=>new Vector3(32+i*8,0,32)).ToArray();Compare(fixture,straight,"straight");
                var negative=Enumerable.Range(0,65).Select(i=>new Vector3(-512+i*8,0,-64)).ToArray();Compare(fixture,negative,"negative boundaries");
                var curve=Enumerable.Range(0,65).Select(i=>new Vector3(32+320*Mathf.Sin(i*.025f),0,32+320*(1-Mathf.Cos(i*.025f)))).ToArray();Compare(fixture,curve,"curve");
                var corner=fixture.Plan(new Vector3(64,0,64),new[]{new Vector3(64,0,64),new Vector3(72,0,64)});
                foreach(var key in new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(0,1),new Vector2Int(1,1)})Check(corner.TryGetValue(key,out float score)&&score< -1000,"four-cell chunk corner footprint is covered");
                fixture.Areas.Remove(new Vector2Int(-1,-1));Compare(fixture,negative,"missing neighbouring owner");
                var planned=new[]{straight[0],new Vector3(32,0,1500),new Vector3(600,0,1500)};fixture.World.SetPlannedRoute(planned);Compare(fixture,straight,"active route",planned);
                fixture.World.SetPlannedRoute(null);
                var many=Enumerable.Range(0,300).Select(i=>new Vector3(32+i*128,0,32)).ToArray();var bounded=fixture.Plan(many[0],many);
                Check(bounded.Count(row=>row.Value< -1000)<=256*4,"primary work is bounded to256points and at most4intersected cells per point");
                Check(bounded[new Vector2Int(510,0)]< -1000&&bounded[new Vector2Int(512,0)]>=-1000,"256th primary point included;257th retains only its old broad priority");
                Set(fixture.World,"fly",true);var fly=fixture.Plan(straight[0],straight);var flyOriginal=Original(fixture,straight[0],straight,null,true);
                Check(fly.Count==flyOriginal.Count&&fly.All(row=>flyOriginal[row.Key]==row.Value),"fly-mode planning remains unchanged without driving tiers");
            }
            Debug.Log("GEOMETRY PRIORITY PASS "+checks+"; actual EnsureAround planner, old windows preserved, no frame-budget/publication changes");
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
