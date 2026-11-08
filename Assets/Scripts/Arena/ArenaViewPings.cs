using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Pings (middle mouse): a marker over the spot drawn on top of everything (pulled onto the screen's edge when
    /// the spot is off screen), a light pillar in the world, a diamond on the minimap, and a chirp when one lands.
    /// </summary>
    public partial class ArenaView
    {
        /// <summary>How long a ping stays up (seconds); it fades over the last second.</summary>
        private const float PingSeconds = 8f;
        /// <summary>Keeps edge markers this far inside the screen (pixels).</summary>
        private const float PingEdge = 48f;

        private float _pingsSeenUpTo = -1f;
        private readonly Dictionary<int, Material> _pingMats = new Dictionary<int, Material>();
        private readonly List<Matrix4x4> _pingBeam = new List<Matrix4x4>();

        private Material PingMat(int networkId)
        {
            if (!_pingMats.TryGetValue(networkId, out var mat))
            {
                mat = MakeGlow(PlayerColors.For(networkId), 1.6f);
                _pingMats[networkId] = mat;
            }
            return mat;
        }

        /// <summary>The world part: a thin pillar of light rising from each ping, and the chirp for new ones.</summary>
        private void DrawPings()
        {
            float now = Time.unscaledTime;
            ArenaFx.Pings.RemoveAll(p => now - p.Time > PingSeconds);
            foreach (var (ping, time) in ArenaFx.Pings)
            {
                if (time > _pingsSeenUpTo)
                {
                    _pingsSeenUpTo = time;
                    ArenaAudio.Ping();
                }
                float age = now - time;
                // Pops up, then stays thin.
                float grow = Mathf.Clamp01(age / 0.25f);
                float width = Mathf.Lerp(1.2f, 0.25f, grow);
                var at = Near(ping.Position);
                _pingBeam.Clear();
                _pingBeam.Add(Matrix4x4.TRS(at + Vector3.up * 6f, Quaternion.identity, new Vector3(width, 12f * grow, width)));
                _pingBeam.Add(Matrix4x4.TRS(at, Quaternion.Euler(0f, 45f + age * 90f, 0f), new Vector3(1.4f, 0.08f, 1.4f)));
                DrawList(_pingBeam, PingMat(ping.PlayerNetworkId));
            }
        }

        /// <summary>The HUD part: markers over the spots and diamonds on the minimap.</summary>
        private void WritePingHud(Entity local, Vector3 feet, Vector2 right, Vector2 forward)
        {
            float now = Time.unscaledTime;
            int slot = 0;
            for (int i = ArenaFx.Pings.Count - 1; i >= 0 && slot < ArenaHud.PingSlots; i--, slot++)
            {
                var (ping, time) = ArenaFx.Pings[i];
                float age = now - time;
                float alpha = Mathf.Clamp01(PingSeconds - age);
                var color = PlayerColors.For(ping.PlayerNetworkId);
                var at = Near(ping.Position);
                var o = new Vector2(at.x - feet.x, at.z - feet.z);
                _hud.SetPingDot(slot, local != Entity.Null, new Vector2(Vector2.Dot(o, right), Vector2.Dot(o, forward)), color);

                if (viewCamera == null)
                {
                    _hud.SetPing(slot, null);
                    continue;
                }
                var mark = at + Vector3.up * (ping.Kind == PingFx.Enemy ? 2.2f : 0.6f);
                var screen = viewCamera.WorldToScreenPoint(mark);
                var size = new Vector2(Screen.width, Screen.height);
                var p = new Vector2(screen.x, screen.y);
                if (screen.z < 0f) p = size - p; // behind you: mirror, then pin to the edge below
                bool off = screen.z < 0f || p.x < PingEdge || p.y < PingEdge || p.x > size.x - PingEdge || p.y > size.y - PingEdge;
                if (off)
                {
                    // Pull it onto the screen's edge along the line from the centre.
                    var c = size * 0.5f;
                    var d = p - c;
                    if (d.sqrMagnitude < 1f) d = Vector2.down;
                    float sx = (c.x - PingEdge) / Mathf.Max(0.001f, Mathf.Abs(d.x));
                    float sy = (c.y - PingEdge) / Mathf.Max(0.001f, Mathf.Abs(d.y));
                    p = c + d * Mathf.Min(sx, sy);
                }
                float metres = Vector3.Distance(at, feet);
                string who = ping.PlayerNetworkId == _localNetIdForPings ? "" : PlayerColors.Name(ping.PlayerNetworkId) + ": ";
                string label = ping.Kind == PingFx.Enemy ? $"{who}ENEMY {metres:0} m" : $"{who}{metres:0} m";
                float pop = 1f + 0.6f * Mathf.Max(0f, 1f - age * 4f);
                _hud.SetPing(slot, label, p, color, alpha, pop);
            }
            for (; slot < ArenaHud.PingSlots; slot++)
            {
                _hud.SetPing(slot, null);
                _hud.SetPingDot(slot, false);
            }
        }

        /// <summary>The human's network id, for leaving their own name off their pings.</summary>
        private int _localNetIdForPings = -1;

        private void NotePingOwner(World world, Entity local)
        {
            _localNetIdForPings = local != Entity.Null && world.EntityManager.HasComponent<GhostOwner>(local)
                ? world.EntityManager.GetComponentData<GhostOwner>(local).NetworkId
                : -1;
        }
    }
}
