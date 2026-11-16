using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadRush.Core;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Beta-only local vehicle library. A verified archive is copied to a
    /// same-directory temporary file and atomically replaces its previous revision.
    /// In-use definitions stay immutable until restart; there is no mid-drive swap.</summary>
    public static class VehiclePackLibrary
    {
        static readonly Dictionary<int, VehiclePack> active = new Dictionary<int, VehiclePack>();
        static readonly List<string> notices = new List<string>();
        public static IReadOnlyList<string> Notices => notices;
        public static string Folder { get; private set; }
        public static string IncludedFolder { get; private set; }
        public static VehiclePack ForModel(int model) => active.TryGetValue(model, out var pack) ? pack : null;

        public static void Initialize(string dataRoot, string includedFolder)
        {
            Folder = Path.Combine(dataRoot, "VehiclePacks");
            IncludedFolder = includedFolder;
            Directory.CreateDirectory(Folder);
            var choices = new Dictionary<string, VehiclePack>(StringComparer.Ordinal);
            foreach (string root in new[] { includedFolder, Folder })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.GetFiles(root, "*.rrcar").OrderBy(p => p, StringComparer.Ordinal).Take(ImportedVehicleCatalogue.MaximumImportedCars + 1))
                {
                    try
                    {
                        var pack = VehiclePackFormat.Read(path);
                        if (!choices.TryGetValue(pack.Settings.Id, out var previous) || pack.Settings.Revision > previous.Settings.Revision)
                            choices[pack.Settings.Id] = pack;
                        else if (pack.Settings.Revision == previous.Settings.Revision && pack.ArchiveHash != previous.ArchiveHash)
                            notices.Add(pack.Settings.Name + ": conflicting bytes share the same revision; retained the first verified pack.");
                    }
                    catch (Exception error) { notices.Add(Path.GetFileName(path) + ": " + error.Message); }
                }
            }
            foreach (var pack in choices.Values.OrderBy(p => p.Settings.Id, StringComparer.Ordinal))
            {
                try { Activate(pack); }
                catch (Exception error) { notices.Add(pack.Settings.Name + ": " + error.Message); }
            }
        }
        public static void Activate(VehiclePack pack)
        {
            if (ImportedVehicleCatalogue.Find(pack.Settings.Id) != null) return;
            var entry = ImportedVehicleCatalogue.Register(pack.Settings);
            active.Add(entry.Model, pack);
        }
        /// <summary>Worker-only validation/install. Does not change the live catalogue.
        /// Returns the verified exact installed bytes, for activation on the UI thread.</summary>
        public static VehiclePack Import(string source, string folder)
        {
            string fullPath = Path.GetFullPath(source.Trim().Trim('"'));
            if (!string.Equals(Path.GetExtension(fullPath), ".rrcar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a .rrcar vehicle pack.");
            if (new FileInfo(fullPath).Length > VehiclePackFormat.MaximumArchiveBytes) throw new InvalidDataException("Pack must be at most 32 MB.");
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, ".import-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Copy first: validation and admission refer to exactly the same bytes.
                using (var input = File.OpenRead(fullPath))
                using (var output = File.Create(staging))
                {
                    var buffer = new byte[16384]; int count, total = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > VehiclePackFormat.MaximumArchiveBytes) throw new InvalidDataException("Pack grew beyond the 32 MB limit.");
                        output.Write(buffer, 0, count);
                    }
                    output.Flush(true);
                }
                var pack = VehiclePackFormat.Read(staging);
                string target = Path.Combine(folder, pack.Settings.Id + ".rrcar");
                var running = ImportedVehicleCatalogue.Find(pack.Settings.Id);
                VehiclePack previous = File.Exists(target) ? VehiclePackFormat.Read(target) : null;
                int previousRevision = Math.Max(previous?.Settings.Revision ?? 0, running?.Settings.Revision ?? 0);
                if (pack.Settings.Revision < previousRevision) throw new InvalidDataException("An older revision cannot replace the installed pack.");
                if (pack.Settings.Revision == previousRevision)
                {
                    string oldHash = previous?.ArchiveHash ?? ForModel(running.Model).ArchiveHash;
                    if (pack.ArchiveHash != oldHash) throw new InvalidDataException("This revision has different contents. Increase the pack version and revision.");
                    return pack;
                }
                string previousVersion = previous?.Settings.Version ?? running?.Settings.Version;
                if (previousVersion != null && new Version(pack.Settings.Version).CompareTo(new Version(previousVersion)) <= 0)
                    throw new InvalidDataException("An update must increase both its version and revision.");
                if (File.Exists(target)) File.Replace(staging, target, target + ".previous");
                else File.Move(staging, target);
                return pack;
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}
