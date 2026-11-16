using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using Clipper2Lib;
using UnityEditor;
using UnityEngine;
using EarthUnscripted.NewbridgePrototype;
namespace EarthUnscripted.Editor
{
    public static class TunnelClearanceVerification
    {
        public static void Run()
        {
            var fixture=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/EarthUnscripted/Editor/TunnelClearanceFixture.json");
            var map=JsonUtility.FromJson<NewbridgeWorld.Map>(fixture.text);
            var root=new GameObject("Cached tunnel clearance proof");
            try
            {
                var world=root.AddComponent<NewbridgeWorld>();
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(NewbridgeWorld).GetMethod("IndexMap",flags).Invoke(world,new object[]{map});
                typeof(NewbridgeWorld).GetField("workingMap",flags).SetValue(world,map);
                var house=map.buildings.Single();var points=house.points.ToList();
                var method=typeof(NewbridgeWorld).GetMethod("BuildingRoadSpace",flags);
                var spatial=((IDictionary)typeof(NewbridgeWorld).GetField("spatialByMap",flags).GetValue(world))[map];
                var stats=Activator.CreateInstance(typeof(NewbridgeWorld).GetNestedType("BuildingWorkStats",BindingFlags.NonPublic),true);
                var before=new PathsD{new PathD(points.Select(p=>new PointD(p.x,p.z)))};
                var after=(PathsD)method.Invoke(world,new object[]{house,points,11.7f,20f,spatial,stats,true});
                if(after==null||Math.Abs(Clipper.Area(after))>=Math.Abs(Clipper.Area(before))-.001)
                    throw new IOException("Cached tunnel facade was not cleared from its actual road corridor");
                var overhead=(PathsD)method.Invoke(world,new object[]{house,points,25f,35f,spatial,stats,true});
                if(overhead!=null)throw new IOException("Safely elevated building was unnecessarily removed above tunnel");
                Debug.Log("TUNNEL CLEARANCE PASS: cached facade trimmed; safely elevated building preserved");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
