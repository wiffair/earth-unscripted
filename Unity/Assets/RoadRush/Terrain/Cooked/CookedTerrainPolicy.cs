namespace RoadRush.TerrainDraft.Cooked
{
    /// <summary>
    /// Serialization and geometric-validation limits, not vehicle tuning.
    /// Coordinates are metres in the independently bound source frame: Y is up;
    /// X/Z are horizontal. The reader never projects, snaps or repairs mesh data.
    /// </summary>
    internal static class CookedTerrainPolicy
    {
        internal const string PayloadMagic = "RoadRushCookedTerrain1";
        internal const string PackageMagic = "RoadRushCookedTerrainPackage1";
        internal const int FormatVersion = 1;
        // The source-format package is small; mesh payloads have a separate cap.
        internal const int MaximumPackageBytes = 16 * 1024;
        internal const int MaximumPayloadBytes = 128 * 1024 * 1024;
        internal const int ReadBlockBytes = 64 * 1024;
        internal const int MaximumStringUtf8Bytes = 4096;
        // Windows/chunks remain local, avoiding unbounded global-coordinate math.
        internal const double MaximumCoordinateMagnitudeMetres = 10_000_000;
        internal const double MaximumHeightMagnitudeMetres = 100_000;
        internal const double MinimumCellSideMetres = 1;
        internal const double MaximumCellSideMetres = 320;
        internal const double MaximumChunkSideMetres = 3200;
        internal const int MaximumQuantizationExponent = 20;
        // With a <=320m cell and exponent<=20, each integer coordinate is at most
        // 335,544,320. Two orientation products fit Int64 (<2.26e17). Every area
        // accumulation is checked: malformed overlapping geometry cannot overflow.
        internal const int MaximumCellVertices = 250_000;
        internal const int MaximumCellTriangles = 500_000;
        internal const int MaximumCellEmbeddingReferences = 4_000_000;
        internal const int MaximumCellEmbeddingUniquePairs = 4_000_000;
        internal const int MaximumCellEmbeddingPairVisits = 16_000_000;
        internal const long AggregateEmbeddingPairBudget = 256_000_000;
        // Grade is rise/run, NOT degrees. These defaults preserve the reviewed
        // ground recipe: roads 18%, ordinary terrain 120%, classified bank 200%,
        // with the same 1ppm numerical allowance as its original verifier.
        internal const double DefaultRoadGradeLimit = 0.180001;
        internal const double DefaultTerrainGradeLimit = 1.200001;
        internal const double DefaultBankGradeLimit = 2.000001;
        internal const double MaximumDeclaredGradeLimit = 10;
        // Comparisons below check re-derived metadata, never permit a hole or
        // delete a face. Final XYZ/index bytes are independently hashed exactly.
        internal const double BankAreaAgreementSquareMetres = 1e-8;
        internal const double BankGradeAgreementRatio = 1e-8;
        internal const double SourceLateralUnitTolerance = 0.001;
        internal const int CancellationCheckInterval = 128;
    }
}
