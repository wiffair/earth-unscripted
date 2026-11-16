using System;
using UnityEngine;
namespace RoadRush
{
    public sealed partial class GameSession
    {
        BootSceneOwnership bootSceneOwnership;
        public string BootCleanupStatus {get;private set;}="";
        GameObject OwnBootRoot(GameObject root)
        {if(bootSceneOwnership==null)bootSceneOwnership=new BootSceneOwnership(gameObject);return bootSceneOwnership.OwnRoot(root);}
        void CommitBootSceneOwnership(){bootSceneOwnership?.Commit();bootSceneOwnership=null;}
        void CleanupIncompleteBootScene()
        {
            // Persistent country service is not scene-owned. Detach only this
            // exact session; never stop an applying SQLite transaction/coroutine.
            string warnings="";
            void Attempt(Action action){try{action();}catch(Exception e){if(warnings.Length<800)warnings+=(warnings.Length==0?"":"; ")+e.GetType().Name+": "+e.Message.Substring(0,Math.Min(e.Message.Length,140));}}
            Attempt(()=>CountryUpdateService.Unbind(this));
            var owner=bootSceneOwnership;if(owner==null){BootCleanupStatus=warnings;return;}
            owner.Quiesce();
            if(Network)Attempt(Network.Stop);if(Racing)Attempt(Racing.Cancel);if(Feedback)Attempt(Feedback.StopAll);
            owner.Rollback();BootCleanupStatus=warnings+(warnings.Length>0&&owner.CleanupWarnings.Length>0?"; ":"")+owner.CleanupWarnings;bootSceneOwnership=null;
            if(rainMaterial){if(Application.isPlaying)Destroy(rainMaterial);else DestroyImmediate(rainMaterial);rainMaterial=null;}
            World=null;Car=null;Input=null;View=null;Feedback=null;audioDriver=null;Network=null;Racing=null;Pits=null;Downloads=null;Conditions=null;Updates=null;rain=null;gate=null;police=null;patrols.Clear();
            // Career/economy are deliberately untouched; no save occurs here.
        }
    }
}
