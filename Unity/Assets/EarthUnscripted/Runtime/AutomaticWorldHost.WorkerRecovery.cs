using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthUnscripted
{
    public sealed partial class AutomaticWorldHost
    {
        string workerExecutable, workerInput, workerCache, expectedWorkerIdentity, expectedSessionHash;
        string workerCommandPrefix = "";
        int workerPort, workerAttempts;
        bool restartingWorker;
        double workerRetryAt;
        public int WorkerRestarts { get; private set; }

        // The endpoint remains stable, so in-flight renderer requests can retry
        // against the replacement owner. Resident geometry stays usable while
        // the independent coverage brake protects its outer boundary.
        IEnumerator RestartOwnedWorker()
        {
            restartingWorker = true;
            status = "Restarting world data service…";
            bool success = false;
            try
            {
                worker?.Dispose(); worker = null;
                string evidence = Path.Combine(sessionFolder, "Worker-recovery-" + (++workerAttempts));
                var launch = new System.Diagnostics.ProcessStartInfo(workerExecutable)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = sessionFolder,
                    Arguments = workerCommandPrefix + "--session " + Quote(workerInput) + " --cache " + Quote(workerCache) +
                        " --evidence " + Quote(evidence) + " --port " + workerPort };
                Exception failure = null;
                try { worker = System.Diagnostics.Process.Start(launch); }
                catch (Exception error) { failure = error; }
                if (failure != null) { status = "World data service will retry: " + failure.Message; yield break; }
                double began = Time.realtimeSinceStartupAsDouble;
                string endpointPath = Path.Combine(evidence, "endpoint.json");
                while (!File.Exists(endpointPath))
                {
                    if (retired || worker.HasExited || Time.realtimeSinceStartupAsDouble - began > 45) yield break;
                    yield return null;
                }
                Endpoint endpoint = null;
                try { endpoint = JsonUtility.FromJson<Endpoint>(File.ReadAllText(endpointPath)); }
                catch (Exception error) { failure = error; }
                if (failure != null || endpoint == null || !WorkerHttp.ValidToken(endpoint.token) || endpoint.pid != worker.Id ||
                    endpoint.url != "http://127.0.0.1:" + workerPort) yield break;
                WorkerHttp.Register(workerPort, endpoint.token);
                using (var health = WorkerHttp.Authorize(UnityWebRequest.Get(endpoint.url + "/health")))
                {
                    health.timeout = 15;
                    yield return health.SendWebRequest();
                    if (retired || health.result != UnityWebRequest.Result.Success) yield break;
                    Health value = null;
                    try { value = JsonUtility.FromJson<Health>(health.downloadHandler.text); }
                    catch (Exception error) { failure = error; }
                    if (failure != null || value == null || !value.ready || value.protocol != 1 ||
                        value.workerIdentity != expectedWorkerIdentity || value.sessionSHA256 != expectedSessionHash) yield break;
                }
                success = true; WorkerRestarts++;
                status = "World data service resumed";
            }
            finally
            {
                restartingWorker = false;
                if (!success)
                {
                    try { if (worker != null && !worker.HasExited) { worker.Kill(); worker.WaitForExit(2000); } }
                    catch (InvalidOperationException) { }
                    workerRetryAt = Time.realtimeSinceStartupAsDouble + Math.Min(30, Math.Pow(2, Math.Min(workerAttempts, 5)));
                    status = "World data service is retrying automatically…";
                }
            }
        }

        public void InterruptWorkerForQa()
        {
            if (Application.identifier != "com.earthunscripted.qa.automaticworld" || !Ready || worker == null || worker.HasExited)
                throw new InvalidOperationException("Owned ready QA worker required");
            worker.Kill();
        }
    }
}
