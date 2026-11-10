using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Drawing the open-world families as robotic insects (see ArenaViewBugs): Sentinels (a blue mantis with one big
    /// eye), Divers (an orange dragonfly or wasp), roaming Bulwarks (a purple pill bug with a plate on its nose), and
    /// the gold crown over elites.
    /// </summary>
    public partial class ArenaView
    {
        private Material _sentinelEyeMat, _sentinelLidMat, _roamerPlateMat, _roamerPlateShutMat, _eliteMat;
        private readonly List<Matrix4x4> _famEye = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famLid = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famPlate = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famPlateShut = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famElite = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famTelegraph = new List<Matrix4x4>();

        private void CreateFamilyMaterials()
        {
            // A soft glow keeps the essence color readable against a backlit sky.
            _sentinelEyeMat = MakeGlow(new Color(0.55f, 0.85f, 1f), 3f);
            _sentinelLidMat = MakeMat(new Color(0.1f, 0.14f, 0.25f));
            _roamerPlateMat = MakeGlow(new Color(0.85f, 0.55f, 1f), 2.6f);
            _roamerPlateShutMat = MakeMat(new Color(0.25f, 0.15f, 0.35f));
            _eliteMat = MakeGlow(new Color(1f, 0.82f, 0.25f), 2.4f);
        }

        private void ClearFamilyLists()
        {
            _famEye.Clear(); _famLid.Clear();
            _famPlate.Clear(); _famPlateShut.Clear(); _famElite.Clear(); _famTelegraph.Clear();
        }

        /// <summary>How much bigger than its tier-1 self an enemy is drawn: smaller deeper down, bigger as an elite.</summary>
        private static float DrawScale(ArenaEnemy en) =>
            EnemyTiers.Size(en.Tier == 0 ? 1 : en.Tier) * (en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f);

        private void AddSentinel(Entity entity, ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Sentinel(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            float w = s.BodyRadius * 1.7f * k;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            // A mantis whose feet reach the ground (the mesh's are 1.88 radii down; deeper tiers are smaller but
            // stand as tall, so their legs stretch). A soaring one tucks its legs up.
            float radius = s.BodyRadius * k;
            float stretch = en.Elite == EliteKind.Soaring ? 0.6f : Mathf.Max(0.6f, s.BodyHeight / radius / 1.88f);
            var (_, speed) = BugMotion(entity, p, en.Yaw);
            DrawBug(BugFor(EnemyFamily.Sentinel, en.LookSeed), p, rot, radius, speed, en.Telegraph ? _telegraphMat : null, stretch);
            var eye = SentinelBehavior.EyeCenterOf(new System.Numerics.Vector3(p.x, p.y, p.z), en.Yaw, s);
            var eyePos = new Vector3(eye.X, eye.Y, eye.Z) + rot * Vector3.forward * (w * 0.5f - s.BodyRadius * 0.9f);
            float eyeSize = s.EyeRadius * 2f * k;
            if (en.Telegraph) _famTelegraph.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize, eyeSize, 0.12f)));
            else if (en.PlateOpen) _famEye.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize, eyeSize, 0.12f)));
            else _famLid.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize * 1.2f, eyeSize * 0.35f, 0.12f)));
        }

        private void AddDiver(Entity entity, ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Diver(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            var rot = Quaternion.Euler(-en.Pitch, en.Yaw, 0f);
            // A dragonfly or a wasp; its wings reach over the wing hit spheres.
            var (_, speed) = BugMotion(entity, p, en.Yaw);
            DrawBug(BugFor(EnemyFamily.Diver, en.LookSeed), p, rot, s.BodyRadius * k, speed, en.Telegraph ? _telegraphMat : null);
        }

        /// <summary>A Bulwark roaming the world: a smaller raid boss with its plate on one side.</summary>
        private void AddRoamingBulwark(Entity entity, ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Bulwark(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            float body = s.BodyRadius * 1.7f * k;
            // An armoured pill bug, its weak plate on its nose.
            var (_, speed) = BugMotion(entity, p, en.Yaw);
            DrawBug(BugFor(EnemyFamily.Bulwark, en.LookSeed), p, rot, s.BodyRadius * k, speed, en.Telegraph ? _telegraphMat : null);
            var plate = p + rot * Vector3.forward * (body * 0.5f + 0.06f);
            float ps = s.PlateRadius * 2f * k;
            var list = en.PlateOpen ? _famPlate : en.PlateWarn ? _famTelegraph : _famPlateShut;
            list.Add(Matrix4x4.TRS(plate, rot, new Vector3(ps, ps, 0.12f)));
        }

        /// <summary>A gold spinning crown over an elite.</summary>
        private void AddEliteMark(ArenaEnemy en, Vector3 top)
        {
            if (en.Elite == EliteKind.None) return;
            _famElite.Add(Matrix4x4.TRS(top + Vector3.up * 0.25f, Quaternion.Euler(0f, Time.time * 160f, 45f), Vector3.one * 0.28f));
        }

        private void DrawFamilies()
        {
            DrawList(_famEye, _sentinelEyeMat);
            DrawList(_famLid, _sentinelLidMat);
            DrawList(_famPlate, _roamerPlateMat);
            DrawList(_famPlateShut, _roamerPlateShutMat);
            DrawList(_famElite, _eliteMat);
            DrawList(_famTelegraph, _telegraphMat);
        }
    }
}
