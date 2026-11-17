using UnityEngine;

namespace EarthUnscripted
{
    public static class ProductIdentity
    {
        public const string Name = "Earth Unscripted";
        public const string Company = Name;
        public const string BetaName = Name + " Beta";
        public const string Tagline = "Real places. Your adventure.";
        public const string Website = "https://earthunscripted.com";
        public const string Version = "0.24.0-beta.14";
        public static string BetaStorageRoot => Application.persistentDataPath;
    }
}
