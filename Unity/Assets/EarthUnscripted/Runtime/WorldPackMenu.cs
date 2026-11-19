using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>
    /// Local immutable pack library. All archive and payload work runs off the render thread.
    /// Installing or inspecting a revision never changes a career or the active world.
    /// </summary>
    public sealed class WorldPackMenu : IDisposable
    {
        sealed class Result
        {
            public IReadOnlyList<WorldPackInventoryItem> Rows;
            public string Message;
            public IReadOnlyList<PackAttestationItem> Certificates;
            public ISet<string> TrustedIssuers;
            public VerifiedGameBuild Build;
            public string BuildProblem;
            public IReadOnlyList<PackValidationState?> CertificateStates;
        }
        string root, archivePath = "", status = "", engineVersion;
        IReadOnlyList<WorldPackInventoryItem> rows = Array.Empty<WorldPackInventoryItem>();
        Task<Result> work;
        CancellationTokenSource cancellation;
        Vector2 scroll, certificateScroll;
        WorldPackInventoryItem certificatePack;
        string certificatePath = "";
        IReadOnlyList<PackAttestationItem> certificates = Array.Empty<PackAttestationItem>();
        ISet<string> trustedIssuers = new HashSet<string>(StringComparer.Ordinal);
        string pendingIssuer, pendingRecord;
        PackGameBuildSource buildSource;
        VerifiedGameBuild verifiedBuild;
        string buildProblem = "Build fingerprint has not been checked.";
        IReadOnlyList<PackValidationState?> certificateStates = Array.Empty<PackValidationState?>();
        GUIStyle heading, body, small, button;
        bool disposed, refreshPending;
        RegionalPackActivation activation;
        GameSession owner;
        bool OperationBusy => work != null || activation && activation.PreferenceBusy;
        public bool Busy => OperationBusy || refreshPending;
        public string Status => status;
        readonly ControllerPackBrowser controllerBrowser=new ControllerPackBrowser();
        public string ControllerPage=>controllerBrowser.IsOpen?"files/"+controllerBrowser.Page:certificatePack!=null?(pendingIssuer!=null?"signer-review":"certificates")+"/"+certificatePack.Manifest.Id+"/"+certificatePack.Manifest.Revision+"/"+(Busy?"checking":"ready"):Busy?"library-checking":"library";
        public int RevisionCount => rows.Count;
        public void ImportArchive(string path) { if (Busy || disposed) return; archivePath = path ?? ""; Import(); }
        public void CheckContent(int index) { if (!Busy && !disposed && index >= 0 && index < rows.Count) Inspect(rows[index]); }
        public void PreviewRevision(int index)
        {
            if (Busy || disposed || index < 0 || index >= rows.Count || !activation) return;
            try { activation.Preview(rows[index]); status = "Preview choice is separate from your career. This pack is not gameplay-certified."; }
            catch (Exception error) { status = "Preview unavailable: " + Clean(error.Message); }
        }
        public void DisablePreview()
        {
            if (Busy || disposed || !activation) return;
            try { activation.Disable(); status = "Default scenery will return when its replacement is ready."; }
            catch (Exception error) { status = "Scenery unchanged: " + Clean(error.Message); }
        }
        // Certificate inspection never sets publisher trust or changes a selected revision.
        public void ShowCertificates(int index)
        {
            if (Busy || disposed || index < 0 || index >= rows.Count || rows[index].Manifest == null) return;
            certificatePack = rows[index]; certificateScroll = Vector2.zero;
            certificates = Array.Empty<PackAttestationItem>(); ReadCertificates(null);
        }
        public void CloseCertificates() { cancellation?.Cancel(); certificatePack = null; pendingIssuer = pendingRecord = null; }
        public void ImportCertificate(string path)
        {
            if (Busy || disposed || certificatePack == null) return;
            certificatePath = path ?? ""; ReadCertificates(certificatePath.Trim().Trim('"'));
        }
        void ReadCertificates(string source)
        {
            var row = certificatePack; string path = root;
            verifiedBuild = null; buildProblem = "Build fingerprint check pending; no exact-build claim.";
            Start(token => {
                var store = new WorldPackStore(path);
                var manifest = store.VerifyInstalled(row.Directory, row.Manifest.ManifestSha256, token);
                var records = new WorldPackAttestationStore(path);
                if (source != null) records.Import(source, manifest.ManifestSha256, token);
                var claims = records.Inspect(manifest.ManifestSha256, token);
                return CertificateResult(store, manifest, claims, new WorldPackIssuerTrust(path).Load(token), token,
                    (source == null ? "Certificate records checked." : "Certificate imported. Import does not grant signer trust.") +
                        " Pack files and selection unchanged.");
            }, source == null ? "Checking pack identity and certificate records…" : "Checking certificate signature and pack identity…");
        }

        public void RequestIssuerTrust(int index)
        {
            if (Busy || disposed || certificatePack == null || index < 0 || index >= certificates.Count || certificates[index].Record == null) return;
            pendingIssuer = certificates[index].Record.IssuerFingerprint;
            pendingRecord = certificates[index].RecordSha256;
        }
        public void CancelIssuerTrust() { pendingIssuer = pendingRecord = null; }
        public void ConfirmIssuerTrust()
        {
            if (Busy || disposed || pendingIssuer == null || certificatePack == null) return;
            string fingerprint = pendingIssuer, hash = pendingRecord; CancelIssuerTrust();
            ChangeIssuerTrust(fingerprint, hash, true);
        }
        public void RevokeIssuerTrust(int index)
        {
            if (Busy || disposed || certificatePack == null || index < 0 || index >= certificates.Count || certificates[index].Record == null) return;
            ChangeIssuerTrust(certificates[index].Record.IssuerFingerprint, certificates[index].RecordSha256, false);
        }
        void ChangeIssuerTrust(string fingerprint, string recordHash, bool trusted)
        {
            var row = certificatePack; string path = root;
            verifiedBuild = null; buildProblem = "Build fingerprint check pending; no exact-build claim.";
            Start(token => {
                var store = new WorldPackStore(path);
                var manifest = store.VerifyInstalled(row.Directory, row.Manifest.ManifestSha256, token);
                var records = new WorldPackAttestationStore(path); var claims = records.Inspect(manifest.ManifestSha256, token);
                // Recheck the exact record before committing a decision made in the confirmation view.
                if (!claims.Any(c => c.RecordSha256 == recordHash && c.Record != null && c.Record.IssuerFingerprint == fingerprint))
                    throw new InvalidDataException("Certificate changed; inspect it again before changing trust.");
                var trust = new WorldPackIssuerTrust(path); trust.SetTrusted(fingerprint, trusted, token);
                return CertificateResult(store, manifest, claims, trust.Load(token), token,
                    trusted ? "You now trust this test-report signer locally. Test scope is checked separately." :
                        "Signer trust revoked. Stored certificates and pack files retained.");
            }, trusted ? "Saving your signer trust decision…" : "Revoking local signer trust…");
        }

        Result CertificateResult(WorldPackStore store, WorldPackManifest manifest,
            IReadOnlyList<PackAttestationItem> claims, ISet<string> trusted, CancellationToken token, string message)
        {
            VerifiedGameBuild build = null; string problem = null;
            try
            {
                if (buildSource == null) problem = "Build fingerprint unavailable on this editor or platform.";
                else build = buildSource.Verify(token);
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is Newtonsoft.Json.JsonException || error is ArgumentException)
            { problem = "Build fingerprint unavailable or files differ. No exact-build validation claim."; }
            string subset = WorldPackCompatibility.Resolve(manifest, Readers(engineVersion)).SubsetIdentity;
            // RSA checks belong to this I/O worker, not to OnGUI's repeated layout/repaint passes.
            var states = new List<PackValidationState?>();
            foreach (var claim in claims)
            {
                token.ThrowIfCancellationRequested();
                states.Add(claim.Record == null ? (PackValidationState?)PackValidationState.SignatureInvalid :
                    build == null ? null : (PackValidationState?)claim.Record.Evaluate(manifest.ManifestSha256, subset,
                        build.EngineVersion, build.Sha256, build.Platform, trusted));
            }
            return new Result { Rows = store.Discover(), Certificates = claims, TrustedIssuers = trusted,
                Build = build, BuildProblem = problem, CertificateStates = states.AsReadOnly(),
                Message = message + " " + (build == null ? problem : "Current build fingerprint verified (" + build.FileCount + " files).") };
        }
        public PackValidationState? CertificateState(int index)
        {
            if (certificatePack == null) return null;
            if (index < 0 || index >= certificates.Count || certificates[index].Record == null) return PackValidationState.SignatureInvalid;
            if (verifiedBuild == null) return null;
            return index < certificateStates.Count ? certificateStates[index] : null;
        }
        string CertificateScope(int index)
        {
            var state = CertificateState(index);
            switch (state)
            {
                case PackValidationState.ValidatedForThisBuild: return "Trusted report: load, drive and render checks for this exact build/content/platform.";
                case PackValidationState.VerifiedPartialEvidence: return "Trusted report: partial checks for this exact build/content/platform.";
                case PackValidationState.OtherGameBuild: return "Trusted report applies to another game build.";
                case PackValidationState.OtherPlatform: return "Trusted report applies to another platform.";
                case PackValidationState.OtherSubset: return "Trusted report applies to another supported-content subset.";
                case PackValidationState.UntrustedIssuer: return "Signer not trusted; test claims are not accepted.";
                case PackValidationState.SignatureInvalid: return "Certificate identity or signature is invalid.";
                default: return buildProblem ?? "Exact-build test status unavailable.";
            }
        }

        public bool OpenCircuitRevision(int index)
        {
            if (Busy || disposed || !owner || index < 0 || index >= rows.Count || rows[index].Manifest?.Kind != "circuit") return false;
            owner.GetComponent<DrivingUI>().ShowImportedCircuit(rows[index]); return true;
        }

        // Register readers only after their actual schema contracts have been implemented.
        // These are inspection capabilities; scene activation has its own admission stage.
        static PackEngineSupport Readers(string version)
        {
            // Library inspection and actual circuit loading share implemented
            // schema capabilities. Extra regional readers do not upgrade a
            // circuit's geometry, test certificate or activation status.
            var features = PackCircuitSources.Readers(version).Features.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            features.Add("regional-index", 1);
            features.Add("regional-buildings", 1);
            return new PackEngineSupport(version, features);
        }

        public void Open(GameSession session)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WorldPackMenu));
            owner = session;
            buildSource = PackGameBuildSource.Capture();
            activation = session ? session.GetComponent<RegionalPackActivation>() : null;
            if (root == null)
            {
                // Automated captures keep their pack library beside their isolated profile.
                root = RegionalPackActivation.LibraryRoot();
                engineVersion = Application.version;
            }
            // A scene can open this menu before its saved selection read finishes.
            // Retain the refresh intent instead of leaving an empty inventory forever.
            refreshPending = true; Poll();
        }

        void Start(Func<CancellationToken, Result> action, string message)
        {
            if (disposed || Busy) return;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            status = message;
            work = Task.Run(() => action(token), token);
        }
        void Refresh()
        {
            string path = root;
            Start(token => { token.ThrowIfCancellationRequested(); return new Result {
                Rows = new WorldPackStore(path).Discover(), Message = "Library ready. Select a revision to check its content." }; }, "Reading local pack library…");
        }
        void Import()
        {
            string path = root, source = archivePath.Trim().Trim('"');
            Start(token => {
                var store = new WorldPackStore(path); var installed = store.Install(source, token);
                return new Result { Rows = store.Discover(), Message = installed.Manifest.Name +
                    (installed.AlreadyInstalled ? " is already installed." : " installed. Previous revisions retained.") +
                    " Content hashes checked; gameplay validation and activation are separate." };
            }, "Checking and installing archive…");
        }
        void Inspect(WorldPackInventoryItem row)
        {
            if (row.Manifest == null) return;
            string path = root, version = engineVersion;
            Start(token => {
                var store = new WorldPackStore(path);
                var manifest = store.VerifyInstalled(row.Directory, row.Manifest.ManifestSha256, token);
                var support = Readers(version); var compatible = WorldPackCompatibility.Resolve(manifest, support);
                string result = compatible.Reason + ". " + compatible.Decisions.Count(d => d.Loaded) +
                    "/" + compatible.Decisions.Count + " elements readable.";
                if (compatible.CanActivate && compatible.Decisions.Any(d => d.Loaded && d.Variant.Feature == "regional-index"))
                {
                    var reader = WorldPackRegionalReader.OpenVerified(store,
                        new InstalledWorldPack(row.Directory, manifest, true), support, token);
                    var audit = reader.AuditReferenceWindow(new RegionalDraft.RegionalCollection.Limits(), token);
                    result += " Reference area: " + audit.Selection.Buildings.Count + " supported whole building parts. " + audit.Message + " " +
                        (reader.SourceAuthoringBlocked ? "Source remains an authoring candidate." : "Source is not authoring-only; scene admission and gameplay validation still required.");
                }
                if (compatible.CanActivate && manifest.Kind == "circuit")
                {
                    var reader = WorldPackCircuitReader.OpenVerified(store, new InstalledWorldPack(row.Directory, manifest, true), support, token);
                    result += " Venue: " + reader.Name + " · " + reader.Layouts.Count(l => l.Readable) + "/" + reader.Layouts.Count + " driving layouts readable; physical/race verification is separate.";
                }
                return new Result { Rows = store.Discover(), Message = result + " No scene activation or native-validation claim." };
            }, "Checking revision files and supported content…");
        }
        public void Poll()
        {
            if (disposed) return;
            if (work != null && work.IsCompleted)
            {
                var completed = work; work = null;
                cancellation.Dispose(); cancellation = null;
                if (completed.IsCanceled) status = "Operation cancelled; active scenery unchanged.";
                else if (completed.IsFaulted) status = "Pack operation failed: " + Clean(completed.Exception.GetBaseException().Message);
                else { rows = completed.Result.Rows; status = completed.Result.Message;
                    if (completed.Result.Certificates != null) certificates = completed.Result.Certificates;
                    if (completed.Result.TrustedIssuers != null) trustedIssuers = completed.Result.TrustedIssuers;
                    if (completed.Result.Certificates != null) { verifiedBuild = completed.Result.Build; buildProblem = completed.Result.BuildProblem; certificateStates = completed.Result.CertificateStates ?? Array.Empty<PackValidationState?>(); } }
            }
            if (refreshPending && !OperationBusy) { refreshPending = false; Refresh(); }
        }
        static string Clean(string text) => (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
        void Styles()
        {
            if (heading != null) return;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, richText = false };
            body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = false };
            small = new GUIStyle(body) { fontSize = 13 };
            heading.normal.textColor = body.normal.textColor = Color.white;
            small.normal.textColor = new Color(.65f, .73f, .77f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }
        public void Draw(Action back)
        {
            Poll(); Styles();
            if(controllerBrowser.IsOpen){controllerBrowser.Draw(path=>{if(certificatePack!=null)ImportCertificate(path);else ImportArchive(path);});return;}
            if (certificatePack != null) { DrawCertificates(); return; }
            var old = GUI.color; GUI.color = new Color(.035f, .055f, .07f, .98f);
            GUI.DrawTexture(new Rect(35, 240, 1370, 620), Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(65, 263, 1250, 35), "IMPORTED WORLD & CIRCUIT PACK LIBRARY", heading);
            GUI.Label(new Rect(65, 307, 1260, 48), "Import a local .euworld archive, then choose nearby buildings or a circuit layout. Imports are experimental and offline; each revision is checked before use.", body);
            GUI.Label(new Rect(65, 360, 1250, 24), "Archive file path (Mac or Windows)", small);
            bool enabled = GUI.enabled; GUI.enabled = enabled && !Busy;
            archivePath = ControllerMenuGUI.TextField(new Rect(65, 390, 690, 34), archivePath, 4096);
            if(ControllerMenuGUI.Button(new Rect(775,390,230,34),"CHOOSE PACK…",button))controllerBrowser.Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),".euworld","WORLD/CIRCUIT PACK",root,"BACK TO PACK LIBRARY");
            if (ControllerMenuGUI.Button(new Rect(1025, 390, 300, 34), "IMPORT ARCHIVE", button)) Import();
            GUI.enabled = enabled;
            scroll = ControllerMenuGUI.BeginScrollView(new Rect(65, 445, 1260, 245), scroll, new Rect(0, 0, 1220, Math.Max(245, rows.Count * 100)));
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; float y = i * 100;
                GUI.Label(new Rect(8, y, 660, 50), row.Manifest == null ? "UNAVAILABLE REVISION" : row.Manifest.Name + " · revision " + row.Manifest.Revision + " · " + (row.Manifest.InstalledBytes / 1048576d).ToString("F1") + " MB", body);
                string rowIdentity=row.Manifest==null?"unavailable:"+i:row.Manifest.Id+"/"+row.Manifest.Revision;
                bool selected = activation && activation.Selected.Matches(row.Manifest);
                GUI.Label(new Rect(8, y + 54, 660, 38), row.Manifest == null ? Clean(row.Problem) : (selected ? "SELECTED PREVIEW · " : "") + row.Manifest.Kind + " · " + row.Manifest.Licence + " · not gameplay-certified", small);
                GUI.enabled = enabled && !Busy && row.Manifest != null;
                if (ControllerMenuGUI.Button(new Rect(690, y + 28, 110, 38), "CHECK", button,"pack-check:"+rowIdentity)) Inspect(row);
                GUI.enabled = enabled && !Busy && row.Manifest != null && activation;
                bool circuit = row.Manifest?.Kind == "circuit";
                if (ControllerMenuGUI.Button(new Rect(815, y + 28, 205, 38), circuit ? "CIRCUIT LAYOUTS" : "PREVIEW BUILDINGS", button,"pack-select:"+rowIdentity))
                { if (circuit) OpenCircuitRevision(i); else PreviewRevision(i); }
                GUI.enabled = enabled && !Busy && row.Manifest != null;
                if (ControllerMenuGUI.Button(new Rect(1035, y + 28, 170, 38), "CERTIFICATES", button,"pack-certificates:"+rowIdentity)) ShowCertificates(i);
                GUI.enabled = enabled;
            }
            ControllerMenuGUI.EndScrollView();
            if (rows.Count == 0) GUI.Label(new Rect(88, 470, 1000, 40), "No imported revisions yet.", body);
            GUI.Label(new Rect(65, 710, 1250, 78), status + (activation ? "\n" + activation.Status : ""), body);
            if (ControllerMenuGUI.Button(new Rect(65, 795, 220, 40), "BACK", button)) back();
            GUI.enabled = enabled && !Busy;
            if (ControllerMenuGUI.Button(new Rect(310, 795, 220, 40), "REFRESH LIBRARY", button)) Refresh();
            GUI.enabled = enabled && Busy;
            if (ControllerMenuGUI.Button(new Rect(555, 795, 220, 40), "CANCEL", button)) { cancellation?.Cancel(); activation?.CancelPreference(); }
            GUI.enabled = enabled && !Busy && activation;
            if (ControllerMenuGUI.Button(new Rect(800, 795, 350, 40), "USE DEFAULT SCENERY", button)) DisablePreview();
            GUI.enabled = enabled;
        }
        void DrawCertificates()
        {
            if (pendingIssuer != null) { DrawIssuerConfirmation(); return; }
            var old = GUI.color; GUI.color = new Color(.035f, .055f, .07f, .98f);
            GUI.DrawTexture(new Rect(35, 240, 1370, 620), Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(65, 263, 1250, 35), "PACK VALIDATION CERTIFICATES", heading);
            GUI.Label(new Rect(65, 305, 1260, 50), certificatePack.Manifest.Name + " · revision " + certificatePack.Manifest.Revision +
                "\nSigned test claims are separate from compatibility, accuracy and permission to distribute.", body);
            GUI.Label(new Rect(65, 361, 1250, 24), "Local .euvalidation certificate file path (does not grant signer trust)", small);
            bool enabled = GUI.enabled; GUI.enabled = enabled && !Busy;
            certificatePath = ControllerMenuGUI.TextField(new Rect(65, 390, 690, 34), certificatePath, 4096);
            if(ControllerMenuGUI.Button(new Rect(775,390,230,34),"CHOOSE FILE…",button))controllerBrowser.Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),".json","CERTIFICATE",root,"BACK TO CERTIFICATES");
            if (ControllerMenuGUI.Button(new Rect(1025, 390, 300, 34), "IMPORT CERTIFICATE", button)) ImportCertificate(certificatePath);
            GUI.enabled = enabled;
            certificateScroll = ControllerMenuGUI.BeginScrollView(new Rect(65, 443, 1260, 250), certificateScroll,
                new Rect(0, 0, 1220, Math.Max(250, certificates.Count * 190)));
            for (int i = 0; i < certificates.Count; i++)
            {
                var item = certificates[i]; float y = i * 190;
                if (item.Record == null)
                { GUI.Label(new Rect(8, y, 1200, 145), "UNAVAILABLE CERTIFICATE\n" + Clean(item.Problem), body); continue; }
                var claim = item.Record;
                GUI.Label(new Rect(8, y, 1200, 28), "SIGNATURE VERIFIED · " + (trustedIssuers.Contains(claim.IssuerFingerprint) ? "TRUSTED BY YOU" : "SIGNER NOT TRUSTED"), body);
                GUI.Label(new Rect(8, y + 30, 1200, 118), "Signer SHA256: " + claim.IssuerFingerprint +
                    "\nTested engine: " + claim.EngineVersion + " · platform: " + claim.Platform + " · issued: " + claim.IssuedUtc +
                    "\nClaimed checks: " + string.Join(", ", claim.Checks) +
                    "\nTested build SHA256: " + claim.GameBuildSha256 +
                    "\n" + CertificateScope(i), small);
                GUI.enabled = enabled && !Busy;
                bool trusted = trustedIssuers.Contains(claim.IssuerFingerprint);
                if (ControllerMenuGUI.Button(new Rect(8, y + 148, 330, 32), trusted ? "REVOKE SIGNER TRUST" : "REVIEW SIGNER TRUST", button,"pack-certificate:"+item.RecordSha256))
                { if (trusted) RevokeIssuerTrust(i); else RequestIssuerTrust(i); }
                GUI.enabled = enabled;
            }
            if (certificates.Count == 0) GUI.Label(new Rect(8, 8, 1200, 80), "No certificate records for this revision.\nThis pack is unsigned; compatibility checks and normal preview options remain separate.", body);
            ControllerMenuGUI.EndScrollView();
            GUI.Label(new Rect(65, 710, 1250, 78), status, body);
            // Closing the view is always available, including after a failed or cancelled import.
            if (ControllerMenuGUI.Button(new Rect(65, 795, 270, 40), "BACK TO PACK LIBRARY", button))
            CloseCertificates();
            GUI.enabled = enabled && !Busy;
            if (ControllerMenuGUI.Button(new Rect(360, 795, 270, 40), "REFRESH CERTIFICATES", button)) ReadCertificates(null);
            GUI.enabled = enabled && Busy;
            if (ControllerMenuGUI.Button(new Rect(655, 795, 220, 40), "CANCEL", button)) cancellation?.Cancel();
            GUI.enabled = enabled;
        }
        void DrawIssuerConfirmation()
        {
            var old = GUI.color; GUI.color = new Color(.035f, .055f, .07f, .98f);
            GUI.DrawTexture(new Rect(35, 240, 1370, 620), Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(65, 263, 1250, 35), "TRUST THIS TEST-REPORT SIGNER?", heading);
            GUI.Label(new Rect(65, 330, 1250, 65), "Public-key fingerprint (SHA256):\n" + pendingIssuer, body);
            GUI.Label(new Rect(65, 420, 1230, 180), "Compare this fingerprint with a publisher or reviewer you trust, using a separate trusted channel. " +
                "A valid signature proves which key signed a claim; it does not identify a person on its own.\n\n" +
                "This choice applies to this signer across your local pack library. It does not approve every pack, prove its accuracy, grant redistribution rights or replace exact-build test checks. You can revoke trust here later.", body);
            if (ControllerMenuGUI.Button(new Rect(65, 795, 330, 40), "CANCEL · KEEP UNTRUSTED", button)) CancelIssuerTrust();
            bool enabled = GUI.enabled; GUI.enabled = enabled && !Busy;
            if (ControllerMenuGUI.Button(new Rect(420, 795, 330, 40), "TRUST THIS SIGNER LOCALLY", button)) ConfirmIssuerTrust();
            GUI.enabled = enabled;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            var source = cancellation; cancellation = null;
            if (source == null) return;
            source.Cancel();
            // Observe a fault even when the scene closes before the worker returns.
            work.ContinueWith(done => { if (done.IsFaulted) _ = done.Exception; source.Dispose(); }, TaskScheduler.Default);
        }
    }
}
