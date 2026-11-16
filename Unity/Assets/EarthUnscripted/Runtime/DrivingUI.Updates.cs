using UnityEngine;

namespace EarthUnscripted
{
    public sealed partial class DrivingUI
    {
        void DrawAppUpdates()
        {
            MenuGroup("GAME & PACK UPDATES","Check at launch and while you drive. Game updates keep Standard and HD together; saves stay in your profile.");
            var updates=Session.AppUpdates;if(!updates){Text(65,410,1200,50,"Updates are available in the single-player beta.",body);GroupBack(21);return;}
            bool auto=StateToggle(new Rect(65,390,1260,45),updates.Settings.automaticUpdates,"Automatically download updates · install the game at close",toggle);
            if(auto!=updates.Settings.automaticUpdates)updates.SetAutomatic(auto);
            Text(65,450,1220,50,"Installed: "+ProductIdentity.Version+"   ·   "+updates.Status,body);
            Text(65,510,1220,60,updates.DownloadStatus,body);
            Text(65,580,1220,65,"Radio downloads reuse unchanged content. New stations and playlists become available without stopping the playing song. Car updates take effect after restart.",small);
            GUI.enabled=!updates.Busy;
            if(Button(65,665,260,"CHECK NOW",42))updates.CheckNow();
            if(updates.ReadyToInstall)
            {
                if(Button(350,665,390,"SAVE, CLOSE & INSTALL",42))updates.InstallNow();
                if(Button(765,665,550,"INSTALL WHEN I CLOSE",42))updates.QueueForClose();
            }
            else if(updates.HasGameUpdate||updates.PackUpdates.Count>0)
            {
                if(Button(350,665,390,"UPDATE NOW",42))updates.DownloadUpdates(true);
                if(Button(765,665,550,"DOWNLOAD · INSTALL AT CLOSE",42))updates.DownloadUpdates(false);
            }
            GUI.enabled=true;
            if(updates.ExitWaiting)
            {if(Button(65,735,400,"QUIT WITHOUT INSTALLING",40))updates.QuitWithoutUpdate();}
            else Text(65,735,1230,45,"You can keep driving while a download runs. A failed check or download leaves your current installation usable.",small);
            GroupBack(21);
        }
    }
}
