using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using NativeWorld = EarthUnscripted.NewbridgePrototype.NewbridgeWorld;

namespace EarthUnscripted
{
    // One world owns one worker and renderer. A normal session retains its car,
    // camera, physics, audio, profile and actor activation transaction.
    public sealed partial class AutomaticWorldHost : MonoBehaviour
    {
        public string PublicRailEndpoint=>workerPort>0?"http://127.0.0.1:"+workerPort+"/trains":"";

        [Serializable] sealed class SessionInput
        {
        public double latitude, longitude;
            public float datum, headingX, headingZ;
            public string countryCode, trafficSide, name;
            public Vector3 start;
        }
        [Serializable] sealed class Endpoint { public string url; public int pid; }
        [Serializable] sealed class Health
        {
            public bool ready;
            public int protocol;
            public string workerIdentity, sessionSHA256;
        }
        System.Diagnostics.Process worker;
        string sessionFolder;
        public NativeWorld Renderer { get; private set; }
        public GeoRegion Region { get; private set; }
        string status = "Starting automatic world…";
        public string Status => restartingWorker ? status : Renderer ? Renderer.LoadingStatus : status;
        public bool Ready => Renderer && Renderer.Ready && Region != null;
        public Transform Observer;
        public Camera View;
        public VehicleController Player;
        bool retired;
        bool queriesDirty;
        Task<GeoRegion> queryPreparation;
        public int QueryRevision {get;private set;}

        public static bool Eligible(GeoRegion source)
        {
            // Frozen peer/country/circuit snapshots remain their own canonical
            // world. Only ordinary downloaded live areas enter this backend.
            return source != null && !source.circuit && source.id != null &&
                source.id.StartsWith("download-v4-", StringComparison.Ordinal) &&
                !source.id.Contains("-p") && string.IsNullOrEmpty(source.networkMapId);
        }

        static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public IEnumerator Prepare(GeoRegion source, Vector3 start, Vector3 heading, Func<bool> cancelled)
        {
            string helpers = Path.GetFullPath(Path.Combine(Application.dataPath,
                Application.platform == RuntimePlatform.OSXPlayer ? "Resources/AutomaticWorld" : "../Helpers/AutomaticWorld"));
#if UNITY_EDITOR
            helpers = Path.GetFullPath(Path.Combine(Application.dataPath, "../AutomaticWorldWorker"));
#endif
            string executable = Path.Combine(helpers, Application.platform == RuntimePlatform.WindowsPlayer
                ? "Python/python.exe" : "EarthUnscriptedWorldWorker");
            workerCommandPrefix = Application.platform == RuntimePlatform.WindowsPlayer
                ? "-X utf8 " + Quote(Path.Combine(helpers, "_internal/worker_entry.py")) + " " : "";
            string manifestPath = Path.Combine(helpers, "package-files.json");
            if (!File.Exists(executable) || !File.Exists(manifestPath))
                throw new FileNotFoundException("Automatic world worker is missing at " + helpers);
            var verification = Task.Run(() =>
            {
                var pins = JObject.Parse(File.ReadAllText(manifestPath));
                foreach (var item in pins.Properties())
                {
                    string path = Path.GetFullPath(Path.Combine(helpers, item.Name));
                    if (!path.StartsWith(helpers + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                        !File.Exists(path) || Hash(File.ReadAllBytes(path)) != (string)item.Value)
                        throw new InvalidDataException("Automatic world worker failed its file check: " + item.Name);
                }
                return (string)JObject.Parse(File.ReadAllText(Path.Combine(helpers,
                    "_internal/worker-code-identity.json")))["workerIdentity"];
            });
            while (!verification.IsCompleted)
            { if (cancelled()) throw new OperationCanceledException(); yield return null; }
            string identity = verification.GetAwaiter().GetResult();
            workerExecutable = executable;
            expectedWorkerIdentity = identity;
            sessionFolder = Path.Combine(Application.temporaryCachePath, "AutomaticWorldSessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionFolder);
            var config = new SessionInput { latitude = source.latitude, longitude = source.longitude,
                datum = source.datum, countryCode = source.countryCode, name = source.name,
                trafficSide = source.drivingSide < 0 ? "left" : "right", start = start,
                headingX = heading.x, headingZ = heading.z };
            byte[] input = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(config));
            expectedSessionHash = Hash(input);
            string inputPath = Path.Combine(sessionFolder, "session.json");
            File.WriteAllBytes(inputPath, input);
            workerInput = inputPath;
            workerCache = Path.Combine(EarthUnscripted.SinglePlayerBeta.ProductRoot, "AutomaticWorldCache");
            var verificationArguments=Environment.GetCommandLineArgs();int cacheArgument=Array.IndexOf(verificationArguments,"-earthUnscriptedInternationalCache");
            if(SinglePlayerBeta.VerificationOutput!=null&&cacheArgument>=0&&cacheArgument+1<verificationArguments.Length)
            {
                string candidate=Path.GetFullPath(verificationArguments[cacheArgument+1]);
                if(!candidate.StartsWith("/private/tmp/EarthUnscriptedBetaWorld-",StringComparison.Ordinal))throw new InvalidDataException("Isolated world cache path required");
                workerCache=candidate;
            }
            var launch = new System.Diagnostics.ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = sessionFolder };
            // ArgumentList is unavailable in Unity's .NET Standard runtime.
            launch.Arguments = workerCommandPrefix + "--session " + Quote(inputPath) + " --evidence " + Quote(Path.Combine(sessionFolder, "Worker")) +
                " --cache " + Quote(workerCache);
            worker = System.Diagnostics.Process.Start(launch);
            string endpointPath = Path.Combine(sessionFolder, "Worker/endpoint.json");
            double began = Time.realtimeSinceStartupAsDouble;
            while (!File.Exists(endpointPath))
            {
                CheckWorker(cancelled);
                if (Time.realtimeSinceStartupAsDouble - began > 45) throw new TimeoutException("World worker startup timed out");
                yield return null;
            }
            var endpoint = JsonUtility.FromJson<Endpoint>(File.ReadAllText(endpointPath));
            if (endpoint.pid != worker.Id || !Uri.TryCreate(endpoint.url, UriKind.Absolute, out var address) ||
                address.Scheme != "http" || address.Host != "127.0.0.1" || address.AbsolutePath != "/")
                throw new InvalidDataException("World worker ownership mismatch");
            workerPort = address.Port;
            using (var request = UnityWebRequest.Get(endpoint.url + "/health"))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) throw new IOException("World worker health check failed");
                var health = JsonUtility.FromJson<Health>(request.downloadHandler.text);
                if (!health.ready || health.protocol != 1 || health.workerIdentity != identity || health.sessionSHA256 != Hash(input))
                    throw new InvalidDataException("World worker input/code identity mismatch");
            }
            CheckWorker(cancelled);
            Renderer = gameObject.AddComponent<NativeWorld>();
            Renderer.EnableGeometryDiagnostics(Application.identifier == "com.earthunscripted.qa.automaticworld" &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-automaticWorldBenchmark")<0);
            yield return Renderer.PrepareEmbedded(endpoint.url, start, heading, source.name, source.attribution, cancelled);
            Region = AutomaticWorldFacade.Create(source, Renderer);
            Renderer.AreasChanged += RefreshQueries;
            status = "Starting roads ready";
        }

        static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        void CheckWorker(Func<bool> cancelled)
        {
            if (retired || cancelled()) throw new OperationCanceledException();
            if (worker == null || worker.HasExited) throw new IOException("Automatic world worker stopped before readiness");
        }
        void RefreshQueries() { queriesDirty=true; }
        void Update()
        {
            using var frameWork=WorkTelemetry.Measure("frame.world-host",null,8);
            if(queryPreparation!=null && queryPreparation.IsCompleted)
            {
                if(queryPreparation.IsFaulted)Debug.LogException(queryPreparation.Exception);
                else if(Region!=null){Region.AdoptAutomaticQueries(queryPreparation.Result);QueryRevision++;}
                queryPreparation=null;
            }
            if(queriesDirty && Region!=null && Renderer && queryPreparation==null)
            {
                queriesDirty=false;
                var maps=Renderer.ResidentAreas.OrderBy(area=>area.tileX).ThenBy(area=>area.tileZ).ToArray();
                var owner=Region;
                queryPreparation=Task.Run(()=>AutomaticWorldFacade.QuerySnapshot(owner,maps));
            }
            if (Ready && !retired && !restartingWorker && (worker == null || worker.HasExited) &&
                Time.realtimeSinceStartupAsDouble >= workerRetryAt) StartCoroutine(RestartOwnedWorker());
            if (!Ready || !Observer) return;
            Renderer.SetEmbeddedObserver(relocationTarget ?? Observer.position, Observer.forward, View);
            status = Renderer.LoadingStatus;
            if (Player)
            {
                // Stopping distance in metres, including a 2 s loading margin.
                // Every branch is forecast by the worker; this guard follows
                // the human's current motion and never chooses their route.
                float speed = Player.Body.linearVelocity.magnitude;
                var direction = Vector3.ProjectOnPlane(Player.Body.linearVelocity, Vector3.up).normalized;
                if (direction.sqrMagnitude < .1f) direction = Observer.forward;
                float distance = speed * speed / 8f + speed * 2f + 30f;
                bool coverage = true;
                for (float at = 0; at <= distance; at += 24)
                    if (!Renderer.HasGeometryAt(Observer.position + direction * at)) { coverage = false; break; }
                Player.StreamingBrake = Relocating || !coverage;
            }
        }
        void OnDestroy()
        {
            retired = true;
            if (Player) Player.StreamingBrake = false;
            if (Renderer) Renderer.AreasChanged -= RefreshQueries;
            if (worker != null)
            {
                try { if (!worker.HasExited) worker.Kill(); worker.WaitForExit(2000); }
                catch (InvalidOperationException) { }
                finally { worker.Dispose(); worker = null; }
            }
        }
    }
}
