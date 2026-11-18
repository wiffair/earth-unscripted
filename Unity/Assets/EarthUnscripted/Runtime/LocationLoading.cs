using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace EarthUnscripted
{
    public sealed class LocationLoading : MonoBehaviour
    {
        static LocationLoading active;string destination;AsyncOperation operation;string loadFailure="";
        public static bool Busy=>active;
        public static void Open(string location)
        {
            if(active)return;active=new GameObject("Location loading").AddComponent<LocationLoading>();active.destination=string.IsNullOrEmpty(location)?"Blackwood County":location;DontDestroyOnLoad(active.gameObject);active.StartCoroutine(active.Load());
        }
        IEnumerator Load()
        {
            Time.timeScale=1;yield return null;yield return null;
            BootTelemetry.SceneRequest(destination);if(!BeginSceneLoad())yield break;
            double deadline=Time.realtimeSinceStartupAsDouble+300;
            while(!operation.isDone){if(Time.realtimeSinceStartupAsDouble>deadline){loadFailure="Scene loading timed out";Time.timeScale=0;yield break;}yield return null;}
            BootTelemetry.Scene("scene.operation.done",destination);
            GameSession session=null;double start=Time.realtimeSinceStartupAsDouble;
            while(!(session=FindAnyObjectByType<GameSession>())){if(Time.realtimeSinceStartupAsDouble-start>30){loadFailure="Game session did not start";yield break;}yield return null;}
            while(session&&!session.BootReady&&!session.BootFailed)yield return null;
            if(!session||!session.BootReady){loadFailure=session?session.BootStatus:"Game session ended before it was ready";yield break;}
            active=null;Destroy(gameObject);
        }
        bool BeginSceneLoad()
        {try{operation=SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);if(operation==null)throw new System.InvalidOperationException("Scene loading could not start");return true;}catch(System.Exception e){loadFailure=e.Message;Time.timeScale=0;return false;}}
        public static void Retry(){if(active){var previous=active;active=null;Destroy(previous.gameObject);}Open("Saved location");}
        readonly ControllerMenuNavigation controller = new ControllerMenuNavigation();
        void Update()
        {
            var session = FindAnyObjectByType<GameSession>();
            if(session && session.ChoosingFailedLocation)return;
            controller.Update(session && !session.BootReady, () => { if (session && !session.BootFailed) session.CancelBoot(); });
        }
        void OnGUI()
        {
            var choosing=FindAnyObjectByType<GameSession>();if(choosing&&choosing.ChoosingFailedLocation)return;
            GUI.depth=-1000;
            GUI.color=Color.white;
            var session=FindAnyObjectByType<GameSession>();
            bool failed=!string.IsNullOrEmpty(loadFailure)||(session&&session.BootFailed);
            string status=!string.IsNullOrEmpty(loadFailure)?loadFailure:session?session.BootStatus:"Opening your saved world…";
            BrandScreen.DrawLoading(failed?"World not started":"Preparing your journey",destination,status,failed);
            using var navigationScope = ControllerMenuGUI.Begin(controller, failed ? "location-error" : "location-loading");
            if(failed)
            {
                BetaDiagnostics.DrawFailedActions(session);
                if(session&&session.CanReturnFromFailedLocation&&ControllerMenuGUI.Button(new Rect(440,690,330,42),"Choose another place"))session.OpenFailedLocationChooser();
                if(ControllerMenuGUI.Button(new Rect(98,690,300,42),"Retry saved location"))Retry();
                if(session&&session.CanReturnFromFailedLocation&&ControllerMenuGUI.Button(new Rect(98,744,300,42),"Open county · keep career"))session.ReturnToCountyKeepingCareer();
                if(ControllerMenuGUI.Button(new Rect(98,798,300,42),"Quit"))Application.Quit();
            }
            else if(session&&!session.BootReady)
            {
                if(ControllerMenuGUI.Button(new Rect(98,690,300,42),"Cancel preparation"))session.CancelBoot();
            }

        }
    }
}
