using UnityEditor;
using UnityEngine;
namespace RoadRush.Editor
{
    /// <summary>Keep short recorded engine loops decompressed and ready before playback.</summary>
    public sealed class CamperAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if(!assetPath.StartsWith("Assets/Resources/CamperAudio/") || !assetPath.EndsWith(".wav"))return;
            var importer=(AudioImporter)assetImporter;
            var settings=importer.defaultSampleSettings;
            settings.compressionFormat=AudioCompressionFormat.PCM;
            settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData=true;
            importer.defaultSampleSettings=settings;
            importer.forceToMono=true;importer.loadInBackground=false;
        }
    }
}
