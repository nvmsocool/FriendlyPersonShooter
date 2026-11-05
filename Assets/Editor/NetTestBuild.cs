using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FriendlyPersonShooter.EditorTools
{
    /// <summary>Dev build with Netcode's network simulator, for testing under lag. Skips the menu; use -fps-mode server|client to split processes.</summary>
    public static class NetTestBuild
    {
        public const string OutputPath = "Builds/NetTest/NetTest.exe";

        [MenuItem("FriendlyPersonShooter/Build Net Test (Windows)")]
        public static string BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                // Starts straight in the expedition (no menu); -fps-game arena switches to the Arena scene.
                scenes = new[] { "Assets/Scenes/Expedition.unity", "Assets/Scenes/Arena.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                // Enables Netcode's network simulator, so a client can run with lag:
                // NetTest.exe ... --loadNetworkSimulatorJsonFile Tools/NetSim/laggy.json
                extraScriptingDefines = new[] { "NETCODE_DEBUG" },
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            string summary = $"{report.summary.result} in {report.summary.totalTime.TotalSeconds:0}s, " +
                             $"{report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)}MB";
            Debug.Log($"[NetTestBuild] {summary}");
            return summary;
        }
    }
}
