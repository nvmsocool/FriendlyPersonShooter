using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FriendlyPersonShooter.EditorTools
{
    /// <summary>
    /// Builds the playtest game for friends: it starts at the main menu (host with a join code, or join one).
    /// The command-line flags of the net test still work in it (-fps-game ... skips the menu).
    /// </summary>
    public static class PlaytestBuild
    {
        public const string OutputFolder = "Builds/Playtest";
        public const string ExeName = "FriendlyPersonShooter.exe";

        [MenuItem("FriendlyPersonShooter/Build Playtest (Windows)")]
        public static string BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Expedition.unity", "Assets/Scenes/Arena.unity" },
                locationPathName = OutputFolder + "/" + ExeName,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "FPS_PLAYTEST" },
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            string summary = $"{report.summary.result} in {report.summary.totalTime.TotalSeconds:0}s, " +
                             $"{report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)}MB";
            Debug.Log($"[PlaytestBuild] {summary}");
            return summary;
        }

        /// <summary>Batch-mode entry point for CI: same build, but exits with code 1 if it fails.</summary>
        public static void BuildWindowsCI()
        {
            string summary = BuildWindows();
            EditorApplication.Exit(summary.StartsWith(nameof(BuildResult.Succeeded)) ? 0 : 1);
        }
    }
}
