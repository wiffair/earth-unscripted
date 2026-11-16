using UnityEditor;
using UnityEngine;

namespace EarthUnscripted.Editor
{
    public sealed class BrandArtworkImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/EarthUnscripted/")) return;
            var texture = (TextureImporter)assetImporter;
            texture.textureType = assetPath.EndsWith("/Icon.png") ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (texture.textureType == TextureImporterType.Sprite) texture.spriteImportMode = SpriteImportMode.Single;
            texture.mipmapEnabled = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.maxTextureSize = 2048;
            texture.textureCompression = TextureImporterCompression.CompressedHQ;
            texture.isReadable = false;
        }
    }
}
