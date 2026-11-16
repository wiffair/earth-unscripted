using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoadRush.Editor
{
    public static class BrandPresentationVerification
    {
        public static void Run()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/EarthUnscripted/Icon.png");
            var splash = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/EarthUnscripted/Splash.png");
            if (!icon || icon.width < 512 || icon.width != icon.height) throw new InvalidDataException("Square application artwork missing");
            if (!splash || (float)splash.width / splash.height < 1.7f) throw new InvalidDataException("Widescreen loading artwork missing");
            if (ProductIdentity.Name != "Earth Unscripted") throw new InvalidDataException("Public product identity");
            // The public name is deliberately not the persisted bundle/profile
            // identity. Changing these would strand existing careers and packs.
            if (SinglePlayerBeta.Identifier != "games.blackwood.roadrush.beta.singleplayer" || SinglePlayerBeta.HDIdentifier != SinglePlayerBeta.Identifier + ".hd")
                throw new InvalidDataException("Existing beta identity was changed");
            var identity = File.ReadAllText("Assets/RoadRush/Runtime/ProductIdentity.cs");
            foreach (string path in new[] { "Library/Application Support/Blackwood Games Beta/Road Rush Beta", "AppData/LocalLow/Blackwood Games Beta/Road Rush Beta", "unity3d/Blackwood Games Beta/Road Rush Beta" })
                if (!identity.Contains(path)) throw new InvalidDataException("Established profile location missing: " + path);
            Debug.Log("Earth Unscripted brand, artwork and existing-career compatibility contract passed");
        }
    }
}
