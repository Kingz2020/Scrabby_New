#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the Android player the same way every time, from the menu or from the
// command line, so a test build on a handheld is one step rather than a walk
// through the build dialog.
public static class BuildApk
{
    private const string OutputDirectory = "Builds";

    [MenuItem("Scrabby/Build/Android APK")]
    public static void BuildFromMenu()
    {
        Build(false);
    }

    // Called by Unity in batch mode:
    //   Unity.exe -quit -batchmode -projectPath <path>
    //             -executeMethod BuildApk.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        Build(true);
    }

    private static void Build(bool batch)
    {
        string[] scenes = EnabledScenes();

        if (scenes.Length == 0)
        {
            Fail(batch, "No scenes are enabled in the build settings.");
            return;
        }

        AppIconSetup.Apply();

        Directory.CreateDirectory(OutputDirectory);

        // Named by version and date so two builds on a phone are tellable
        // apart, which matters when testing a fix against the thing it fixed.
        string name = string.Format(
            "Scrabby_{0}_{1}.apk",
            PlayerSettings.bundleVersion,
            DateTime.Now.ToString("yyyyMMdd_HHmm"));

        string path = Path.Combine(OutputDirectory, name);

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

        Debug.Log("[BUILD] Building " + path + " from " + scenes.Length + " scene(s).");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log(string.Format(
                "[BUILD] Succeeded: {0} ({1:0.0} MB, {2})",
                path,
                summary.totalSize / (1024f * 1024f),
                summary.totalTime));

            if (batch)
                EditorApplication.Exit(0);

            return;
        }

        Fail(batch, "Build " + summary.result + " with " +
                    summary.totalErrors + " error(s).");
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
