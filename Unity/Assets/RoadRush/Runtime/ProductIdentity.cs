using System;
using System.IO;
using UnityEngine;

namespace RoadRush
{
    /// <summary>Public branding is independent of persisted identifiers and wire formats.</summary>
    public static class ProductIdentity
    {
        public const string Name = "Earth Unscripted";
        public const string BetaName = Name + " Beta";
        public const string Tagline = "Real places. Your adventure.";
        public const string Website = "https://earthunscripted.com";
        public const string Version = "0.23.0-beta.52";

        // Keep the existing beta's identity on disk. Renaming a product must not
        // create a new career, abandon cached areas or cross into live-game saves.
        public static string BetaStorageRoot
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (Application.platform == RuntimePlatform.OSXPlayer)
                    return Path.Combine(home, "Library/Application Support/Blackwood Games Beta/Road Rush Beta");
                if (Application.platform == RuntimePlatform.WindowsPlayer)
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData/LocalLow/Blackwood Games Beta/Road Rush Beta");
                if (Application.platform == RuntimePlatform.LinuxPlayer)
                {
                    string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    if (string.IsNullOrWhiteSpace(config)) config = Path.Combine(home, ".config");
                    return Path.Combine(config, "unity3d/Blackwood Games Beta/Road Rush Beta");
                }
                return Application.persistentDataPath;
            }
        }
    }
}
