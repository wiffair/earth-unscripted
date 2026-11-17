using System;
using System.IO;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Initializes only the separately identified beta. Profiles, career slots,
    /// region files, preferences and the world cache belong to this product.
    /// Existing live-game files are neither discovered nor imported.
    /// </summary>
    public static class SinglePlayerBeta
    {
        public const string Identifier = "com.earthunscripted.beta.singleplayer";
        public const string HDIdentifier = Identifier+".hd";
        public static bool Enabled => Application.identifier == Identifier || Application.identifier == HDIdentifier;
        public static string ProductRoot => Enabled ? ProductIdentity.BetaStorageRoot : Application.persistentDataPath;
        public static string DataRoot { get; private set; }
        public static string VerificationOutput { get; private set; }
        public static string StartupError { get; private set; }
        static FileStream sessionLease;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Initialize()
        {
            if (!Enabled) return;
            GameSession.SuppressAutomaticLaunch = true;
            try
            {
                if (Application.companyName != "Earth Unscripted" || Application.productName != ProductIdentity.BetaName)
                    throw new InvalidDataException("Beta product identity is inconsistent");
                DataRoot = Path.Combine(ProductRoot, "BetaData");
                var arguments = Environment.GetCommandLineArgs();
                int verification = Array.IndexOf(arguments, "-earthUnscriptedBetaSmoke");
                if (verification >= 0)
                {
                    if (verification + 1 >= arguments.Length || !Guid.TryParseExact(arguments[verification + 1], "N", out var token))
                        throw new ArgumentException("A fresh beta verification token is required");
                    VerificationOutput = Path.Combine(Path.GetTempPath(), "EarthUnscriptedBetaSmoke-" + token.ToString("N"));
                    if (Directory.Exists(VerificationOutput)) throw new IOException("Verification output already exists");
                    Directory.CreateDirectory(VerificationOutput);
                    DataRoot = Path.Combine(VerificationOutput, "BetaData");
                    Application.runInBackground = true;
                }
                int sharedProof=Array.IndexOf(arguments,"-earthUnscriptedSharedProfile");
                if(VerificationOutput!=null&&sharedProof>=0)
                {
                    if(sharedProof+1>=arguments.Length||!Guid.TryParseExact(arguments[sharedProof+1],"N",out var sharedToken))
                        throw new ArgumentException("A shared disposable profile token is required");
                    DataRoot=Path.Combine(Path.GetTempPath(),"EarthUnscriptedSharedProfile-"+sharedToken.ToString("N"),"BetaData");
                }
                Directory.CreateDirectory(DataRoot);
                // Standard and HD intentionally share progress. Protect direct
                // app launches too; the launcher already prevents overlap.
                sessionLease=new FileStream(Path.Combine(DataRoot,".active-session"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                Application.quitting+=()=>{sessionLease?.Dispose();sessionLease=null;};
                VehiclePackLibrary.Initialize(DataRoot, Path.Combine(Application.streamingAssetsPath, "VehiclePacks"));
                PlayerProfile.StorageOverride = Path.Combine(DataRoot, "beta-save.json");
                RegionCache.OverrideFolder = Path.Combine(DataRoot, "Regions");
                Directory.CreateDirectory(RegionCache.OverrideFolder);
                var seed = Resources.Load<TextAsset>("AutomaticWorld/BetaStart");
                if (!seed) throw new FileNotFoundException("Beta starting area is missing");
                string seedText=seed.text;
                int coastSeed=Array.IndexOf(arguments,"-earthUnscriptedBetaCoastSeed");
                if(VerificationOutput!=null&&coastSeed>=0&&coastSeed+1<arguments.Length)seedText=File.ReadAllText(arguments[coastSeed+1]);
                var region = WorldSnapshotCodec.DecodeBootSource(seedText, out _);
                if (!AutomaticWorldHost.Eligible(region)) throw new InvalidDataException("Beta start must use automatic generation");
                string regionPath = RegionCache.PathFor(region.id);
                if (!File.Exists(regionPath)) File.WriteAllText(regionPath, seedText);
                if (!File.Exists(PlayerProfile.StorageOverride))
                {
                    var profile = new PlayerProfile
                    {
                        region = region.id, regionTimeZone = region.timeZone,
                        quality = 2, clarity = 0, weatherMode = 0, clockMode = 0,
                        fixedHour = 14, livingScenery = true
                    };
                    int testStart=Array.IndexOf(arguments,"-earthUnscriptedBetaSmokeStart");
                    if(VerificationOutput!=null && testStart>=0 && testStart+2<arguments.Length)
                    {
                        profile.customStart=true;profile.startRegion=region.id;
                        profile.startX=float.Parse(arguments[testStart+1],System.Globalization.CultureInfo.InvariantCulture);
                        profile.startZ=float.Parse(arguments[testStart+2],System.Globalization.CultureInfo.InvariantCulture);
                        if(float.IsNaN(profile.startX)||float.IsNaN(profile.startZ)||Mathf.Abs(profile.startX)>1000000||Mathf.Abs(profile.startZ)>1000000)throw new ArgumentException("Invalid verification start");
                    }
                    if (!profile.Save()) throw new IOException("Could not create the separate beta save");
                }
                if (VerificationOutput != null && Array.IndexOf(arguments, "-earthUnscriptedBetaStartupSmoke") >= 0)
                    BootTelemetry.EnableIsolated();
                GameSession.SuppressAutomaticLaunch = VerificationOutput!=null && Array.IndexOf(arguments,"-earthUnscriptedBetaRideSmoke")>=0;
            }
            catch (Exception error)
            {
                StartupError = error.ToString();
                Debug.LogException(error);
                Application.Quit(2);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallIndicator()
        {
            if (!Enabled) return;
            // Place selection replaces the driving scene. These app services
            // must survive it, then rebind to the newly created GameSession.
            var services=new GameObject("Single player beta session services");
            UnityEngine.Object.DontDestroyOnLoad(services);
            services.AddComponent<BetaIndicator>();services.AddComponent<BetaDrivingTelemetry>();
            if(Application.platform==RuntimePlatform.OSXPlayer)services.AddComponent<MacDrivingActivity>();
            if (VerificationOutput != null)
            {
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaWindshieldSmoke")>=0)new GameObject("Explicit cockpit rain and camper verification").AddComponent<BetaWindshieldRainVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedTerrainPavementSmoke")>=0)new GameObject("Explicit landing pavement clearance verification").AddComponent<BetaTerrainPavementVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta10WorldSmoke")>=0)new GameObject("Explicit Fort William terrain verification").AddComponent<Beta10NativeWorldVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBeta10BridgeWaterSmoke")>=0)new GameObject("Explicit Kessock water verification").AddComponent<Beta10NativeBridgeWaterVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaUpdateSmoke")>=0)new GameObject("Explicit native update verification").AddComponent<BetaUpdateVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedPresentationProof")>=0)new GameObject("Earth Unscripted presentation verification").AddComponent<BetaBrandVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedGraphicsProof")>=0)new GameObject("Shared game graphics verification").AddComponent<SharedGraphicsVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaLaunchSmoke")>=0)new GameObject("Explicit launch regression proof").AddComponent<BetaLaunchVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaTrainSmoke")>=0)new GameObject("Explicit native train traversal proof").AddComponent<BetaTrainVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaControllerSmoke")>=0)new GameObject("Explicit controller menu and feedback proof").AddComponent<BetaControllerVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaRideSmoke")>=0)new GameObject("Explicit suspension and junction ride proof").AddComponent<BetaRideVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaStartupSmoke")>=0)new GameObject("Explicit cached startup verification").AddComponent<BetaStartupVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaAudioSwapSmoke")>=0)new GameObject("Explicit audio and parked car verification").AddComponent<BetaAudioSwapVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaPoliceSmoke")>=0)new GameObject("Explicit patrol performance verification").AddComponent<BetaPolicePerformanceVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaCarSmoke")>=0)new GameObject("Explicit car-pack verification").AddComponent<BetaVehiclePackVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaCoastSmoke")>=0)new GameObject("Explicit coastal verification").AddComponent<BetaCoastVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaInteractionSmoke")>=0)new GameObject("Explicit world interaction verification").AddComponent<BetaInteractionVerification>();
                else if(Array.IndexOf(Environment.GetCommandLineArgs(),"-earthUnscriptedBetaLocationSmoke")>=0)BetaLocationVerification.Install();
                else new GameObject("Explicit beta verification").AddComponent<BetaSmokeVerification>();
            }
        }
    }

    public sealed class BetaIndicator : MonoBehaviour
    {
        GUIStyle style;
        void OnGUI()
        {
            if (SinglePlayerBeta.StartupError != null)
            {
                GUI.Box(new Rect(20, 20, Screen.width - 40, 180), "Beta could not start\n" + SinglePlayerBeta.StartupError);
                return;
            }
            if(style==null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 11 };
                style.normal.textColor = InterfaceTheme.Muted;
            }
            GUI.Label(new Rect(Screen.width - 620, 12, 600, 28), "EARTH UNSCRIPTED · BETA · "+GameGraphics.Name.ToUpperInvariant()+" GRAPHICS", style);
        }
    }
}
