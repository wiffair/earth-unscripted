using System.Collections;
using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        double nextSeaRecovery;
        float floodedSeconds;
        bool hasAutomaticSafeRoad;
        Vector3 automaticSafeRoad;
        Quaternion automaticSafeHeading;

        void RememberAutomaticSafeRoad()
        {
            if(World.AutomaticWorld && !OnRoad && !PreparingStart && World.AutomaticWorld.Renderer.TryWaterSurface(Car.Body.position,out float water,out _))
            {
                float groundPlane=Car.Body.position.y-Car.Metrics.RestRootHeight(Car.Body.mass,Car.SpringFactor);
                Car.WaterDepth=Mathf.Max(0,water-groundPlane);
                floodedSeconds=Car.WaterDepth>Car.WadingDepth?floodedSeconds+Time.deltaTime:Mathf.Max(0,floodedSeconds-Time.deltaTime*2);
                if(hasAutomaticSafeRoad&&floodedSeconds>3&&Time.realtimeSinceStartupAsDouble>nextSeaRecovery)
                {nextSeaRecovery=Time.realtimeSinceStartupAsDouble+10;floodedSeconds=0;BeginAutomaticRecovery(false);Notify("Water too deep · recovered to a safe road");return;}
            }
            else{Car.WaterDepth=0;floodedSeconds=0;}
            if(!World.AutomaticWorld || !Car.Grounded || !OnRoad || CurrentRoad==null)return;
            var region=World.Route.Region;
            var projection=region.ProjectRoadForVehicle(Car.Body.position,VehicleRoadSupportHeight());
            if(!projection.OnRoad)return;
            automaticSafeRoad=region.DrivingLanePoint(projection.Road,projection.Source.Point,Car.transform.forward,out var heading);
            automaticSafeHeading=Quaternion.LookRotation(heading,Vector3.up);
            hasAutomaticSafeRoad=true;
        }

        void BeginAutomaticRecovery(bool tow)
        {
            if(PreparingStart || Network.Active&&!Network.Hosting)return;
            var point=tow?World.Garage.position:hasAutomaticSafeRoad?automaticSafeRoad:World.Route.At(35,World.Route.LaneOffset(35));
            var rotation=tow?World.Garage.rotation:hasAutomaticSafeRoad?automaticSafeHeading:World.Route.Rotation(35);
            StartCoroutine(RelocateAutomaticCar(point,rotation,tow,++startPreparationGeneration));
        }

        IEnumerator RelocateAutomaticCar(Vector3 point,Quaternion rotation,bool tow,int request)
        {
            var host=World.AutomaticWorld;var car=Car;var body=car.Body;
            bool wasMenu=Menu,wasKinematic=body.isKinematic,success=false;
            var velocity=body.linearVelocity;var angular=body.angularVelocity;
            SetMenu(true);PreparingStart=true;PreparingRoadFrontier=true;
            PreparingStartStatus=tow?"Preparing the service area…":"Preparing the last supported road…";
            host.BeginRelocation(point);body.isKinematic=true;
            double began=Time.realtimeSinceStartupAsDouble;
            try
            {
                while(host && !host.RelocationReady)
                {
                    if(request!=startPreparationGeneration || !BootReady || Network.Active&&!Network.Hosting)yield break;
                    if(Time.realtimeSinceStartupAsDouble-began>300)
                    {Notify("That road is still loading · recovery cancelled");yield break;}
                    PreparingStartStatus=host.Status;yield return null;
                }
                if(!host || request!=startPreparationGeneration)yield break;
                body.isKinematic=false;
                car.Recover(point,rotation);Racing?.Recovery();Feedback.StopAll();success=true;
                Notify(tow?"Towed to the roadside service area":"Recovered to the last supported road · damage retained");
            }
            finally
            {
                if(host)host.EndRelocation();
                if(body)
                {
                    body.isKinematic=wasKinematic;
                    if(!wasKinematic && !success){body.linearVelocity=velocity;body.angularVelocity=angular;}
                }
                if(request==startPreparationGeneration)
                {PreparingStart=false;PreparingRoadFrontier=false;PreparingStartStatus="";if(success&&!wasMenu)SetMenu(false);}
            }
        }
    }
}
