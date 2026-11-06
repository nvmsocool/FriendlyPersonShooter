using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The strange rooms of a raid run on the server (Docs/RaidDevPlan.md, phase 3): Lanterns (shades can
    /// only be hurt near a player an ally lit up), Sentinel Eye (hit the eye while it's open on the turning
    /// shell), and the Weaver's falling floor and threads.
    /// </summary>
    public partial class ArenaServerSystem
    {
        private int _eyeHits, _tetherHits, _weaverFalls;
        private readonly List<int> _upIds = new List<int>();
        private readonly List<WeaverTether> _snapped = new List<WeaverTether>();

        // ---------- Lanterns ----------

        private bool InLanterns => _run != null && !_run.AtBoss && _run.Room == RaidRoomKind.Lanterns && _run.Lanterns != null;

        /// <summary>Lanterns: shades (Skitters) trickle in, one at a time, up to the room's cap.</summary>
        private void KeepShadesUp()
        {
            if (_time < _nextRoomAdds) return;
            if (RaidEnemyCount() >= _run.Lanterns.MaxAlive * _run.AddsMultiplier) return;
            _nextRoomAdds = _time + 1.5;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var spots = _run.Layout.AddSpots;
            var at = RaidArena.FromRoom(RaidRoomKind.Lanterns, spots[random.NextInt(spots.Count)]);
            _spawned.Clear();
            SpawnSkitter(new Vector3(at.x, at.y + _skitter.BodyRadius, at.z));
            foreach (var s in _spawned) s.Raid = true;
            _spawned.Clear();
        }

        /// <summary>Lanterns: a helpful hit on an ally lights them up.</summary>
        private void LanternLit(int targetId)
        {
            if (InLanterns && !_run.Solved && InsideRaid(targetId)) _run.Lanterns.Lit(targetId, _time);
        }

        /// <summary>Lanterns: true if a hit on this raid enemy lands in the dark (it does nothing).</summary>
        private bool ShadeInDark(Entity enemyEntity, EnemyState enemy)
        {
            if (!enemy.Raid || !InLanterns || _run.Solved) return false;
            var p = RaidArena.ToRoom(RaidRoomKind.Lanterns, EntityManager.GetComponentData<LocalTransform>(enemyEntity).Position);
            if (_run.Lanterns.InLight(p, _roomPlayers, _time)) return false;
            _run.Lanterns.HitInDark();
            return true;
        }

        /// <summary>Seconds of glow left on a player (for the light the clients draw around them).</summary>
        private float LanternGlow(int id) => InLanterns && !_run.Solved && InsideRaid(id) ? _run.Lanterns.GlowLeft(id, _time) : 0f;

        /// <summary>Players' gravity: lower inside a Low Gravity run.</summary>
        private float RaidGravity(int id) => _run != null && InsideRaid(id) ? _run.GravityMultiplier : 1f;

        // ---------- Sentinel Eye ----------

        /// <summary>The shell fires a bolt at someone it can see.</summary>
        private void EyeBolt(EntityCommandBuffer ecb)
        {
            var center = RaidArena.FromRoom(RaidRoomKind.SentinelEye, SentinelEye.ShellCenter);
            _upIds.Clear();
            foreach (var p in _roomPlayers) if (p.Up) _upIds.Add(p.Id);
            if (_upIds.Count == 0) return;
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            int first = random.NextInt(_upIds.Count);
            for (int k = 0; k < _upIds.Count; k++)
            {
                int id = _upIds[(first + k) % _upIds.Count];
                var chest = ToFloat3(PlayerFeet(id)) + new float3(0f, 1.1f, 0f);
                var dir = math.normalize(chest - center);
                var from = center + dir * (SentinelEye.ShellRadius + 0.4f);
                if (ArenaLayout.RaycastCover(from, dir, math.distance(from, chest)) != float.MaxValue) continue;
                float speed = 20f * _run.BoltSpeedMultiplier;
                FireBolt(ecb, EnemyFamily.Bulwark, ToVec(from), ToVec(chest), speed, 0.35f, SentinelEye.BoltDamage, 60f);
                return;
            }
        }

        /// <summary>Where a ray first meets a sphere (distance along it), or -1.</summary>
        private static float RaySphere(float3 origin, float3 dir, float3 center, float radius)
        {
            var oc = center - origin;
            float along = math.dot(oc, dir);
            float miss2 = math.lengthsq(oc - dir * along);
            if (miss2 > radius * radius) return -1f;
            float d = along - math.sqrt(radius * radius - miss2);
            return d < 0f ? (along >= 0f ? 0f : -1f) : d;
        }

        /// <summary>
        /// Sentinel Eye: the shell stops a shot (shortening <paramref name="distance"/>); returns 0 if it hits the
        /// open eye first, else -1.
        /// </summary>
        private int EyeOnShot(float3 eye, float3 dir, ref float distance)
        {
            var room = RaidRoomKind.SentinelEye;
            var center = RaidArena.FromRoom(room, SentinelEye.ShellCenter);
            float shell = RaySphere(eye, dir, center, SentinelEye.ShellRadius);
            float before = distance;
            if (shell >= 0f && shell < distance) distance = shell;
            if (!_run.Eye.IsOpen) return -1;
            var at = RaidArena.FromRoom(room, SentinelEye.EyeAt(_run.Eye.Azimuth, _run.Eye.Elevation, _run.StageTime(_time)));
            float hit = RaySphere(eye, dir, at, SentinelEye.EyeRadius);
            // The eye bulges out of the shell, so it's hit first from its own side only.
            if (hit < 0f || hit > before || hit > distance + 0.25f) return -1;
            distance = hit;
            return 0;
        }

        // ---------- the Weaver ----------

        /// <summary>The Weaver's body (the raid's Bulwark), or the arena's centre if it's gone.</summary>
        private float3 WeaverPosition()
        {
            foreach (var s in _enemies.Values) if (s.Raid && s.Family == EnemyFamily.Bulwark && !s.WorldBoss) return ToFloat3(s.Boss.Position);
            return RaidArena.Center + new float3(0f, 3f, 0f);
        }

        /// <summary>A thread's two ends (chests, or the Weaver's body), pulled in a little so the bodies stay hittable.</summary>
        private void TetherEnds(WeaverTether t, out float3 a, out float3 b)
        {
            a = ToFloat3(PlayerFeet(t.A)) + new float3(0f, 1.1f, 0f);
            b = t.B == WeaverTether.WeaverEnd ? WeaverPosition() : ToFloat3(PlayerFeet(t.B)) + new float3(0f, 1.1f, 0f);
            var ab = b - a;
            float len = math.length(ab);
            if (len < 1.5f) return;
            var u = ab / len;
            a += u * 0.6f;
            b -= u * (t.B == WeaverTether.WeaverEnd ? 3f : 0.6f);
        }

        /// <summary>The Weaver's threads: the nearest one a shot passes through (100 + its index), or -1.</summary>
        private int TetherOnShot(float3 eye, float3 dir, ref float distance)
        {
            int best = -1;
            var tethers = _run.Weaver.Tethers;
            for (int i = 0; i < tethers.Count; i++)
            {
                TetherEnds(tethers[i], out var a, out var b);
                if (!RayNearSegment(eye, dir, distance, a, b, RaidWeaver.TetherRadius, out float along)) continue;
                distance = along;
                best = 100 + i;
            }
            return best;
        }

        /// <summary>True if the ray passes within <paramref name="radius"/> of segment ab before <paramref name="maxAlong"/>.</summary>
        private static bool RayNearSegment(float3 o, float3 d, float maxAlong, float3 a, float3 b, float radius, out float along)
        {
            along = 0f;
            var u = b - a;
            var w = o - a;
            float uu = math.dot(u, u), ud = math.dot(u, d), uw = math.dot(u, w), dw = math.dot(d, w);
            float denom = uu - ud * ud;
            float s = denom > 1e-6f ? math.saturate((uw - ud * dw) / denom) : 0f;
            // Closest point on the segment, then on the ray to it.
            var p = a + u * s;
            float t = math.dot(p - o, d);
            if (t < 0f || t > maxAlong) return false;
            float miss = math.distance(o + d * t, p);
            if (miss > radius) return false;
            along = math.max(0f, t - 0.05f);
            return true;
        }

        /// <summary>The Weaver's stage: its floor, its threads, and those who fell through.</summary>
        private void StepWeaver(EntityCommandBuffer ecb)
        {
            var w = _run.Weaver;
            if (w.StepFloor(_time)) RaidArena.SetBossFloor(w.Fallen);
            _upIds.Clear();
            foreach (var p in _roomPlayers) if (p.Up) _upIds.Add(p.Id);
            _snapped.Clear();
            int tied = w.StepTethers(_time, _upIds, _snapped);
            if (tied > 0)
            {
                _announce.Add((AnnounceKind.TetherTied, tied, 0, 0));
                Debug.Log($"[Raid] the Weaver tied {tied} thread(s)");
            }
            foreach (var t in _snapped)
            {
                HurtPlayer(ecb, t.A, EnemyFamily.Bulwark, RaidWeaver.TetherDamage, PlayerFeet(t.A), 0);
                if (t.B != WeaverTether.WeaverEnd) HurtPlayer(ecb, t.B, EnemyFamily.Bulwark, RaidWeaver.TetherDamage, PlayerFeet(t.B), 0);
                _announce.Add((AnnounceKind.TetherSnapped, 0, t.A, t.B == WeaverTether.WeaverEnd ? 0 : t.B));
            }
        }

        /// <summary>Someone fell through the Weaver's floor: back up onto the nearest safe tile.</summary>
        private float3 WeaverLanding(float3 pos)
        {
            var local = RaidArena.ToRoom(RaidRoomKind.Boss, pos);
            int tile = _run.Weaver.NearestSafeTile(local);
            var c = tile >= 0 ? RaidWeaver.TileCenter(tile) : Vector3.Zero;
            return RaidArena.Center + new float3(c.X, 0.05f, c.Z);
        }

        // ---------- shots ----------

        /// <summary>
        /// A shot reached a room target (crystal, rune, the eye, a thread). Returns 1 to show a hit marker,
        /// 2 for a refused one, 0 for none.
        /// </summary>
        private int StrangeHit(int target, int shooterId)
        {
            if (_run.AtBoss && _run.Weaver != null && target >= 100)
            {
                int index = target - 100;
                if (index >= _run.Weaver.Tethers.Count) return 0;
                var hit = _run.Weaver.HitTether(index, shooterId);
                if (hit == TetherHit.Refused) return 2;
                _tetherHits++;
                if (hit == TetherHit.Cut) _announce.Add((AnnounceKind.TetherCut, 0, shooterId, 0));
                return 1;
            }
            if (_run.Room == RaidRoomKind.SentinelEye)
            {
                if (!_run.Eye.Hit(_time)) return 0;
                _eyeHits++;
                return 1;
            }
            return 0;
        }

        private string StrangeLog() =>
            (_run?.Lanterns != null ? $" lanternKills={_run.Lanterns.Kills}/{_run.Lanterns.Target} lightings={_run.Lanterns.Lightings} darkHits={_run.Lanterns.DarkHits}" : "") +
            (_run?.Eye != null ? $" eyeHits={_run.Eye.Hits}/{_run.Eye.Target} eyeMisses={_run.Eye.Misses}" : "") +
            (_run?.Weaver != null ? $" tilesFallen={_run.Weaver.TilesFallen} threads={_run.Weaver.Tethers.Count} tied={_run.Weaver.TethersTied} " +
                                    $"cut={_run.Weaver.TethersCut} snapped={_run.Weaver.TethersSnapped} weaverFalls={_weaverFalls}" : "") +
            $" eyeShots={_eyeHits} threadHits={_tetherHits}";
    }
}
