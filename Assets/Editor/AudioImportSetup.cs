#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// The import settings for game sounds, applied on the way in.
//
// Left to the defaults, a WAV dropped into the project is imported as stereo
// PCM and streamed - three ways wrong for a short interface sound. Mono
// halves it for no audible loss, since these are not placed in a world;
// Vorbis takes it further; and a sound that decides whether it has finished
// loading at the moment it is asked for arrives late, which for a tile going
// down is the one thing it must not do.
//
// Done here rather than in the inspector so that adding a sound is dropping
// in a file, and so nobody has to remember this.
public class AudioImportSetup : AssetPostprocessor
{
    private void OnPreprocessAudio()
    {
        string path = assetPath.Replace("\\", "/");

        if (!path.Contains("/Resources/Audio/"))
            return;

        AudioImporter importer = assetImporter as AudioImporter;

        if (importer == null)
            return;

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;

        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.7f;
        settings.preloadAudioData = true;

        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.loadInBackground = false;

        Debug.Log("[SOUND] Imported " + path + " as mono, Vorbis, ready in memory.");
    }
}
#endif
