using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition half of the view (MVP step 4): zone floors coloured by tile, landmarks,
    /// shrines, the gate, dropped caches and the expedition HUD (no beacon on the objective: you go and look at the gate).
    /// The world wraps, so everything is drawn at its copy nearest the camera.
    /// </summary>
    public partial class ArenaView
    {
        private Material[] _tileMats;
        /// <summary>The gate's colour runes, per essence colour: lit (banked) and faint (still needed).</summary>
        private Material[] _gateRuneMats, _gateRuneDimMats;
        private readonly List<Matrix4x4> _gateRunesLit = new List<Matrix4x4>(), _gateRunesDim = new List<Matrix4x4>();
        private Material _landmarkMat, _shrineMat, _gateMat, _gateOpenMat, _cacheMat, _worldBossBeaconMat, _upPortalMat, _upPortalCoreMat;
        private EntityQuery _cacheQuery, _statusQuery;
        private readonly List<Matrix4x4> _x = new List<Matrix4x4>();
        private float3 _camRef;
        private bool _wrap;

        /// <summary>A world position moved to its copy nearest the camera (unchanged in the arena).</summary>
        private Vector3 Near(float3 p) => _wrap ? (Vector3)ExpeditionWorld.Near(p, _camRef) : (Vector3)p;

        private void CreateExpeditionMaterials()
        {
            var tiles = new[]
            {
                new Color(0.42f, 0.4f, 0.36f), new Color(0.3f, 0.42f, 0.5f), new Color(0.45f, 0.43f, 0.4f), new Color(0.55f, 0.42f, 0.3f),
                new Color(0.36f, 0.5f, 0.3f), new Color(0.33f, 0.45f, 0.28f), new Color(0.5f, 0.48f, 0.38f), new Color(0.4f, 0.38f, 0.45f),
            };
            _tileMats = new Material[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) _tileMats[i] = MakeMat(tiles[i]);
            _landmarkMat = MakeMat(new Color(0.55f, 0.5f, 0.45f));
            _shrineMat = MakeMat(new Color(0.4f, 0.95f, 1f));
            // Neutral stone, so the runes of the gate's colour stand out.
            _gateMat = MakeMat(new Color(0.4f, 0.38f, 0.42f));
            _gateOpenMat = MakeMat(new Color(1f, 0.95f, 0.4f));
            _cacheMat = MakeMat(new Color(1f, 0.85f, 0.1f));
            _worldBossBeaconMat = MakeMat(new Color(1f, 0.55f, 0.15f));
            _upPortalMat = MakeMat(new Color(0.55f, 0.6f, 0.7f));
            _upPortalCoreMat = MakeGlow(new Color(0.45f, 0.8f, 1f), 1.2f);
            _gateRuneMats = new Material[EssenceColors.Count];
            _gateRuneDimMats = new Material[EssenceColors.Count];
            foreach (var c in EssenceColors.All)
            {
                ColorUtility.TryParseHtmlString(EssencePack.Hex(c), out var color);
                _gateRuneMats[(int)c] = MakeGlow(color, 1.8f);
                _gateRuneDimMats[(int)c] = MakeGlow(color * 0.6f, 0.6f);
            }
        }

        /// <summary>
        /// A remote client follows the server's layer from its status (the map, terrain and boxes follow it).
        /// On the host the server already set it, and a status a tick behind must not set it back.
        /// </summary>
        private void FollowLayer()
        {
            var server = Unity.NetCode.ClientServerBootstrap.ServerWorld;
            if (server != null && server.IsCreated) return;
            if (TryGetStatus(out var s) && s.Layer != 0 && s.Layer != ExpeditionWorld.Layer) ExpeditionWorld.Layer = s.Layer;
        }

        private void CreateExpeditionQueries(EntityManager em)
        {
            _cacheQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaCache>(), ComponentType.ReadOnly<LocalTransform>());
            _statusQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ExpeditionStatus>(), ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        private bool TryGetStatus(out ExpeditionStatus status)
        {
            status = default;
            if (_statusQuery.CalculateEntityCount() != 1) return false;
            using var all = _statusQuery.ToComponentDataArray<ExpeditionStatus>(Allocator.Temp);
            status = all[0];
            return true;
        }

        private void DrawExpeditionWorld()
        {
            FollowLayer();
            var map = ExpeditionWorld.Map;
            var tiling = map.Tiling;
            var camZone = tiling.ZoneAt(_camRef.x, _camRef.z);

            bool terrain = ExpeditionTerrain.Active;
            var zones = terrain ? ExpeditionTerrain.World : null;
            if (zones != null && zones.ReadyCount != _groundCacheZones)
            {
                _groundCache.Clear();
                _groundCacheZones = zones.ReadyCount;
            }

            // Floors for the zones around the camera, a little apart so zone edges show
            // (on generated ground: only where a zone hasn't been built yet).
            // Each floor is a square that fits inside its zone (a hexagon, square or triangle).
            foreach (var zone in tiling.Neighborhood(camZone, 2))
            {
                if (zones != null && zones.IsReady(zone)) continue;
                var c = map.ZoneCentre(zone);
                float side = tiling.EdgeDistance(zone, c) * 2f - 1f;
                var centre = ExpeditionWorld.ToFloat3(c, -0.25f);
                _x.Clear();
                _x.Add(Matrix4x4.TRS(Near(centre), Quaternion.identity, new Vector3(side, 0.5f, side)));
                DrawList(_x, _tileMats[(int)map.Zone(zone).Tile % _tileMats.Length]);
            }

            float range = tiling.Edge * 3.2f;
            _x.Clear();
            foreach (var l in map.Landmarks)
            {
                if (terrain) break;
                var p = Near(ExpeditionWorld.ToFloat3(l.Point.World));
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_camRef.x, _camRef.z)) > range) continue;
                _x.Add(Matrix4x4.TRS(p + Vector3.up * ExpeditionWorld.LandmarkHeight * 0.5f, Quaternion.identity,
                    new Vector3(ExpeditionWorld.LandmarkWidth, ExpeditionWorld.LandmarkHeight, ExpeditionWorld.LandmarkWidth)));
            }
            DrawList(_x, _landmarkMat);

            _x.Clear();
            float spin = Time.time * 40f;
            foreach (var s in map.Shrines)
            {
                var p = Near(ExpeditionWorld.ToFloat3(s.Point.World, GroundAt(s.Point.World)));
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_camRef.x, _camRef.z)) > range) continue;
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 0.1f, Quaternion.identity, new Vector3(5f, 0.2f, 5f)));
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 1.6f, Quaternion.Euler(45f, spin, 45f), Vector3.one * 0.8f));
            }
            DrawList(_x, _shrineMat);

            bool open = TryGetStatus(out var status) && status.Phase >= ExpeditionPhase.PassThrough;
            _x.Clear();
            foreach (var g in map.Gates)
            {
                var p = Near(ExpeditionWorld.ToFloat3(g.Point.World, GroundAt(g.Point.World)));
                float w = ExpeditionWorld.GateWidth, h = ExpeditionWorld.GateHeight;
                _x.Add(Matrix4x4.TRS(p + new Vector3(-w * 0.5f, h * 0.5f, 0f), Quaternion.identity, new Vector3(1f, h, 1f)));
                _x.Add(Matrix4x4.TRS(p + new Vector3(w * 0.5f, h * 0.5f, 0f), Quaternion.identity, new Vector3(1f, h, 1f)));
                _x.Add(Matrix4x4.TRS(p + new Vector3(0f, h, 0f), Quaternion.identity, new Vector3(w + 1f, 1f, 1.2f)));
                if (open) _x.Add(Matrix4x4.TRS(p + new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, new Vector3(w - 1f, h - 0.5f, 0.1f)));
            }
            DrawList(_x, open ? _gateOpenMat : _gateMat);
            DrawGateRunes(map, status, open);

            // The up-portal by the start (layers 2 and 3): a stone ring with a glowing, turning core.
            if (LayerTravel.TryPortal(map, ExpeditionWorld.Layer, out var portal))
            {
                var p = Near(ExpeditionWorld.ToFloat3(portal, GroundAt(portal)));
                float r = LayerTravel.PortalRadius;
                _x.Clear();
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    _x.Add(Matrix4x4.TRS(p + new Vector3(Mathf.Cos(a) * r, 1.6f, Mathf.Sin(a) * r), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), new Vector3(0.5f, 3.2f, 0.9f)));
                }
                DrawList(_x, _upPortalMat);
                _x.Clear();
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 4f);
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 1.6f, Quaternion.Euler(0f, spin, 0f), new Vector3(r * 1.4f, 2.6f, r * 1.4f) * pulse));
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 20f, Quaternion.identity, new Vector3(0.5f, 40f, 0.5f)));
                DrawList(_x, _upPortalCoreMat);
            }

            _x.Clear();
            using (var caches = _cacheQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                foreach (var c in caches)
                {
                    var p = Near(c.Position);
                    _x.Add(Matrix4x4.TRS(p + Vector3.up * (0.4f + 0.15f * Mathf.Sin(Time.time * 3f)), Quaternion.Euler(0f, spin * 3f, 0f), Vector3.one * 0.6f));
                }
            }
            DrawList(_x, _cacheMat);

            // No beacon over the objective: players go and look at the gate to see what it needs.

            // A thick orange pillar over the Colossus, so it can be found from anywhere.
            if (status.WorldBoss && !status.InRaid)
            {
                var at = new System.Numerics.Vector2(status.WorldBossAt.x, status.WorldBossAt.y);
                var p = Near(new float3(at.X, GroundAt(at), at.Y));
                _x.Clear();
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 60f, Quaternion.identity, new Vector3(1.2f, 120f, 1.2f)));
                DrawList(_x, _worldBossBeaconMat);
            }
        }


        /// <summary>
        /// The gate shows the essence it needs: runes of its colour up both posts (front and back), faint until the
        /// party bank holds enough to light them, and a gem of that colour on the lintel. All lit once it's open.
        /// </summary>
        /// <summary>
        /// A stone tablet over the gate with one slot per essence the gate costs, in the gate's color (see
        /// <see cref="EssenceSlots"/>). Shooting an empty one banks a carried essence and lights it; all are lit once it's open.
        /// </summary>
        private void DrawGateRunes(LayerMap map, ExpeditionStatus status, bool open)
        {
            var color = LayerSettings.ForLayer(ExpeditionWorld.Layer).Gate().Color;
            int slots = status.Goal > 0 && status.GoalColor == color ? status.Goal : 0;
            if (slots <= 0) return;
            float tabletH = EssenceSlots.GateTabletHeight(slots);
            _gateRunesLit.Clear();
            _gateRunesDim.Clear();
            _x.Clear();
            var size = new Vector3(EssenceSlots.GateSize, EssenceSlots.GateSize, 0.08f);
            var diamond = Quaternion.Euler(0f, 0f, 45f);
            foreach (var g in map.Gates)
            {
                var p = Near(ExpeditionWorld.ToFloat3(g.Point.World, GroundAt(g.Point.World)));
                if ((p - (Vector3)_camRef).sqrMagnitude > 400f * 400f) continue;
                _x.Add(Matrix4x4.TRS(p + new Vector3(0f, EssenceSlots.GateTabletBase + tabletH * 0.5f, 0f), Quaternion.identity,
                    new Vector3(EssenceSlots.GateColumns * EssenceSlots.GatePitch + 0.6f, tabletH, 0.6f)));
                for (int k = 0; k < slots; k++)
                {
                    Vector3 at = EssenceSlots.GateSlot(k, slots);
                    var list = open || GateSlotLit(status, k) ? _gateRunesLit : _gateRunesDim;
                    foreach (float face in new[] { -EssenceSlots.GateFace, EssenceSlots.GateFace })
                    {
                        list.Add(Matrix4x4.TRS(p + at + new Vector3(0f, 0f, face), diamond, size));
                    }
                }
            }
            DrawList(_x, open ? _gateOpenMat : _gateMat);
            DrawList(_gateRunesLit, _gateRuneMats[(int)color]);
            DrawList(_gateRunesDim, _gateRuneDimMats[(int)color]);
        }

        /// <summary>Is gate slot <paramref name="k"/> lit? (Past slot 128 the first ones light in order.)</summary>
        private static bool GateSlotLit(ExpeditionStatus status, int k)
        {
            if (k < 64) return ((status.GateSlots0 >> k) & 1UL) != 0;
            if (k < 128) return ((status.GateSlots1 >> (k - 64)) & 1UL) != 0;
            return k < status.Banked;
        }

        private readonly Dictionary<System.Numerics.Vector2, float> _groundCache = new Dictionary<System.Numerics.Vector2, float>();
        private int _groundCacheZones = -1;

        /// <summary>The ground under a map point (0 on the flat floor), cached until more zones finish building.</summary>
        private float GroundAt(System.Numerics.Vector2 point)
        {
            if (!ExpeditionTerrain.Active) return 0f;
            if (_groundCache.TryGetValue(point, out float y)) return y;
            if (!ExpeditionTerrain.World.IsReady(point.X, point.Y)) return 0f;
            y = ExpeditionTerrain.GroundY(point.X, point.Y);
            _groundCache[point] = y;
            return y;
        }

        private static readonly double RunSeconds = new ExpeditionSettings().TargetDuration;
        private readonly int[] _carriedSlots = new int[EssenceColors.Count];
        private Color[] _essenceColors;

        /// <summary>The top-left hourglass and essence slots (the only expedition info on the HUD).</summary>
        private void ShowExpeditionClock(bool show, ExpeditionStatus s)
        {
            if (_essenceColors == null)
            {
                _essenceColors = new Color[EssenceColors.Count];
                foreach (var c in EssenceColors.All) ColorUtility.TryParseHtmlString(EssencePack.Hex(c), out _essenceColors[(int)c]);
            }
            if (show)
            {
                foreach (var c in EssenceColors.All) _carriedSlots[(int)c] = EssencePack.Get(s.CarriedAll, c);
            }
            _hud.SetExpedition(show, show ? 1f - (float)(s.Elapsed / RunSeconds) : 0f, _carriedSlots, _essenceColors);
        }

        private void WriteExpeditionHud()
        {
            bool has = TryGetStatus(out var s);
            ShowExpeditionClock(has && _wrap, s);
            if (!has || !_wrap) return;
            int minutes = (int)(s.Elapsed / 60f), seconds = (int)(s.Elapsed % 60f);
            var e = _hud.Expedition;
            e.Append($"<b>EXPEDITION</b>   {minutes:00}:{seconds:00} / 20:00\n");
            // Every color at a glance: carried in bold, then the party bank (the gate's color shows its price).
            e.Append($"Layer {math.max(1, (int)s.Layer)}   ");
            foreach (var c in EssenceColors.All)
            {
                int carried = EssencePack.Get(s.CarriedAll, c), banked = EssencePack.Get(s.BankedAll, c);
                e.Append($"{EssencePack.Named(c)} <b>{carried}</b> +{banked}{(c == s.GoalColor ? $"/{s.Goal}" : "")}  ");
            }
            e.Append("<color=#B8B0D8>carried +banked</color>");
            if (s.Kudos > 0) e.Append($"   kudos {s.Kudos}");
            e.Append($"\n{s.Objective}\n");
            WriteLayerHud(s, e);
            WriteRaidHud(s, e);
            WriteWorldBossHud(s, e);
            if (s.HasTarget && !s.Complete)
            {
                var delta = ExpeditionWorld.Near(new float3(s.Target.x, 0f, s.Target.y), _camRef) - _camRef;
                float distance = math.length(delta.xz);
                float bearing = math.degrees(math.atan2(delta.x, delta.z));
                float relative = Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing);
                e.Append($"<color=#FFE14D>{distance:0} m {Direction(relative)}</color>\n");
            }
            if (s.BloomNear)
            {
                string prey = EssenceColors.TryGetFamily(s.GoalColor, out var family) ? EssenceColors.Plural(family) : "enemies";
                e.Append($"<color=#FFE14D>A {s.GoalColor} BLOOM is running nearby: lots of {prey}!</color>\n");
            }
            e.Append($"<color=#B8B0D8>You are {s.Location}</color>");
            WriteSocial();
            if (s.Complete)
            {
                _center.text = $"EXPEDITION COMPLETE in {minutes:00}:{seconds:00}!";
            }
        }

        /// <summary>The group's layer move: a countdown, and where the portal is on lower layers.</summary>
        private void WriteLayerHud(ExpeditionStatus s, System.Text.StringBuilder e)
        {
            if (s.LayerNext != 0)
            {
                bool down = s.LayerNext > s.Layer;
                int left = Mathf.CeilToInt(s.LayerCountdown);
                if (s.LayerNext == s.Layer)
                {
                    e.Append($"<color=#9AD8FF><b>EXPEDITION ENDS in {left}s</b>: the last gate is open</color>\n");
                    return;
                }
                e.Append($"<color=#9AD8FF><b>{(down ? "DROPPING" : "CLIMBING")} TO LAYER {s.LayerNext} in {left}s</b>: the whole group moves together, wherever you are</color>\n");
                if (left <= 5 && !s.InRaid) _center.text = $"Layer {s.LayerNext} in {left}";
                return;
            }
            if (!LayerTravel.TryPortal(ExpeditionWorld.Map, ExpeditionWorld.Layer, out var portal)) return;
            var delta = ExpeditionWorld.Near(new float3(portal.X, 0f, portal.Y), _camRef) - _camRef;
            float distance = math.length(delta.xz);
            if (distance > 60f) return;
            float bearing = math.degrees(math.atan2(delta.x, delta.z));
            e.Append($"<color=#9AD8FF>Up-portal to layer {s.Layer - 1}</color> {distance:0} m {Direction(Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing))}\n");
        }

        private const float ChatSeconds = 10f;

        /// <summary>Recent quick-chat lines from players nearby, and kudos given or received.</summary>
        private void WriteSocial()
        {
            float now = Time.unscaledTime;
            var map = ExpeditionWorld.Map;
            var lines = ExpeditionSocialClientSystem.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                var (sender, message, time) = lines[i];
                if (now - time > ChatSeconds) continue;
                _hud.Chat.Append($"{PlayerColors.Tag(sender)}: {Core.Social.QuickChatCatalog.Render(message, map, id => PlayerColors.Tag(id))}\n");
            }
            var kudos = ExpeditionSocialClientSystem.Kudos;
            for (int i = kudos.Count - 1; i >= 0; i--)
            {
                var (n, time) = kudos[i];
                if (now - time > ChatSeconds)
                {
                    kudos.RemoveAt(i);
                    continue;
                }
                _hud.Chat.Append(n.Outcome == Core.Social.KudosOutcome.Given
                    ? $"* {PlayerColors.Tag(n.GiverId)} gave kudos to {PlayerColors.Tag(n.ReceiverId)}\n"
                    : $"* kudos not sent ({n.Outcome})\n");
            }
        }

        private static string Direction(float relative)
        {
            float a = Mathf.Abs(relative);
            if (a < 20f) return "ahead";
            if (a > 150f) return "behind you";
            string side = relative > 0 ? "right" : "left";
            return a < 60f ? $"ahead-{side}" : $"to your {side}";
        }
    }
}
