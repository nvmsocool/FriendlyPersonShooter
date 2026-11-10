using System.Collections.Generic;
using FriendlyPersonShooter.Core.Avatars;
using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Enemies as robotic insects (see <see cref="BugMeshes"/>): each one built from its own seed, so no two look
    /// exactly alike. Walkers scuttle on alternating legs while they move, fliers beat their wings, and bugs without
    /// a synced facing turn to where they're going. The gameplay overlays (eyes, plates, bars) are drawn on top.
    /// </summary>
    public partial class ArenaView
    {
        /// <summary>Hit radii of the families without tier settings (match the server's defaults).</summary>
        private const float WispRadius = 0.5f, SkitterRadius = 0.6f;
        /// <summary>Mites are drawn a little bigger than their hit sphere (0.15) so swarms read from afar.</summary>
        private const float MiteDrawRadius = 0.24f;
        /// <summary>Glimmer hit radius (matches the server's).</summary>
        private const float GlimmerDrawRadius = 0.45f;
        /// <summary>Mites share a few looks: swarms are big and each bug is tiny.</summary>
        private const uint MiteLooks = 12;

        private Material _bugMat;
        private readonly Dictionary<long, BugParts> _bugParts = new Dictionary<long, BugParts>();
        private Dictionary<Entity, (Vector3 Pos, float Yaw, float Speed)> _bugMotion = new Dictionary<Entity, (Vector3, float, float)>();
        private Dictionary<Entity, (Vector3 Pos, float Yaw, float Speed)> _bugMotionNext = new Dictionary<Entity, (Vector3, float, float)>();

        private void CreateBugMaterials()
        {
            try
            {
                _bugMat = FriendlyPersonShooter.ZoneArt.ZoneLook.VertexColor("Bug", wrap: 0.45f, glow: 4f, doubleSided: true);
            }
            catch (System.ArgumentException e)
            {
                Debug.LogWarning("[Arena] Bug shader missing, enemies use the plain material: " + e.Message);
                _bugMat = MakeMat(Color.white);
            }
        }

        private static Color BugColor(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return new Color(1f, 0.85f, 0.2f);
                case EnemyFamily.Skitter: return new Color(1f, 0.18f, 0.12f);
                case EnemyFamily.Sentinel: return new Color(0.25f, 0.5f, 0.95f);
                case EnemyFamily.Diver: return new Color(1f, 0.5f, 0.1f);
                case EnemyFamily.Bulwark: return new Color(0.55f, 0.3f, 0.8f);
                case EnemyFamily.Glimmer: return new Color(1f, 0.95f, 0.35f);
                default: return new Color(0.3f, 1f, 0.35f);
            }
        }

        private BugParts BugFor(EnemyFamily family, uint seed)
        {
            if (family == EnemyFamily.Mite) seed = 1 + seed % MiteLooks;
            if (seed == 0) seed = 1;
            long key = (long)seed * 16 + (int)family;
            if (_bugParts.TryGetValue(key, out var parts)) return parts;
            if (_bugParts.Count >= 512)
            {
                // Enemies come and go; drop the old meshes now and then.
                foreach (var p in _bugParts.Values) DestroyBug(p);
                _bugParts.Clear();
            }
            parts = BugMeshes.Build(new BugLook(family, seed), BugColor(family));
            _bugParts[key] = parts;
            return parts;
        }

        private static void DestroyBug(BugParts p)
        {
            Destroy(p.Body);
            if (p.LegsA != null) Destroy(p.LegsA);
            if (p.LegsB != null) Destroy(p.LegsB);
            if (p.Wing != null) Destroy(p.Wing);
            if (p.Wing2 != null) Destroy(p.Wing2);
        }

        /// <summary>Call once per frame before drawing bugs; forgets enemies that died.</summary>
        private void BeginBugs()
        {
            var t = _bugMotion;
            _bugMotion = _bugMotionNext;
            _bugMotionNext = t;
            _bugMotionNext.Clear();
        }

        /// <summary>
        /// Tracks an enemy's movement: its heading (where it's going, or <paramref name="syncedYaw"/> when it has one)
        /// and its ground speed, smoothed.
        /// </summary>
        private (float Yaw, float Speed) BugMotion(Entity entity, Vector3 p, float? syncedYaw)
        {
            float dt = Time.deltaTime;
            float yaw = syncedYaw ?? (float)(entity.Index * 47 % 360);
            float speed = 0f;
            if (_bugMotion.TryGetValue(entity, out var prev))
            {
                yaw = syncedYaw ?? prev.Yaw;
                speed = prev.Speed;
                if (dt > 0f)
                {
                    var d = p - prev.Pos;
                    d.y = 0f;
                    float raw = d.magnitude / dt;
                    // A teleport (a blink, a wrap across the world edge) isn't movement.
                    if (raw > 30f) raw = 0f;
                    speed = Mathf.Lerp(prev.Speed, raw, 1f - Mathf.Exp(-dt * 8f));
                    if (syncedYaw == null && raw > 0.3f)
                    {
                        float target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        yaw = Mathf.MoveTowardsAngle(prev.Yaw, target, 360f * dt);
                    }
                }
            }
            _bugMotionNext[entity] = (p, yaw, speed);
            return (yaw, speed);
        }

        /// <summary>
        /// Draws one bug with its hit sphere's centre at <paramref name="p"/>, <paramref name="radius"/> its hit radius.
        /// <paramref name="material"/> overrides the vertex colours (a telegraph flash, a Wisp between blinks).
        /// <paramref name="legStretch"/> stretches the legs down (a Sentinel stands taller than its radius says).
        /// </summary>
        private void DrawBug(BugParts parts, Vector3 p, Quaternion rot, float radius, float speed, Material material, float legStretch = 1f)
        {
            var look = parts.Look;
            var mat = material != null ? material : _bugMat;
            var rp = new RenderParams(mat);
            float t = Time.time;
            var scale = Vector3.one * radius;
            var m = Matrix4x4.TRS(p, rot, scale);
            Graphics.RenderMesh(rp, parts.Body, 0, m);
            if (parts.LegsA != null)
            {
                // Scuttle: swap between the two leg poses, faster when moving faster; stand still on pose A.
                bool b = speed > 0.25f && Mathf.Repeat(t * look.GaitSpeed * (3f + speed * 1.5f) + look.Phase, 2f) >= 1f;
                var lm = Matrix4x4.TRS(p, rot, new Vector3(radius, radius * legStretch, radius));
                Graphics.RenderMesh(rp, b ? parts.LegsB : parts.LegsA, 0, lm);
            }
            if (parts.Wing != null && parts.WingRoots != null)
            {
                var family = parts.Look.Family;
                float rate = family == EnemyFamily.Wisp ? 9f : family == EnemyFamily.Glimmer ? 7f : 22f;
                // Butterflies beat slow and wide, insects fast and small.
                float swing = family == EnemyFamily.Glimmer ? 45f : 28f;
                for (int i = 0; i < parts.WingRoots.Length; i++)
                {
                    // Pairs beat a little out of step, like a dragonfly's.
                    float flap = Mathf.Sin(t * rate * look.GaitSpeed + look.Phase + i * 1.3f) * swing + 8f;
                    var root = parts.WingRoots[i];
                    var wing = i > 0 && parts.Wing2 != null ? parts.Wing2 : parts.Wing;
                    // A butterfly's forewing sweeps forward, its hindwing back.
                    float sweep = family == EnemyFamily.Glimmer ? (i == 0 ? 25f : -30f) : 0f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        // The left wing is the right one mirrored.
                        var at = new Vector3(root.x * s, root.y, root.z);
                        Graphics.RenderMesh(rp, wing, 0, m * Matrix4x4.TRS(at, Quaternion.Euler(0f, -s * sweep, s * flap), new Vector3(s, 1f, 1f)));
                    }
                }
            }
        }

        /// <summary>A small walker or flier (Wisp, Skitter, Mite, Glimmer) at <paramref name="p"/>; returns its drawn size for bars.</summary>
        private float DrawSmallBug(Entity entity, ArenaEnemy en, Vector3 p, float scale)
        {
            var (yaw, speed) = BugMotion(entity, p, null);
            var parts = BugFor(en.Family, en.LookSeed);
            float radius;
            Material mat = en.Telegraph ? _telegraphMat : null;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            switch (en.Family)
            {
                case EnemyFamily.Wisp:
                    radius = WispRadius * scale;
                    if (!en.Hittable && !en.Telegraph)
                    {
                        // Between blinks: a small grey ghost of itself.
                        radius *= 0.6f;
                        mat = _wispDimMat;
                    }
                    // A gentle bob and a tilt into its flight.
                    p += Vector3.up * (Mathf.Sin(Time.time * 2.2f + parts.Look.Phase) * 0.06f);
                    rot *= Quaternion.Euler(Mathf.Clamp(speed * 4f, 0f, 20f), 0f, 0f);
                    break;
                case EnemyFamily.Skitter:
                    radius = SkitterRadius * scale;
                    break;
                case EnemyFamily.Glimmer:
                    // Hovers in place, turning slowly and bobbing, wings beating.
                    radius = GlimmerDrawRadius;
                    rot = Quaternion.Euler(-15f, Time.time * 40f + parts.Look.Phase * Mathf.Rad2Deg, 0f);
                    p += Vector3.up * (Mathf.Sin(Time.time * 3f + parts.Look.Phase) * 0.08f);
                    break;
                default:
                    radius = MiteDrawRadius * scale;
                    // Mites jitter about.
                    rot *= Quaternion.Euler(0f, Mathf.Sin(Time.time * 7f + parts.Look.Phase) * 25f, 0f);
                    speed = 1f;
                    break;
            }
            DrawBug(parts, p, rot, radius, speed, mat);
            return radius * 2f;
        }
    }
}
