using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Drawing the open-world families: Sentinels (a blue pillar with one eye), Divers (an orange dart with two
    /// glowing wings), roaming Bulwarks (a purple shell with a plate), and the gold crown over elites.
    /// </summary>
    public partial class ArenaView
    {
        private Material _sentinelMat, _sentinelEyeMat, _sentinelLidMat, _diverMat, _diverWingMat, _roamerMat, _roamerPlateMat, _roamerPlateShutMat, _eliteMat;
        private readonly List<Matrix4x4> _famSentinel = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famEye = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famLid = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famDiver = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famWing = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famRoamer = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famPlate = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famPlateShut = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famElite = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _famTelegraph = new List<Matrix4x4>();

        private void CreateFamilyMaterials()
        {
            // A soft glow keeps the essence color readable against a backlit sky.
            _sentinelMat = MakeGlow(new Color(0.25f, 0.5f, 0.95f), 0.55f);
            _sentinelEyeMat = MakeGlow(new Color(0.55f, 0.85f, 1f), 3f);
            _sentinelLidMat = MakeMat(new Color(0.1f, 0.14f, 0.25f));
            _diverMat = MakeGlow(new Color(1f, 0.5f, 0.1f), 1.2f);
            _diverWingMat = MakeGlow(new Color(1f, 0.8f, 0.3f), 2.2f);
            _roamerMat = MakeGlow(new Color(0.55f, 0.3f, 0.8f), 0.5f);
            _roamerPlateMat = MakeGlow(new Color(0.85f, 0.55f, 1f), 2.6f);
            _roamerPlateShutMat = MakeMat(new Color(0.25f, 0.15f, 0.35f));
            _eliteMat = MakeGlow(new Color(1f, 0.82f, 0.25f), 2.4f);
        }

        private void ClearFamilyLists()
        {
            _famSentinel.Clear(); _famEye.Clear(); _famLid.Clear(); _famDiver.Clear(); _famWing.Clear();
            _famRoamer.Clear(); _famPlate.Clear(); _famPlateShut.Clear(); _famElite.Clear(); _famTelegraph.Clear();
        }

        /// <summary>How much bigger than its tier-1 self an enemy is drawn: smaller deeper down, bigger as an elite.</summary>
        private static float DrawScale(ArenaEnemy en) =>
            EnemyTiers.Size(en.Tier == 0 ? 1 : en.Tier) * (en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f);

        private void AddSentinel(ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Sentinel(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            float w = s.BodyRadius * 1.7f * k;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            // A pillar standing on the ground (a soaring one is a shorter block); the body centre is near its top.
            float height = en.Elite == EliteKind.Soaring ? w * 1.2f : s.BodyHeight + 0.5f;
            _famSentinel.Add(Matrix4x4.TRS(p + Vector3.down * (height * 0.5f - 0.5f), rot, new Vector3(w, height, w)));
            var eye = SentinelBehavior.EyeCenterOf(new System.Numerics.Vector3(p.x, p.y, p.z), en.Yaw, s);
            var eyePos = new Vector3(eye.X, eye.Y, eye.Z) + rot * Vector3.forward * (w * 0.5f - s.BodyRadius * 0.9f);
            float eyeSize = s.EyeRadius * 2f * k;
            if (en.Telegraph) _famTelegraph.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize, eyeSize, 0.12f)));
            else if (en.PlateOpen) _famEye.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize, eyeSize, 0.12f)));
            else _famLid.Add(Matrix4x4.TRS(eyePos, rot, new Vector3(eyeSize * 1.2f, eyeSize * 0.35f, 0.12f)));
        }

        private void AddDiver(ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Diver(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            var rot = Quaternion.Euler(-en.Pitch, en.Yaw, 0f);
            float body = s.BodyRadius * 2f * k;
            (en.Telegraph ? _famTelegraph : _famDiver).Add(Matrix4x4.TRS(p, rot, new Vector3(body * 0.7f, body * 0.5f, body * 1.6f)));
            var right = Quaternion.Euler(0f, en.Yaw, 0f) * Vector3.right;
            float wing = s.WingRadius * 2f * k;
            float flap = Mathf.Sin(Time.time * 14f) * 18f;
            foreach (int side in new[] { -1, 1 })
            {
                var at = p + right * (side * s.WingSpan);
                _famWing.Add(Matrix4x4.TRS(at, rot * Quaternion.Euler(0f, 0f, side * flap), new Vector3(wing, wing * 0.25f, wing)));
            }
        }

        /// <summary>A Bulwark roaming the world: a smaller raid boss with its plate on one side.</summary>
        private void AddRoamingBulwark(ArenaEnemy en, Vector3 p)
        {
            var s = EnemyTiers.Bulwark(en.Tier == 0 ? 1 : en.Tier);
            float k = en.Elite != EliteKind.None ? Elites.SizeMultiplier : 1f;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            float body = s.BodyRadius * 1.7f * k;
            (en.Telegraph ? _famTelegraph : _famRoamer).Add(Matrix4x4.TRS(p, rot, Vector3.one * body));
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
            DrawList(_famSentinel, _sentinelMat);
            DrawList(_famEye, _sentinelEyeMat);
            DrawList(_famLid, _sentinelLidMat);
            DrawList(_famDiver, _diverMat);
            DrawList(_famWing, _diverWingMat);
            DrawList(_famRoamer, _roamerMat);
            DrawList(_famPlate, _roamerPlateMat);
            DrawList(_famPlateShut, _roamerPlateShutMat);
            DrawList(_famElite, _eliteMat);
            DrawList(_famTelegraph, _telegraphMat);
        }
    }
}
