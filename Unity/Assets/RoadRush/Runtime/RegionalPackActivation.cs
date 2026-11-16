using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RoadRush.Packs;
using UnityEngine;

namespace RoadRush
{
    /// <summary>
    /// Owns the local user's exact pack preference and its offline scenery controller.
    /// Career slots never own or overwrite this setting. A new world/generation binds
    /// afresh; a failed preference write keeps the current scene and selection intact.
    /// </summary>
    public sealed class RegionalPackActivation : MonoBehaviour
    {
        sealed class Preference { public WorldPackSelection Choice; public InstalledWorldPack Installed; }
        GameSession session;
        RegionalPackWindowController controller;
        GeoWorld boundWorld;
        int boundGeneration = -1, preferenceVersion, boundPreference = -1;
        string root, engineVersion, message = "Reading saved pack choice…";
        WorldPackSelection choice = WorldPackSelection.Disabled;
        InstalledWorldPack installed;
        bool loaded, disposed;
        Task<Preference> preference;
        CancellationTokenSource cancellation;

        public bool PreferenceBusy => preference != null;
        public WorldPackSelection Selected => choice;
        public string SelectedAttribution => choice.Enabled ? installed?.Manifest?.Attribution : null;
        public string Status => PreferenceBusy ? message : message + (choice.Enabled && boundWorld && controller ? " · " + controller.Status : "");
        public RegionalPackWindowController Controller => controller;
        public static string LibraryRoot()
        {
            string save = PlayerProfile.StorageOverride;
            return Path.Combine(string.IsNullOrEmpty(save) ? RoadRush.SinglePlayerBeta.ProductRoot
                : Path.GetDirectoryName(Path.GetFullPath(save)), "ImportedWorldPacks");
        }
        internal void Initialize(GameSession owner)
        {
            if (session) throw new InvalidOperationException("Pack activation already initialized");
            session = owner; root = LibraryRoot(); engineVersion = Application.version;
            string path = root;
            StartPreference(token => {
                token.ThrowIfCancellationRequested(); var saved = WorldPackSelection.Load(path);
                return new Preference { Choice = saved, Installed = saved.Enabled ? new WorldPackStore(path).Locate(saved) : null };
            }, "Reading saved pack choice…");
        }
        void StartPreference(Func<CancellationToken, Preference> operation, string status)
        {
            if (disposed || PreferenceBusy) throw new InvalidOperationException("Another pack preference operation is active");
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            message = status; preference = Task.Run(() => operation(token), token);
        }
        public void Preview(WorldPackInventoryItem row)
        {
            if (row?.Manifest == null) throw new ArgumentException("Readable installed revision required");
            if (!session || !session.BootReady || session.Network.Active || session.IsScopedReaderSession800)
                throw new InvalidOperationException("Leave LAN before selecting a local scenery preview");
            string path = root, version = engineVersion;
            var wanted = WorldPackSelection.For(row.Manifest, true);
            StartPreference(token => {
                var store = new WorldPackStore(path); var exact = store.Locate(wanted);
                // Checks run off the render thread, before publishing user intent. The
                // controller checks stored bytes again when binding each new world.
                var readers = new PackEngineSupport(version, new System.Collections.Generic.Dictionary<string, int> {
                    { "regional-index", 1 }, { "regional-buildings", 1 }, { "provenance", 1 } });
                var reader = WorldPackRegionalReader.OpenVerified(store, exact, readers, token);
                reader.AuditReferenceWindow(new RegionalDraft.RegionalCollection.Limits(), token);
                wanted.Save(path, token);
                return new Preference { Choice = wanted, Installed = exact };
            }, "Checking revision before saving preview choice…");
        }
        public void Disable()
        {
            if (!session || !session.BootReady || session.Network.Active)
                throw new InvalidOperationException("Leave LAN before changing local scenery");
            string path = root;
            StartPreference(token => { var disabled = WorldPackSelection.Disabled; disabled.Save(path, token);
                return new Preference { Choice = disabled }; }, "Saving default scenery choice…");
        }
        public void CancelPreference() => cancellation?.Cancel();
        void Update()
        {
            if (disposed || !session) return;
            if (preference != null && preference.IsCompleted)
            {
                var done = preference; preference = null; cancellation.Dispose(); cancellation = null;
                if (done.IsCanceled) message = "Choice cancelled; current scenery retained";
                else if (done.IsFaulted) message = "Pack choice unchanged: " + done.Exception.GetBaseException().Message;
                else
                {
                    choice = done.Result.Choice; installed = done.Result.Installed; loaded = true; preferenceVersion++;
                    message = choice.Enabled ? "Offline scenery preview selected" : "Default scenery selected";
                }
            }
            if (!loaded || !session.BootReady || !session.World) return;
            var world = session.World.GeographicWorld;
            if (session.Network.Active || session.IsScopedReaderSession800 || session.World.IsCircuit || !world)
            {
                if (controller && controller.Active) controller.StopPreview();
                boundWorld = null; boundGeneration = boundPreference = -1;
                if (!PreferenceBusy && choice.Enabled) message = session.Network.Active
                    ? "Local scenery preview suspended during LAN" : "Scenery choice saved for geographic road areas";
                return;
            }
            if (boundWorld == world && boundGeneration == world.SurfaceGeneration && boundPreference == preferenceVersion) return;
            if (controller) controller.StopPreview();
            boundWorld = world; boundGeneration = world.SurfaceGeneration; boundPreference = preferenceVersion;
            try
            {
                if (!choice.Enabled)
                {
                    if (world.BuildingWindowIdentity != null || world.BuildingWindowBusy) world.QueueDefaultBuildings(session);
                    message = "Default scenery selected";
                }
                else
                {
                    if (!controller) controller = gameObject.AddComponent<RegionalPackWindowController>();
                    controller.OpenSelected(session, new WorldPackStore(root), installed, choice.AllowCandidatePreview);
                }
            }
            catch (Exception error) { message = "Current scenery retained: " + error.Message; }
        }
        void OnDestroy()
        {
            disposed = true; if (controller) controller.StopPreview();
            var source = cancellation; cancellation = null;
            if (source == null) return;
            source.Cancel(); var pending = preference; preference = null;
            if (pending == null) source.Dispose();
            else pending.ContinueWith(done => { if (done.IsFaulted) _ = done.Exception; source.Dispose(); }, TaskScheduler.Default);
        }
    }
}
