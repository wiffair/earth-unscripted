using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EarthUnscripted.Packs;
using UnityEngine;

namespace EarthUnscripted
{
    /// <summary>Local venue/layout chooser. Verification and owned source decoding run
    /// on one cancellable worker; only texture creation and the normal scene request use Unity's main thread.</summary>
    public sealed class CircuitPackMenu : IDisposable
    {
        sealed class Result
        {
            public WorldPackCircuitReader Catalogue; public WorldPackManifest Manifest;
            public GeoRegion Preview; public PackCircuitContext Context;
        }
        GameSession session;
        WorldPackCircuitReader catalogue; WorldPackManifest manifest; PackCircuitContext context;
        GeoRegion preview; Texture2D map;
        string root, version, status = "Reading circuit pack…";
        Task<Result> work; CancellationTokenSource cancellation;
        bool disposed; Vector2 scroll;
        int mode = 1, lapChoice, difficulty = 1;
        GUIStyle heading, body, small, button;
        public bool Busy => work != null;
        public string Status => status;
        public string SelectedLayout => context?.LayoutId;
        public PackCircuitContext SelectedCircuit => context;
        public int LayoutCount => catalogue?.Layouts.Count ?? 0;
        public int EventCount => catalogue?.Events.Count ?? 0;
        public string SelectedEvent => context?.Event?.Event.Id;
        public void Open(GameSession owner, WorldPackInventoryItem row)
        {
            if (disposed || session || owner == null || row?.Manifest == null) throw new InvalidOperationException("Circuit menu needs a new readable revision");
            session = owner; root = RegionalPackActivation.LibraryRoot(); version = Application.version;
            var pin = WorldPackSelection.For(row.Manifest, false); string path = root, engine = version;
            Start(token => {
                var store = new WorldPackStore(path); var installed = store.Locate(pin);
                return new Result { Catalogue = WorldPackCircuitReader.OpenVerified(store, installed, PackCircuitSources.Readers(engine), token), Manifest = installed.Manifest };
            }, "Checking venue and supported layouts…");
        }
        void Start(Func<CancellationToken, Result> action, string message)
        {
            if (disposed || Busy) throw new InvalidOperationException("Circuit operation already active");
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            status = message; work = Task.Run(() => action(token), token);
        }
        public void SelectLayout(string id)
        {
            if (disposed || Busy || catalogue == null) return;
            var layout = catalogue.Layout(id);
            if (!layout.Readable) { status = "Layout unavailable: " + layout.UnavailableReason; return; }
            var reference = WorldPackCircuitReference.For(manifest, layout); string path = root, engine = version;
            Start(token => {
                reference.Store(path, token);
                var source = new PackCircuitSources.Captured(path, engine, reference.Token);
                var owned = WorldSnapshotCodec.DecodeBootSource(source.Read(token), out _);
                source.BindOwned(owned, token);
                return new Result { Preview = owned, Context = source.Context };
            }, "Checking the selected driving layout…");
        }
        public void SelectEvent(string id)
        {
            if(disposed||Busy||catalogue==null)return;
            var item=catalogue.Event(id);
            if(!item.Readable){status="Event unavailable: "+item.UnavailableReason;return;}
            var layout=catalogue.Layout(item.LayoutId);var reference=WorldPackCircuitReference.ForEvent(manifest,layout,item);
            string path=root,engine=version;
            Start(token=>{
                // Verify typed rules/event values before writing a launch pin. The
                // normal boot repeats verification and never trusts this preview.
                catalogue.ReadEvent(id,token);reference.Store(path,token);
                var source=new PackCircuitSources.Captured(path,engine,reference.Token);
                var owned=WorldSnapshotCodec.DecodeBootSource(source.Read(token),out _);source.BindOwned(owned,token);
                return new Result{Preview=owned,Context=source.Context};
            },"Checking event, driving layout and racing rules…");
        }
        public bool StartSession(int requestedMode, int laps, int aiDifficulty)
        {
            if (disposed || Busy || context == null || !session) return false;
            bool changed = session.SelectImportedCircuit(context, requestedMode, laps, aiDifficulty);
            if (!changed) status = "Circuit not started; current drive and career retained";
            return changed;
        }
        public void Poll()
        {
            if (disposed || work == null || !work.IsCompleted) return;
            var done = work; work = null; cancellation.Dispose(); cancellation = null;
            if (done.IsCanceled) { status = "Circuit operation cancelled; current drive retained"; return; }
            if (done.IsFaulted)
            {
                var error=done.Exception.Flatten().InnerExceptions[0];
                status=CircuitSceneryPlan.IsCapacityFailure(error)
                    ? "Circuit scenery exceeds the supported detail limit. Choose a reduced-detail revision; your current drive is unchanged."
                    : "Circuit unavailable: " + error.Message;
                return;
            }
            var result = done.Result;
            if (result.Catalogue != null)
            {
                catalogue = result.Catalogue; manifest = result.Manifest;
                var first = catalogue.Layouts.FirstOrDefault(l => l.Readable);
                status = first == null ? "No supported driving layouts in this revision" : "Venue ready";
                if (first != null) SelectLayout(first.Id);
            }
            else
            {
                // Publish the selection only after its thumbnail succeeds. A failed
                // new preview must not pair an old context with a newly highlighted name.
                Texture2D next = null;
                try
                {
                    next = RegionMapTexture.Create(result.Preview);
                    if (map) UnityEngine.Object.Destroy(map); map = next; preview = result.Preview; context = result.Context;
                    var preset=context.Event;
                    if(preset!=null){mode=preset.Mode;difficulty=preset.Difficulty;}
                    status = (preset==null?"Layout ready · choose racing mode":preset.Event.Name+" · "+preset.Rules.Name+" · "+preset.Laps+" lap(s)")+" · experimental offline import"+(map?"":" · map preview omitted (drawing budget)");
                }
                catch (Exception error)
                { if (next) UnityEngine.Object.Destroy(next); status = "Previous layout retained: preview unavailable · " + error.Message; }
            }
        }
        void Styles()
        {
            if (heading != null) return;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, richText = false };
            body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = false };
            small = new GUIStyle(body) { fontSize = 13 }; button = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = false };
            heading.normal.textColor = body.normal.textColor = Color.white; small.normal.textColor = new Color(.65f, .73f, .77f);
        }
        public void Draw(Action back)
        {
            Poll(); Styles(); var old = GUI.color; GUI.color = new Color(.035f, .055f, .07f, .98f);
            GUI.DrawTexture(new Rect(35, 240, 1370, 620), Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(65, 263, 1250, 36), catalogue?.Name ?? "IMPORTED CIRCUIT", heading);
            GUI.Label(new Rect(65, 307, 1250, 46), "Choose a layout from this venue. The active car, setup and existing pit service carry into the normal circuit session.", body);
            scroll = ControllerMenuGUI.BeginScrollView(new Rect(65, 370, 650, 160), scroll, new Rect(0, 0, 620, Math.Max(160, (LayoutCount+EventCount) * 55)));
            int row=0;
            if (catalogue != null) for (int i = 0; i < catalogue.Layouts.Count; i++)
            {
                var layout = catalogue.Layouts[i]; bool enabled = GUI.enabled;
                GUI.enabled = enabled && !Busy && layout.Readable;
                if (ControllerMenuGUI.Button(new Rect(0, row++ * 55, 605, 46), (SelectedLayout == layout.Id && SelectedEvent==null ? "SELECTED · " : "") + layout.Name + (layout.Readable ? "" : " · UNSUPPORTED"), button)) SelectLayout(layout.Id);
                GUI.enabled = enabled;
                foreach(var item in catalogue.Events.Where(e=>e.LayoutId==layout.Id))
                {
                    GUI.enabled=enabled&&!Busy&&item.Readable;
                    if(ControllerMenuGUI.Button(new Rect(20,row++*55,585,46),(SelectedEvent==item.Id?"SELECTED · ":"EVENT · ")+item.Name+(item.Readable?"":" · UNSUPPORTED"),button))SelectEvent(item.Id);
                    GUI.enabled=enabled;
                }
            }
            ControllerMenuGUI.EndScrollView();
            bool previous = GUI.enabled; GUI.enabled = previous && !Busy && context?.Event==null;
            mode = ControllerMenuGUI.Toolbar(new Rect(65, 550, 650, 42), mode, new[] { "PRACTICE", "TIME TRIAL", "RACE", "SPEED TRIAL" });
            GUI.Label(new Rect(65, 615, 160, 30), context?.Event==null?"Race laps":"Event: "+context.Event.Laps+" laps", body);
            if(context?.Event==null)lapChoice = ControllerMenuGUI.Toolbar(new Rect(235, 610, 480, 36), lapChoice, new[] { "1", "3", "5" });
            else GUI.Label(new Rect(235,610,480,36),context.Event.Laps+" · specified by event",body);
            GUI.Label(new Rect(65, 665, 160, 30), "AI difficulty", body);
            difficulty = ControllerMenuGUI.Toolbar(new Rect(235, 660, 480, 36), difficulty, new[] { "EASY", "NORMAL", "HARD" }); GUI.enabled = previous;
            if (map) GUI.DrawTexture(new Rect(845, 365, 420, 290), map, ScaleMode.ScaleToFit);
            if (preview != null) GUI.Label(new Rect(785, 665, 540, 55), (preview.referenceLength / 1000f).ToString("F3") + " km reference · owned car\n" + Core.GarageRules.Catalogue[session.Profile.garage.Active.model].Name, body);
            GUI.Label(new Rect(65, 715, 1260, 68), status, body);
            GUI.enabled = previous && !Busy && context != null && !session.Network.Active;
            if (ControllerMenuGUI.Button(new Rect(310, 800, 310, 40), "START IMPORTED SESSION", button)) StartSession(mode, new[] { 1, 3, 5 }[lapChoice], difficulty);
            GUI.enabled = previous;
            if (ControllerMenuGUI.Button(new Rect(65, 800, 220, 40), "BACK TO PACKS", button)) back();
            GUI.enabled = previous && Busy;
            if (ControllerMenuGUI.Button(new Rect(645, 800, 220, 40), "CANCEL CHECK", button)) cancellation?.Cancel(); GUI.enabled = previous;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; if (map) UnityEngine.Object.Destroy(map);
            var source = cancellation; cancellation = null; if (source == null) return;
            source.Cancel(); var pending = work; work = null;
            if (pending == null) source.Dispose();
            else pending.ContinueWith(done => { if (done.IsFaulted) _ = done.Exception; source.Dispose(); }, TaskScheduler.Default);
        }
    }
}
