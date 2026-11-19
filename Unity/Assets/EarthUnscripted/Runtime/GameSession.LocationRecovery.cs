using UnityEngine;
namespace EarthUnscripted
{
    public sealed partial class GameSession
    {
        WorldMapMenu failedLocationMenu;
        public bool ChoosingFailedLocation {get;private set;}
        public void OpenFailedLocationChooser()
        {
            if(!CanReturnFromFailedLocation)return;
            if(!Downloads)Downloads=gameObject.AddComponent<GlobalRegions>();
            failedLocationMenu ??= new WorldMapMenu();failedLocationMenu.Open(this);
            ChoosingFailedLocation=true;
        }
        public void CloseFailedLocationChooser(){ChoosingFailedLocation=false;}
        void DrawFailedLocationChooser()
        {
            GUI.depth=-1100;InterfaceTheme.Apply();
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1440*scale)*.5f,(Screen.height-900*scale)*.5f),Quaternion.identity,Vector3.one*scale);
            BrandScreen.DrawLoading("Choose another place","Your career is retained","Search for a place and prepare a starting road.",false);
            using var scope=ControllerMenuGUI.Begin(bootMenus,"failed-location-chooser");
            failedLocationMenu.Draw(this,CloseFailedLocationChooser);
        }
        public bool StartRecoveredLocation(string id,Vector3 position)
        {
            if(!CanReturnFromFailedLocation||!ChoosingFailedLocation||!RegionCache.Safe(id)||!RegionCache.Exists(id)
                ||float.IsNaN(position.x)||float.IsInfinity(position.x)||float.IsNaN(position.z)||float.IsInfinity(position.z))return false;
            string previous=Profile.region,previousStart=Profile.startRegion;bool custom=Profile.customStart;float x=Profile.startX,z=Profile.startZ;
            Profile.region=id;Profile.startRegion=id;Profile.customStart=true;Profile.startX=position.x;Profile.startZ=position.z;
            if(!Profile.Save())
            {Profile.region=previous;Profile.startRegion=previousStart;Profile.customStart=custom;Profile.startX=x;Profile.startZ=z;Downloads.Status="Could not save this start. Your previous location is retained.";return false;}
            ChoosingFailedLocation=false;AutoStartCircuit=false;LocationLoading.Retry();return true;
        }
    }
}
