using System;
using System.Collections.Generic;

namespace EarthUnscripted.Core
{
    /// <summary>Validated imported physics values. Distances are metres, mass kg,
    /// spring N/m, damping N·s/m. Power/grip are the game's existing tuning factors.</summary>
    public sealed class ImportedVehicleSettings
    {
        public string Id, Name, Version;
        public int Revision, Price;
        public float Mass, Power, Speed, Grip, OffroadGrip, FrontDriveFraction;
        public float BodyWidth, Track, Wheelbase, WheelRadius, TyreWidth;
        public float AnchorY, RayLength, Travel, SpringRate, DamperRate;
        public float ColliderWidth, ColliderLength, ColliderY, ColliderHeight, RoofHeight;
        public float CentreOfMassY, CockpitX, CockpitY, CockpitZ;
        public float StructuralStrength=1, SafeLandingSpeed=5.5f, WadingDepth=.35f;
        public bool WheelObstacleContact;
        // Derived from painted materials actually used by admitted geometry.
        public bool SupportsPaint=true;
        public string EngineLayout="standard",TyreSoundProfile="auto";
    }

    public sealed class ImportedVehicleEntry
    {
        public readonly int Model;
        public readonly ImportedVehicleSettings Settings;
        public readonly VehicleMetric Metric;
        public ImportedVehicleEntry(int model, ImportedVehicleSettings settings)
        {
            Model = model;
            Settings = settings;
            Metric = new VehicleMetric(model, settings);
        }
    }

    /// <summary>Append-only process catalogue. Built-in model indexes 0–3 stay fixed.
    /// Saved imported cars use a stable publisher-qualified ID; their numeric index
    /// is resolved on each load, so installation order never changes ownership.
    /// Missing packs retain their saved car/condition, while another available car
    /// is selected. Updating an existing pack takes effect at the next app start.</summary>
    public static class ImportedVehicleCatalogue
    {
        public const int BuiltInCount = 4;
        public const int MaximumImportedCars = 64;
        static readonly List<ImportedVehicleEntry> entries = new List<ImportedVehicleEntry>();
        static readonly Dictionary<string, ImportedVehicleEntry> byId = new Dictionary<string, ImportedVehicleEntry>(StringComparer.Ordinal);
        public static IReadOnlyList<ImportedVehicleEntry> Entries => entries;

        public static ImportedVehicleEntry Register(ImportedVehicleSettings settings)
        {
            if (byId.TryGetValue(settings.Id, out var existing)) return existing;
            if (entries.Count >= MaximumImportedCars) throw new InvalidOperationException("The vehicle library is full (64 imported cars).");
            int model = GarageRules.Catalogue.Length;
            var next = new CarSpec[model + 1];
            Array.Copy(GarageRules.Catalogue, next, model);
            next[model] = new CarSpec(settings.Name, settings.Price, settings.Mass, settings.Power,
                settings.Speed, settings.Grip, 1, 1);
            var entry = new ImportedVehicleEntry(model, settings);
            entries.Add(entry);
            byId.Add(settings.Id, entry);
            GarageRules.Catalogue = next;
            return entry;
        }

        public static ImportedVehicleEntry Get(int model)
        {
            int index = model - BuiltInCount;
            if (index < 0 || index >= entries.Count) throw new ArgumentOutOfRangeException(nameof(model));
            return entries[index];
        }
        public static ImportedVehicleEntry Find(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var entry) ? entry : null;
        public static string IdForModel(int model) => model < BuiltInCount ? "" : Get(model).Settings.Id;
        public static bool Available(OwnedCar car) => string.IsNullOrEmpty(car.packVehicleId) || Find(car.packVehicleId) != null;
        public static string DisplayName(OwnedCar car) => Available(car) ? GarageRules.Catalogue[car.model].Name : car.packVehicleId + " · PACK MISSING";
        public static void ResolveSavedCar(OwnedCar car)
        {
            if (!string.IsNullOrEmpty(car.packVehicleId)) car.model = Find(car.packVehicleId)?.Model ?? 0;
            else if (car.model < 0 || car.model >= BuiltInCount) car.model = 0;
        }
    }
}
