namespace EarthUnscripted.FunnyAnimals.Semantics800Proposal
{
    /// <summary>
    /// Source-kind classification, not species suitability. Enumeration values
    /// are internal proposal data and are not persisted or sent over the network.
    /// </summary>
    public enum AreaKind800
    {
        Unknown, WaterHazard, GrassAgriculture, Woodland, Urban, OtherKnownDry
    }

    /// <summary>
    /// Exact source-tag policy independent of the land-cover raster. The reviewed
    /// converter maps water polygons to raster value1, so raster-only checks miss
    /// them. Known dry kinds remain distinct facts; unsupported kinds stay unbound.
    /// </summary>
    public static class AreaTaxonomy800
    {
        /// <summary>
        /// Classifies the original case-sensitive tag without trimming, guessing
        /// synonyms or changing geometry. Null/unrecognised strings return Unknown.
        /// This pure operation is safe on a managed preparation worker.
        /// </summary>
        public static AreaKind800 Classify(string sourceKind)
        {
            switch (sourceKind)
            {
                case "water":
                case "wetland":
                case "reservoir":
                case "basin":
                case "riverbank":
                case "pond":
                case "swimming_pool":
                    return AreaKind800.WaterHazard;
                case "grass":
                case "grassland":
                case "meadow":
                case "farmland":
                case "pasture":
                case "orchard":
                    return AreaKind800.GrassAgriculture;
                case "forest":
                case "wood":
                    return AreaKind800.Woodland;
                case "residential":
                case "industrial":
                case "commercial":
                case "retail":
                case "construction":
                case "brownfield":
                    return AreaKind800.Urban;
                case "scrub":
                case "heath":
                case "fell":
                case "bare_rock":
                case "sand":
                case "beach":
                case "garden":
                case "allotments":
                case "recreation_ground":
                case "village_green":
                case "cemetery":
                case "farmyard":
                case "greenfield":
                    return AreaKind800.OtherKnownDry;
                default:
                    return AreaKind800.Unknown;
            }
        }
    }
}
