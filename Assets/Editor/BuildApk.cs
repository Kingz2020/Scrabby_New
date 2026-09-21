#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the Android player the same way every time, from the menu or from the
// command line.
//
// Two kinds of build, for two different jobs:
//
//   APK - for putting on a phone by cable while testing. Debug-signed, so it
//         needs nothing set up and nothing secret.
//
//   AAB - for uploading to Google Play, which accepts nothing else for a new
//         app. Signed with the upload key, and refuses to build without one:
//         an unsigned bundle is rejected at upload anyway, but only after the
//         wait, and the reason is easier to fix from here.
//
// The signing secrets never touch the project. They are read from a file in
// the user's own folder, outside the repository, and the keystore settings are
// put back to empty the moment the build is done so nothing about them is
// written into ProjectSettings and committed.
public static class BuildApk
{
    private const string OutputDirectory = "Builds";

    // Google Play's rule for new apps from August 2026 onward. Pinned rather
    // than left on "automatic", which is whatever the installed SDK happens to
    // be and can change underneath a build without anyone deciding it should.
    private const int TargetSdk = 36;

    // Where the upload key's details live - deliberately not in the project.
    private static string SigningFile
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".scrabby", "signing.json");
        }
    }

    [Serializable]
    private class Signing
    {
        public string keystorePath;
        public string keystorePass;
        public string keyAlias;
        public string keyPass;
    }

    // ------------------------------------------------------------- entries --

    [MenuItem("Scrabby/Build/Android APK (test, by cable)")]
    public static void BuildFromMenu()
    {
        Build(false, false);
    }

    [MenuItem("Scrabby/Build/Android App Bundle (for Google Play)")]
    public static void BuildBundleFromMenu()
    {
        Build(false, true);
    }

    // Called by Unity in batch mode:
    //   Unity.exe -quit -batchmode -projectPath <path>
    //             -executeMethod BuildApk.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        Build(true, false);
    }

    //   ... -executeMethod BuildApk.BuildBundleFromCommandLine
    public static void BuildBundleFromCommandLine()
    {
        Build(true, true);
    }

    // ---------------------------------------------------------------- build --

    private static void Build(bool batch, bool forPlay)
    {
        string[] scenes = EnabledScenes();

        if (scenes.Length == 0)
        {
            Fail(batch, "No scenes are enabled in the build settings.");
            return;
        }

        Signing signing = null;

        if (forPlay)
        {
            signing = ReadSigning(out string problem);

            if (signing == null)
            {
                Fail(batch, problem);
                return;
            }
        }

        AppIconSetup.Apply();

        Directory.CreateDirectory(OutputDirectory);

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[BUILD] Switching to Android.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
            {
                Fail(batch, "Could not switch the build target to Android.");
                return;
            }
        }

        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetSdk;

        // Remembered so they can be put back exactly as they were.
        int versionCodeBefore = PlayerSettings.Android.bundleVersionCode;
        bool bundleBefore = EditorUserBuildSettings.buildAppBundle;

        string path;

        try
        {
            if (forPlay)
            {
                // Play refuses an upload whose version code is not higher than
                // every one uploaded before it - including uploads that were
                // rejected. Worked out from the clock rather than counted, so
                // it cannot be forgotten, cannot go backwards, and does not
                // need writing back into ProjectSettings to be remembered.
                PlayerSettings.Android.bundleVersionCode = VersionCodeForNow();

                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = signing.keystorePath;
                PlayerSettings.Android.keystorePass = signing.keystorePass;
                PlayerSettings.Android.keyaliasName = signing.keyAlias;
                PlayerSettings.Android.keyaliasPass = signing.keyPass;

                EditorUserBuildSettings.buildAppBundle = true;
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                EditorUserBuildSettings.buildAppBundle = false;
            }

            // Named by version and date so two builds are tellable apart,
            // and a bundle carries its version code so the one uploaded can
            // be matched to the one in the console.
            path = Path.Combine(OutputDirectory, forPlay
                ? string.Format("Scrabby_{0}_code{1}_{2}.aab",
                                PlayerSettings.bundleVersion,
                                PlayerSettings.Android.bundleVersionCode,
                                DateTime.Now.ToString("yyyyMMdd_HHmm"))
                : string.Format("Scrabby_{0}_{1}.apk",
                                PlayerSettings.bundleVersion,
                                DateTime.Now.ToString("yyyyMMdd_HHmm")));

            Debug.Log("[BUILD] Building " + path + " from " + scenes.Length +
                      " scene(s), target SDK " + TargetSdk +
                      (forPlay ? ", version code " + PlayerSettings.Android.bundleVersionCode +
                                 ", signed with " + signing.keyAlias
                               : ", debug-signed") + ".");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail(batch, "Build " + summary.result + " with " +
                            summary.totalErrors + " error(s).");
                return;
            }

            // The size of the file that was written. The report's own total is
            // the uncompressed payload - it said 750 MB for a 73 MB APK - and a
            // number that is wrong by ten times is worse than no number.
            long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;

            Debug.Log(string.Format(
                "[BUILD] Succeeded: {0} ({1:0.0} MB, {2})",
                path, bytes / (1024f * 1024f), summary.totalTime));
        }
        finally
        {
            // Nothing about the upload key is left in the project, and the
            // version code in ProjectSettings is left as it was - the clock
            // decides the next one, not the file.
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = "";
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasName = "";
            PlayerSettings.Android.keyaliasPass = "";
            PlayerSettings.Android.bundleVersionCode = versionCodeBefore;
            EditorUserBuildSettings.buildAppBundle = bundleBefore;
        }

        if (batch)
            EditorApplication.Exit(0);
    }

    // Minutes since the start of 2026. Always rising, a few hundred thousand
    // today, and good for thousands of years before it reaches Play's ceiling
    // of 2,100,000,000. Two builds in the same minute would share one, which
    // is the one way to trip it.
    private static int VersionCodeForNow()
    {
        DateTime epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return Math.Max(2, (int)(DateTime.UtcNow - epoch).TotalMinutes);
    }

    private static Signing ReadSigning(out string problem)
    {
        problem = null;

        if (!File.Exists(SigningFile))
        {
            problem =
                "No upload key is set up, so there is nothing to sign the bundle " +
                "with.\n\nExpected: " + SigningFile + "\n\nSee Tools/SIGNING.md in " +
                "the project for how to make one.";
            return null;
        }

        Signing signing;

        try
        {
            signing = JsonUtility.FromJson<Signing>(File.ReadAllText(SigningFile));
        }
        catch (Exception error)
        {
            problem = SigningFile + " could not be read: " + error.Message;
            return null;
        }

        if (signing == null ||
            string.IsNullOrEmpty(signing.keystorePath) ||
            string.IsNullOrEmpty(signing.keystorePass) ||
            string.IsNullOrEmpty(signing.keyAlias) ||
            string.IsNullOrEmpty(signing.keyPass))
        {
            problem = SigningFile + " is missing one of keystorePath, keystorePass, " +
                      "keyAlias or keyPass.";
            return null;
        }

        if (!File.Exists(signing.keystorePath))
        {
            problem = "The keystore named in " + SigningFile + " is not there: " +
                      signing.keystorePath;
            return null;
        }

        return signing;
    }

    private static string[] EnabledScenes()
    {
        var scenes = new System.Collections.Generic.List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled)
                scenes.Add(scene.path);

        return scenes.ToArray();
    }

    private static void Fail(bool batch, string message)
    {
        Debug.LogError("[BUILD] " + message);

        // A batch build has to exit non-zero, or a script calling it cannot
        // tell a failed build from a finished one.
        if (batch)
            EditorApplication.Exit(1);
        else
            EditorUtility.DisplayDialog("Build failed", message, "OK");
    }
}
#endif
