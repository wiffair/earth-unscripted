using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using RoadRush.Core800;
using UnityEngine;

namespace RoadRush
{
    public sealed partial class GameSession
    {
        internal const string ScopedSessionFlag800 = "-roadRushActualScopedSession800";
        internal const string ScopedCompany800 = "RoadRushQA";
        internal const string ScopedProduct800 = "RoadRushQAActualScoped800";
        internal const string ScopedIdentifier800 = "games.blackwood.roadrush.qa.actualscoped800";
        const double ScopedPreparationTimeoutSeconds = 300;
        CancellationTokenSource scopedPreparationCancellation800;
        Task<PreparedScopedSession800> scopedPreparation800;
        bool scopedReaderOnly800;

        /// <summary>
        /// Closed QA mode, not a caller completeness flag. Set only by the real
        /// boot after explicit CLI opt-in AND isolated company/product/storage.
        /// It refuses driving/LAN until the complete normal-world consumers have
        /// a reviewed same-mesh integration. Source and career are not replaced.
        /// </summary>
        internal bool IsScopedReaderSession800 => scopedReaderOnly800;

        /// <summary>
        /// Runs in actual Awake BEFORE PlayerProfile.Load. Opt-in on a normal or
        /// incorrectly built app refuses before reading the production career.
        /// The external guard still binds executable/source/settings and all inputs.
        /// </summary>
        void ValidateScopedIsolationBeforeProfileLoad800()
        {
            var arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, ScopedSessionFlag800) < 0) return;
            if (Application.companyName != ScopedCompany800 || Application.productName != ScopedProduct800 ||
                Application.identifier != ScopedIdentifier800)
                throw new InvalidOperationException("Scoped reader requires its exact separate QA application domain.");
            string profile = RequiredScopedArgument800(arguments, "-roadRushScopedProfile800");
            const string PrivatePrefix = "/private/tmp/rr019-actual-scoped-session-";
            if (!profile.StartsWith(PrivatePrefix, StringComparison.Ordinal) ||
                !profile.EndsWith("/profile.json", StringComparison.Ordinal) || !System.IO.File.Exists(profile) ||
                (System.IO.File.GetAttributes(profile) & System.IO.FileAttributes.ReparsePoint) != 0 ||
                (!string.IsNullOrEmpty(PlayerProfile.StorageOverride) && PlayerProfile.StorageOverride != profile))
                throw new InvalidOperationException("Fresh explicit private scoped QA profile required before profile load.");
            // Directory symlinks could redirect an otherwise lexical private name.
            for (var directory = new System.IO.FileInfo(profile).Directory; directory != null; directory = directory.Parent)
                if ((directory.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Scoped QA profile parents must be real directories.");
            PlayerProfile.StorageOverride = profile;
        }

        bool TryBeginScopedSession800(string[] arguments, int request)
        {
            if (Array.IndexOf(arguments, ScopedSessionFlag800) < 0) return false;
            if (string.IsNullOrEmpty(PlayerProfile.StorageOverride) ||
                Application.companyName != ScopedCompany800 || Application.productName != ScopedProduct800 ||
                Application.identifier != ScopedIdentifier800)
                throw new InvalidOperationException("Explicit isolated QA build and profile storage required for scoped session.");
            if (Profile.customStart)
                throw new InvalidOperationException("Scoped diagnostic refuses an unrelated saved custom start.");
            string cooked = RequiredScopedArgument800(arguments, "-roadRushScopedCooked800");
            string context = RequiredScopedArgument800(arguments, "-roadRushScopedSourceContext800");
            scopedReaderOnly800 = true;
            scopedPreparationCancellation800 = new CancellationTokenSource();
            var token = scopedPreparationCancellation800.Token;
            scopedPreparation800 = Task.Run(() => PreparedScopedSession800.PrepareReviewed(cooked, context, token), token);
            // Observe even a late cancelled worker failure; no unobserved exception
            // or scene reference is retained by the pure preparation task.
            _ = scopedPreparation800.ContinueWith(task => { var ignored = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            StartCoroutine(GuardBoot(BootScopedSession800(arguments, request, token)));
            return true;
        }

        static string RequiredScopedArgument800(string[] arguments, string key)
        {
            int index = Array.IndexOf(arguments, key);
            if (index < 0 || index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
                throw new ArgumentException("Missing scoped QA argument " + key);
            return System.IO.Path.GetFullPath(arguments[index + 1]);
        }

        IEnumerator BootScopedSession800(string[] arguments, int request, CancellationToken token)
        {
            Task<PreparedScopedSession800> work = scopedPreparation800;
            double began = Time.realtimeSinceStartupAsDouble;
            while (!work.IsCompleted)
            {
                if (request != bootGeneration) yield break;
                if (Time.realtimeSinceStartupAsDouble - began > ScopedPreparationTimeoutSeconds)
                    throw new TimeoutException("Reviewed scoped preparation timed out.");
                yield return null;
            }
            token.ThrowIfCancellationRequested();
            if (request != bootGeneration) yield break;
            if (work.IsFaulted) throw work.Exception.GetBaseException();
            if (work.IsCanceled) throw new OperationCanceledException("Scoped preparation cancelled.");
            var prepared = work.Result;
            BootSourceSha = prepared.Plan.RawDocumentDigest;
            BootStatus = "Uploading verified scoped roads and terrain…";
            World = OwnBootRoot(new GameObject("Actual scoped session world800")).AddComponent<WorldBuilder>();
            yield return World.BuildScopedSession800(prepared, token, () => request == bootGeneration && !token.IsCancellationRequested);
            if (request != bootGeneration) yield break;
            BootStatus = "Preparing actual scoped vehicle cohort…";
            yield return PrepareActorStages(Profile.region, arguments, request);
            if (request == bootGeneration && !BootFailed && !BootReady)
                throw new InvalidOperationException("Actual scoped actor stages did not commit.");
        }

        void CommitScopedSessionActors800()
        {
            if (!scopedReaderOnly800) return;
            if (!World || World.ScopedSession800 == null || World.ScopedGround800 == null)
                throw new InvalidOperationException("Actual scoped world factory missing at roster commit.");
            BindActualVehicleScope(World.ScopedGround800);
            scopedPreparation800 = null;
            scopedPreparationCancellation800?.Dispose();
            scopedPreparationCancellation800 = null;
        }

        void CancelScopedSession800()
        {
            scopedPreparationCancellation800?.Cancel();
            scopedPreparationCancellation800?.Dispose();
            scopedPreparationCancellation800 = null;
            scopedPreparation800 = null;
            // Before BootSceneOwnership.Quiesce/Destroy, not just OnDestroy.
            if (World) World.RetireScopedSession800("actual session cancelled or retired");
        }

        bool RefuseScopedDriving800()
        {
            if (!scopedReaderOnly800) return false;
            Notify("Scoped reader QA only · normal-world driving/LAN not admitted");
            return true;
        }
    }
}
