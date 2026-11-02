using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Profiling;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Logs frame rate, memory and connected bots every few seconds, so headless test runs can be measured.
    /// Starts itself; nothing needs to be in the scene.
    /// </summary>
    public class NetPerfLogger : MonoBehaviour
    {
        private const float Interval = 5f;
        private float _nextLog;
        private int _frames;
        private float _windowStart;
        private float _worstFrame;
        private readonly System.Collections.Generic.Dictionary<string, int> _stuck =
            new System.Collections.Generic.Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("NetPerfLogger");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<NetPerfLogger>();

            // Headless processes would otherwise spin as fast as possible and starve each other.
            if (Application.isBatchMode) Application.targetFrameRate = 60;
        }

        /// <summary>A bot whose simulated player is logged off isn't stuck.</summary>
        private static bool IsOfflineByChoice(World world)
        {
            var pop = BotDirector.Population;
            if (pop == null) return false;
            using var seat = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BotSeat>());
            if (seat.CalculateEntityCount() != 1) return false;
            return !pop.Accounts[seat.GetSingleton<BotSeat>().Index].WantsOnline;
        }

        private void Update()
        {
            _frames++;
            _worstFrame = Mathf.Max(_worstFrame, Time.unscaledDeltaTime);
            float now = Time.realtimeSinceStartup;
            if (now < _nextLog) return;

            float elapsed = Mathf.Max(now - _windowStart, 0.001f);
            int botsInGame = 0, botWorlds = 0;
            foreach (var world in World.All)
            {
                if (!world.IsThinClient()) continue;
                botWorlds++;
                using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamInGame>());
                if (q.CalculateEntityCount() > 0)
                {
                    botsInGame++;
                    continue;
                }
                if (IsOfflineByChoice(world)) continue;
                // Record where bots that aren't in the game yet are stuck.
                using var conn = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
                string state = "no-connection";
                if (conn.CalculateEntityCount() > 0)
                {
                    state = conn.GetSingleton<NetworkStreamConnection>().CurrentState.ToString();
                }
                _stuck[state] = _stuck.TryGetValue(state, out int n) ? n + 1 : 1;
            }
            string stuck = "";
            foreach (var kv in _stuck) stuck += $" {kv.Key}={kv.Value}";
            _stuck.Clear();

            long memoryMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            Debug.Log($"[Perf] mode={NetTestSettings.Mode} fps={_frames / elapsed:0.0} worstFrame={_worstFrame * 1000f:0}ms " +
                      $"memory={memoryMb}MB botsInGame={botsInGame}/{botWorlds}" +
                      (stuck.Length > 0 ? $" waiting:{stuck}" : ""));

            _frames = 0;
            _worstFrame = 0f;
            _windowStart = now;
            _nextLog = now + Interval;
        }
    }
}
