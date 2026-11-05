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
    /// shrines, the gate, dropped caches, a beacon on the current objective, and the expedition HUD.
    /// The world wraps, so everything is drawn at its copy nearest the camera.
    /// </summary>
    public partial class ArenaView
    {
        private Material[] _tileMats;
        private Material _landmarkMat, _shrineMat, _gateMat, _gateOpenMat, _cacheMat, _beaconMat, _worldBossBeaconMat;
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
            _gateMat = MakeMat(new Color(0.75f, 0.6f, 0.2f));
            _gateOpenMat = MakeMat(new Color(1f, 0.95f, 0.4f));
            _cacheMat = MakeMat(new Color(1f, 0.85f, 0.1f));
            _beaconMat = MakeMat(new Color(1f, 1f, 0.6f));
            _worldBossBeaconMat = MakeMat(new Color(1f, 0.55f, 0.15f));
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
            var map = ExpeditionWorld.Map;
            float zoneSize = map.Grid.ZoneSize;
            var camZone = map.Grid.ZoneAt(_camRef.x, _camRef.z);

            bool terrain = ExpeditionTerrain.Active;
            var zones = terrain ? ExpeditionTerrain.World : null;
            if (zones != null && zones.ReadyCount != _groundCacheZones)
            {
                _groundCache.Clear();
                _groundCacheZones = zones.ReadyCount;
            }

            // Floors for the zones around the camera, a little apart so zone edges show
            // (on generated ground: only where a zone hasn't been built yet).
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var zone = map.Grid.Wrap(camZone.X + dx, camZone.Y + dz);
                    if (zones != null && zones.IsReady(zone)) continue;
                    var centre = ExpeditionWorld.ToFloat3(map.ZoneCentre(zone), -0.25f);
                    _x.Clear();
                    _x.Add(Matrix4x4.TRS(Near(centre), Quaternion.identity, new Vector3(zoneSize - 1f, 0.5f, zoneSize - 1f)));
                    DrawList(_x, _tileMats[(int)map.Zone(zone).Tile % _tileMats.Length]);
                }
            }

            float range = zoneSize * 1.6f;
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

            // A tall thin beacon over the current objective.
            if (status.HasTarget && !status.Complete)
            {
                var p = Near(new float3(status.Target.x, GroundAt(new System.Numerics.Vector2(status.Target.x, status.Target.y)), status.Target.y));
                _x.Clear();
                _x.Add(Matrix4x4.TRS(p + Vector3.up * 30f, Quaternion.identity, new Vector3(0.4f, 60f, 0.4f)));
                DrawList(_x, _beaconMat);
            }

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

        private void WriteExpeditionHud()
        {
            if (!TryGetStatus(out var s)) return;
            int minutes = (int)(s.Elapsed / 60f), seconds = (int)(s.Elapsed % 60f);
            var e = _hud.Expedition;
            e.Append($"<b>EXPEDITION</b>   {minutes:00}:{seconds:00} / 20:00\n");
            // Every color at a glance: carried in bold, then the party bank (the gate's price after Yellow).
            e.Append($"<color=#FFE14D>Yellow</color> <b>{s.Carried}</b> +{s.Banked}/{s.Goal}   " +
                     $"<color=#FF6B5B>Red</color> <b>{s.CarriedRed}</b> +{s.BankedRed}   " +
                     $"<color=#7BE07B>Green</color> <b>{s.CarriedGreen}</b> +{s.BankedGreen}   <color=#B8B0D8>carried +banked</color>");
            if (s.Kudos > 0) e.Append($"   kudos {s.Kudos}");
            e.Append($"\n{s.Objective}\n");
            WriteRaidHud(s, e);
            WriteWorldBossHud(s, e);
            if (s.HasTarget && !s.Complete)
            {
                var delta = ExpeditionWorld.Near(new float3(s.Target.x, 0f, s.Target.y), _camRef) - _camRef;
                float distance = math.length(delta.xz);
                float bearing = math.degrees(math.atan2(delta.x, delta.z));
                float relative = Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing);
                e.Append($"<color=#FFE14D>{distance:0} m {Direction(relative)}</color>  (follow the light beam)\n");
            }
            if (s.BloomNear) e.Append("<color=#FFE14D>A Yellow BLOOM is running nearby: lots of Wisps!</color>\n");
            e.Append($"<color=#B8B0D8>You are {s.Location}</color>");
            WriteSocial();
            if (s.Complete)
            {
                _center.text = $"EXPEDITION COMPLETE in {minutes:00}:{seconds:00}!";
            }
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
