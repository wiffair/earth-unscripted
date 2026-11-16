using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using RoadRush.Core;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Paused local car-pack import UI. Archive validation and native file
    /// selection run on workers; activation and all vehicle changes run on the UI thread.</summary>
    public sealed class VehiclePackMenu : IDisposable
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("RoadRushCarPicker")] static extern IntPtr RoadRushChooseCarPack();
        [DllImport("RoadRushCarPicker")] static extern void RoadRushFreeCarPickerPath(IntPtr path);
#endif
        string archivePath = "", status = "Choose a .rrcar pack, or select an installed car below.";
        Task<VehiclePack> import;
        Task<string> picker;
        Process pickerProcess;
        readonly object pickerLock = new object();
        Vector2 scroll;
        GUIStyle heading, body, small, button;
        bool disposed;
        public void ImportArchive(string path)
        {
            if (import != null || picker != null || disposed) return;
            archivePath = path;
            string folder = VehiclePackLibrary.Folder;
            import = Task.Run(() => VehiclePackLibrary.Import(path, folder));
            status = "Checking the car pack…";
        }
        public void Poll()
        {
            if (picker != null && picker.IsCompleted)
            {
                var completed = picker; picker = null;
                if (completed.IsFaulted) status = "Could not open the file chooser. Paste the pack path below or use the car-pack folder.";
                else if (!string.IsNullOrWhiteSpace(completed.Result)) ImportArchive(completed.Result.Trim());
                else status = "File selection cancelled.";
            }
            if (import == null || !import.IsCompleted) return;
            var finished = import; import = null;
            if (finished.IsFaulted) { status = "Pack not imported: " + finished.Exception.GetBaseException().Message; return; }
            var pack = finished.Result;
            var running = ImportedVehicleCatalogue.Find(pack.Settings.Id);
            if (running == null)
            {
                VehiclePackLibrary.Activate(pack);
                status = pack.Settings.Name + " " + pack.Settings.Version + " imported. Select it below when stopped in free drive.";
            }
            else status = running.Settings.Revision == pack.Settings.Revision ? "This pack version is already available." : pack.Settings.Name + " " + pack.Settings.Version + " installed. Restart the beta to use the update; your car and upgrades are retained.";
        }
        void Browse()
        {
            if (picker != null || import != null) return;
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            IntPtr chosen = IntPtr.Zero;
            try
            {
                chosen = RoadRushChooseCarPack();
                if (chosen == IntPtr.Zero) { status = "File selection cancelled."; return; }
                string selected = Marshal.PtrToStringUTF8(chosen);
                ImportArchive(selected);
            }
            catch (Exception error) { status = "File chooser unavailable: " + error.Message + ". You can also paste the pack path below."; }
            finally { if (chosen != IntPtr.Zero) RoadRushFreeCarPickerPath(chosen); }
            return;
#else
            picker = Task.Run(() =>
            {
                const string executable = "powershell.exe";
                const string arguments = "-NoProfile -STA -Command \"Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.OpenFileDialog; $d.Filter='Earth Unscripted car packs (*.rrcar)|*.rrcar'; if($d.ShowDialog() -eq 'OK'){[Console]::Write($d.FileName)}\"";
                using (var process = new Process { StartInfo = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } })
                {
                    lock (pickerLock) { if (disposed) return ""; process.Start(); pickerProcess = process; }
                    try
                    {
                        if (!process.WaitForExit(300000)) { process.Kill(); throw new IOException("File chooser timed out."); }
                        if (process.ExitCode != 0) throw new IOException("File chooser was unavailable.");
                        return process.StandardOutput.ReadToEnd();
                    }
                    finally { lock (pickerLock) pickerProcess = null; }
                }
            });
#endif
        }
        readonly ControllerPackBrowser controllerBrowser = new ControllerPackBrowser();
        public string ControllerPage => controllerBrowser.IsOpen ? "files-" + controllerBrowser.Page : "library";
        public void Draw(GameSession session, Action back)
        {
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = false };
                small = new GUIStyle(body) { fontSize = 13 };
                button = new GUIStyle(GUI.skin.button) { fontSize = 17 };
                heading.normal.textColor = body.normal.textColor = small.normal.textColor = Color.white;
            }
            if (controllerBrowser.IsOpen) { controllerBrowser.Draw(path => { archivePath = path; ImportArchive(path); }); return; }
            GUI.Box(new Rect(35, 240, 1370, 620), GUIContent.none);
            GUI.Label(new Rect(65, 260, 1250, 35), "CAR PACKS · IMPORT & SELECT", heading);
            GUI.Label(new Rect(65, 305, 1250, 48), "Add cars alongside the built-in collection. Packs have their own versions; updates keep your owned car and upgrades.", body);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && import == null && picker == null;
            if (ControllerMenuGUI.Button(new Rect(65, 365, 250, 38), "CHOOSE CAR PACK…", button))
            {
                if (ControllerMenuGUI.Active?.ControllerActive == true)
                    controllerBrowser.Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                else Browse();
            }
            if (ControllerMenuGUI.Button(new Rect(335, 365, 250, 38), "OPEN PACKS FOLDER", button))
            {
                if (ControllerMenuGUI.Active?.ControllerActive == true) controllerBrowser.Open(VehiclePackLibrary.Folder);
                else Application.OpenURL(new Uri(VehiclePackLibrary.Folder).AbsoluteUri);
            }
            archivePath = ControllerMenuGUI.TextField(new Rect(65, 420, 1050, 34), archivePath, 4096);
            if (ControllerMenuGUI.Button(new Rect(1135, 420, 205, 34), "IMPORT PATH", button)) ImportArchive(archivePath);
            GUI.enabled = oldEnabled;
            scroll = ControllerMenuGUI.BeginScrollView(new Rect(65, 475, 1275, 230), scroll, new Rect(0, 0, 1245, Math.Max(230, ImportedVehicleCatalogue.Entries.Count * 110)));
            int index = 0;
            foreach (var entry in ImportedVehicleCatalogue.Entries)
            {
                var pack = VehiclePackLibrary.ForModel(entry.Model);
                float y = index++ * 110;
                GUI.Label(new Rect(10, y, 900, 30), entry.Settings.Name + (entry.Settings.Price==0?" · FREE":" · "+entry.Settings.Price.ToString("N0")+" CR") + " · v" + entry.Settings.Version + " · revision " + entry.Settings.Revision, body);
                GUI.Label(new Rect(10, y + 32, 900, 60), pack.Description + "\n" + pack.Author + " · " + pack.Licence + " · " + (entry.Settings.FrontDriveFraction > 0 && entry.Settings.FrontDriveFraction < 1 ? "4WD" : "2WD"), small);
                bool owned = session.Profile.garage.cars.Exists(c => c.packVehicleId == entry.Settings.Id);
                GUI.enabled = oldEnabled && import == null && picker == null;
                string label = owned ? "SELECT OWNED CAR" : entry.Settings.Price == 0 ? "ADD & SELECT · FREE" : "BUY & SELECT · " + entry.Settings.Price.ToString("N0") + " CR";
                if (ControllerMenuGUI.Button(new Rect(945, y + 15, 285, 40), label, button)) session.SelectPackVehicle(entry.Model);
                GUI.enabled = oldEnabled;
            }
            ControllerMenuGUI.EndScrollView();
            GUI.Label(new Rect(65, 715, 1275, 65), status + (VehiclePackLibrary.Notices.Count > 0 ? "\nLibrary notice: " + VehiclePackLibrary.Notices[0] : ""), small);
            if (ControllerMenuGUI.Button(new Rect(65, 800, 175, 37), "BACK", button)) back();
            var roadCar = session.Profile.garage.cars.Find(c => string.IsNullOrEmpty(c.packVehicleId) && c.model < ImportedVehicleCatalogue.BuiltInCount);
            if (roadCar != null && ControllerMenuGUI.Button(new Rect(265, 800, 300, 37), "USE OWNED ROAD CAR", button)) session.SelectPackVehicle(roadCar.model);
            GUI.Label(new Rect(595, 796, 730, 50), "Swap while stopped in free drive; Harbor Auto is not required. All owned cars are also in Cars & Garage → My Garage.", small);
        }
        public void Dispose()
        {
            lock (pickerLock)
            {
                disposed = true;
                if (pickerProcess != null && !pickerProcess.HasExited) pickerProcess.Kill();
            }
        }
    }
}
