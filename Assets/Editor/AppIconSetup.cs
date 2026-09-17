#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

// Puts the Scrabby icon in every slot Android asks for.
//
// Android wants three kinds: an adaptive icon in two layers (the launcher
// crops the background to the phone's own shape and lays the foreground over
// it), a round one, and a legacy square one for older phones. Doing that by
// hand is a dozen texture slots in Player Settings; this fills them all from
// three images, and the build calls it so an APK never goes out with Unity's
// default icon.
public static class AppIconSetup
{
    private const string Folder = "Assets/Scrabby/Art/AppIcon/";
    private const string Full = Folder + "ScrabbyIcon.png";
    private const string Background = Folder + "ScrabbyIcon_Background.png";
    private const string Foreground = Folder + "ScrabbyIcon_Foreground.png";

    [MenuItem("Scrabby/Build/Apply App Icon")]
    public static void ApplyFromMenu()
    {
        if (Apply())
            AssetDatabase.SaveAssets();
    }

    public static bool Apply()
    {
        Texture2D full = AssetDatabase.LoadAssetAtPath<Texture2D>(Full);
        Texture2D background = AssetDatabase.LoadAssetAtPath<Texture2D>(Background);
        Texture2D foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(Foreground);

        if (full == null || background == null || foreground == null)
        {
            Debug.LogWarning("[ICON] Icon images missing from " + Folder +
                             "; leaving the icon as it is.");
            return false;
        }

        NamedBuildTarget android = NamedBuildTarget.Android;

        PlatformIcon[] adaptive = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Adaptive);
        foreach (PlatformIcon icon in adaptive)
            icon.SetTextures(background, foreground);   // layer 0 background, layer 1 foreground
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Adaptive, adaptive);

        PlatformIcon[] round = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Round);
        foreach (PlatformIcon icon in round)
            icon.SetTexture(full);
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Round, round);

        PlatformIcon[] legacy = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Legacy);
        foreach (PlatformIcon icon in legacy)
            icon.SetTexture(full);
        PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Legacy, legacy);

        // the project-wide default, used anywhere a platform has no icon of its own
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { full }, IconKind.Application);

        Debug.Log("[ICON] App icon applied: " + adaptive.Length + " adaptive, " +
                  round.Length + " round, " + legacy.Length + " legacy slots.");
        return true;
    }
}
#endif
