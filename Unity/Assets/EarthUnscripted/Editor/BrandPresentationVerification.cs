using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
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
            if (SinglePlayerBeta.Identifier != "com.earthunscripted.beta.singleplayer" ||
                SinglePlayerBeta.HDIdentifier != SinglePlayerBeta.Identifier + ".hd")
                throw new InvalidDataException("Earth Unscripted application identifiers");
            if (ProductIdentity.BetaStorageRoot != Application.persistentDataPath)
                throw new InvalidDataException("Earth Unscripted profile must use this product's storage");
            Debug.Log("Earth Unscripted brand, artwork and fresh-profile identity passed");
        }
    }
}
