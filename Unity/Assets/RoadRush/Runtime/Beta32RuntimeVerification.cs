using System;
using System.Linq;
using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace RoadRush
{
    public static class Beta32RuntimeVerification
    {
        public static void Run(GameSession session)
        {
            var world=session.World.AutomaticWorld.Renderer;
            var roads=world.ResidentAreas.SelectMany(m=>m.features).Where(f=>f.category=="road").ToArray();
            int known=roads.Count(f=>f.signContexts!=null&&f.signContexts.Any(s=>s.region=="GB-WLS"));
            int lit=roads.Count(f=>f.signLighting=="yes");if(known==0||lit==0)throw new Exception("Actual cached worker sidecar context missing");
            var ui=UnityEngine.Object.FindFirstObjectByType<DrivingUI>();var p=session.Car.Body.position;
            var selected=new NavigationRoute(new[]{p,p+Vector3.forward*300,p+Vector3.forward*300+Vector3.left*2000});
            var property=typeof(DrivingNavigation).GetProperty("Current");var previous=ui.Navigation.Current;
            var update=typeof(DrivingUI).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
            var field=world.GetType().GetField("plannedRouteSource",BindingFlags.NonPublic|BindingFlags.Instance);
            try
            {
                property.SetValue(ui.Navigation,selected);update.Invoke(ui,null);
                if(!ReferenceEquals(field.GetValue(world),selected.points))throw new Exception("Selected navigation route did not reach renderer");
                ui.Navigation.CancelRoute();update.Invoke(ui,null);
                if(field.GetValue(world)!=null)throw new Exception("Cancelled route kept preparation priority");
            }
            finally{property.SetValue(ui.Navigation,previous);world.SetPlannedRoute(previous?.points);}
            File.WriteAllText(Path.Combine(SinglePlayerBeta.VerificationOutput,"beta32-runtime.json"),new JObject{["passed"]=true,["welshRoadContexts"]=known,["litRoadContexts"]=lit,["actualNavigationBindingAndCancel"]=true,["originalGeometryCacheReused"]=world.FinishedCacheHits}.ToString());
        }
    }
}
