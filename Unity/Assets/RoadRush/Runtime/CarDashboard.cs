using UnityEngine;

namespace RoadRush
{
    // Original instruments on the concept model's existing display surface.
    // Readouts use the same speed and drive/reverse state as the main HUD.
    public sealed class CarDashboard
    {
        readonly Transform appearanceOwner;
        readonly TextMesh speed,gear;
        int previousSpeed=-1;string previousGear="";
        public string SpeedText=>speed?speed.text:"";
        public CarDashboard(Transform owner)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearanceContent(owner);

            appearanceOwner = owner;
            var root=new GameObject("Working digital instruments").transform;root.SetParent(owner,false);
            // Measured front display plane in the normalized car coordinate frame.
            var normal=new Vector3(0,.3304f,-.9439f).normalized;
            root.localPosition=new Vector3(.006f*Core.VehicleDimensions.ForModel(0).LegacyBodyXScale,.740f,1.137f)+normal*.004f;
            root.localRotation=Quaternion.LookRotation(-normal,Vector3.ProjectOnPlane(Vector3.up,normal));
            var cyan=new Color(.55f,.95f,1);
            speed=Geometry.FittedLabel("000",root,new Vector3(0,.004f,0),Quaternion.identity,.13f,.045f,cyan);speed.name="Dashboard speed";
            gear=Geometry.FittedLabel("P",root,new Vector3(-.145f,.004f,0),Quaternion.identity,.027f,.037f,Color.white);gear.name="Dashboard drive mode";
            Geometry.FittedLabel("MPH",root,new Vector3(.141f,.003f,0),Quaternion.identity,.052f,.017f,cyan).name="Dashboard speed unit";
        }
        public void Set(float metresPerSecond,bool reversing)
        {
            using var appearanceWrite = VehiclePopulation.VehiclePopulationHooks.BeforeAppearancePose(appearanceOwner);

            int mph=Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(metresPerSecond)*Core.DrivingMath.MphPerMetreSecond),0,999);
            string mode=reversing?"R":"D";
            if(mph!=previousSpeed){previousSpeed=mph;speed.text=mph.ToString("000");}
            if(mode!=previousGear){previousGear=mode;gear.text=mode;}
        }
    }
}
