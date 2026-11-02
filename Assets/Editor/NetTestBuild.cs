using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FriendlyPersonShooter.EditorTools
{
    /// <summary>Builds the standalone networking test, used to run a headless server and bot processes.</summary>
    public static class NetTestBuild
    {
        public const string OutputPath = "Builds/NetTest/NetTest.exe";

        [MenuItem("FriendlyPersonShooter/Build Net Test (Windows)")]
        public static string BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                // -fps-game arena switches to the Arena scene at startup.
                scenes = new[] { "Assets/Scenes/NetTest.unity", "Assets/Scenes/Arena.unity", "Assets/Scenes/Expedition.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                // Enables Netcode's network simulator, so a bot process can run with lag:
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
