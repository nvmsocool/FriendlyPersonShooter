using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// Lanterns: a dark room full of shades. A helpful (payload) hit on an ally makes them glow for
    /// <see cref="GlowSeconds"/>; an enemy can only be hurt while a glowing player stands within
    /// <see cref="LightRadius"/> of it. Kill <see cref="Target"/> shades. Nobody can light themselves, so
    /// the group takes turns lighting each other (a lone player always glows).
    /// </summary>
    public sealed class Lanterns
    {
        public const float GlowSeconds = 10f;
        public const float LightRadius = 7.5f;
        /// <summary>The light comes from about chest height.</summary>
        public const float LightHeight = 1.1f;

        private readonly Dictionary<int, double> _glowUntil = new Dictionary<int, double>();

        public Lanterns(int players)
        {
            Players = Math.Max(1, players);
            Target = 6 + 2 * Players;
            MaxAlive = 2 + (Players + 1) / 2;
        }

        public int Players { get; }
        /// <summary>Kills needed.</summary>
        public int Target { get; }
        /// <summary>How many shades the room keeps up at once.</summary>
        public int MaxAlive { get; }
        public int Kills { get; private set; }
        /// <summary>Hits that landed on a shade out of the light (they do nothing).</summary>
        public int DarkHits { get; private set; }
        public int Lightings { get; private set; }
        public bool Solved => Kills >= Target;
        public float Progress => Math.Min(1f, Kills / (float)Target);

        /// <summary>An ally's payload landed on this player: they glow (again) for the full time.</summary>
        public void Lit(int playerId, double now)
        {
            _glowUntil[playerId] = now + GlowSeconds;
            Lightings++;
        }

        /// <summary>Seconds of glow left (a lone player always glows).</summary>
        public float GlowLeft(int playerId, double now)
        {
            if (Players == 1) return GlowSeconds;
            return _glowUntil.TryGetValue(playerId, out double until) ? (float)Math.Max(0, until - now) : 0f;
        }

        public bool Glowing(int playerId, double now) => GlowLeft(playerId, now) > 0f;

        /// <summary>
        /// True if a shade at <paramref name="enemy"/> is in the light: some glowing player who is up stands
        /// within <see cref="LightRadius"/> of it. Positions are feet, in any one frame of reference.
        /// </summary>
        public bool InLight(Vector3 enemy, IReadOnlyList<(int Id, Vector3 Feet, bool Up)> players, double now)
        {
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p.Up || !Glowing(p.Id, now)) continue;
                var light = p.Feet + new Vector3(0f, LightHeight, 0f);
                if (Vector3.DistanceSquared(light, enemy) <= LightRadius * LightRadius) return true;
            }
            return false;
        }

        /// <summary>A hit on a shade out of the light: counted, for the log and the hint.</summary>
        public void HitInDark() => DarkHits++;

        /// <summary>A shade died. True if that solved the room.</summary>
        public bool Killed()
        {
            if (Solved) return false;
            Kills++;
            return Solved;
        }

        /// <summary>For the HUD: bits 0-7 kills, 8-15 target.</summary>
        public int Pack() => Math.Min(255, Kills) | (Math.Min(255, Target) << 8);

        public static int KillsOf(int packed) => packed & 255;
        public static int TargetOf(int packed) => (packed >> 8) & 255;
    }
}
