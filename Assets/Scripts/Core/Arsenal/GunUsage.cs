using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>
    /// Playtest numbers for tuning guns: per frame, how long it was held, shots, hits, damage,
    /// healing and kills; how long each payload was held; and what people took from the ground.
    /// The server feeds it human players only, so bots don't skew it.
    /// </summary>
    public sealed class GunUsage
    {
        public sealed class FrameTotals
        {
            public double HeldSeconds;
            public int Shots, EnemyHits, AllyHits, Kills;
            public float Damage, Healing;
        }

        private readonly Dictionary<FrameKind, FrameTotals> _frames = new Dictionary<FrameKind, FrameTotals>();
        private readonly Dictionary<PayloadKind, double> _payloadSeconds = new Dictionary<PayloadKind, double>();
        private readonly Dictionary<string, int> _taken = new Dictionary<string, int>();

        public int BoxesOpened { get; private set; }

        public FrameTotals For(FrameKind frame)
        {
            if (!_frames.TryGetValue(frame, out var t)) _frames[frame] = t = new FrameTotals();
            return t;
        }

        public double PayloadSeconds(PayloadKind p) => _payloadSeconds.TryGetValue(p, out var s) ? s : 0;
        public int Taken(string label) => _taken.TryGetValue(label, out var n) ? n : 0;

        public void Held(Gun gun, double seconds)
        {
            if (gun.IsEmpty) return;
            For(gun.Frame).HeldSeconds += seconds;
            _payloadSeconds.TryGetValue(gun.Payload, out var s);
            _payloadSeconds[gun.Payload] = s + seconds;
        }

        public void Shots(FrameKind frame, int count) => For(frame).Shots += count;

        public void EnemyHit(FrameKind frame, float damage, bool killed)
        {
            var t = For(frame);
            t.EnemyHits++;
            t.Damage += damage;
            if (killed) t.Kills++;
        }

        public void AllyHit(FrameKind frame, float healing)
        {
            var t = For(frame);
            t.AllyHits++;
            t.Healing += healing;
        }

        public void BoxOpened() => BoxesOpened++;

        public void Took(LootItem item)
        {
            string label = Label(item);
            _taken.TryGetValue(label, out int n);
            _taken[label] = n + 1;
        }

        /// <summary>"Rifle gun", "Epic Damage mod", "Rare Vigor payload".</summary>
        public static string Label(LootItem item)
        {
            if (item.IsGun) return GunFrames.Name(item.Gun.Frame) + " gun";
            var m = item.Mod;
            return $"{m.Tier} {GunMods.Name(m.Kind)} {(m.IsPayload ? "payload" : "mod")}";
        }

        /// <summary>One log line: frames by time held, payloads by time held, and the most taken items.</summary>
        public string Report()
        {
            var sb = new StringBuilder("[Guns]");
            foreach (var kv in _frames.OrderByDescending(k => k.Value.HeldSeconds))
            {
                var t = kv.Value;
                int hits = t.EnemyHits + t.AllyHits;
                float accuracy = t.Shots > 0 ? 100f * hits / t.Shots : 0f;
                double minutes = Math.Max(t.HeldSeconds / 60.0, 1e-6);
                sb.Append($" {GunFrames.Name(kv.Key)}: held {t.HeldSeconds:0}s shots={t.Shots} hit={accuracy:0}% " +
                          $"dmg={t.Damage:0} ({t.Damage / minutes:0}/min) heal={t.Healing:0} ({t.Healing / minutes:0}/min) kills={t.Kills} |");
            }
            sb.Append(" payloads:");
            foreach (var kv in _payloadSeconds.OrderByDescending(k => k.Value)) sb.Append($" {kv.Key}={kv.Value:0}s");
            sb.Append($" | boxes={BoxesOpened} taken:");
            foreach (var kv in _taken.OrderByDescending(k => k.Value).ThenBy(k => k.Key)) sb.Append($" {kv.Key} x{kv.Value},");
            return sb.ToString().TrimEnd(',');
        }
    }
}
