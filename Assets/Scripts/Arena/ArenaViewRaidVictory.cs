using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The end of a won raid: the boss blows apart (a flash, a shockwave, flying armor and a light), then the exit
    /// portal stands open on the boss platform until everyone has gone home through it.
    /// </summary>
    public partial class ArenaView
    {
        private const float ExplosionSeconds = 4f;
        private const int Debris = 48;

        private bool _wonSeen;
        private float _explodedAt = -100f;
        private Vector3 _explodedFrom;
        private Material _blastCoreMat, _blastHotMat, _debrisMat, _debrisGlowMat;
        private Light _blastLight;
        private readonly List<Matrix4x4> _vCore = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _vHot = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _vDebris = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _vDebrisGlow = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _vPortal = new List<Matrix4x4>();
        private readonly Vector3[] _debrisDir = new Vector3[Debris];
        private readonly float[] _debrisSize = new float[Debris];

        private static bool RaidWon(ExpeditionStatus s) =>
            s.InRaid && s.RaidStageCount > 0 && s.RaidStage >= s.RaidStageCount - 1 && s.RaidRoom == RaidRoomKind.Boss && s.RaidPortal > -1.5f;

        private void DrawVictory(ExpeditionStatus s)
        {
            bool won = RaidWon(s);
            if (won && !_wonSeen) Explode();
            _wonSeen = won;
            DrawExplosion();
            if (!won) return;
            if (_portalMat == null) CreateRoomMaterials();
            _vPortal.Clear();
            AddPortal(_vPortal, Near(RaidArena.RoomCenter(RaidRoomKind.Boss)), RaidRoomLayout.For(RaidRoomKind.Boss).Portal, 90f, Time.time);
            DrawList(_vPortal, _portalMat);
        }

        private void Explode()
        {
            if (_blastCoreMat == null)
            {
                _blastCoreMat = MakeGlow(new Color(1f, 0.95f, 0.8f), 4f);
                _blastHotMat = MakeGlow(new Color(1f, 0.55f, 0.15f), 3f);
                _debrisMat = MakeMat(new Color(0.3f, 0.24f, 0.38f));
                _debrisGlowMat = MakeGlow(RaidViolet, 2.2f);
            }
            _explodedAt = Time.time;
            _explodedFrom = _bossSeen ? _bossPos : Near(RaidArena.Center) + new Vector3(0f, 4f, 0f);
            var random = new System.Random(Mathf.RoundToInt(_explodedAt * 1000f));
            for (int i = 0; i < Debris; i++)
            {
                // Mostly outward and up, some flat along the floor.
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float up = (float)(0.2f + random.NextDouble() * 1.1f);
                float speed = (float)(10f + random.NextDouble() * 18f);
                _debrisDir[i] = new Vector3(Mathf.Sin(a), up, Mathf.Cos(a)).normalized * speed;
                _debrisSize[i] = (float)(0.35f + random.NextDouble() * 1.1f);
            }
            if (_blastLight == null)
            {
                var go = new GameObject("Boss explosion light");
                go.transform.SetParent(transform, false);
                _blastLight = go.AddComponent<Light>();
                _blastLight.type = LightType.Point;
                _blastLight.color = new Color(1f, 0.75f, 0.45f);
                _blastLight.range = 70f;
                _blastLight.shadows = LightShadows.None;
            }
            ArenaAudio.BossExplosion();
        }

        private void DrawExplosion()
        {
            float age = Time.time - _explodedAt;
            bool live = age >= 0f && age < ExplosionSeconds;
            if (_blastLight != null)
            {
                _blastLight.enabled = live;
                if (live)
                {
                    _blastLight.transform.position = _explodedFrom;
                    _blastLight.intensity = 60f * Mathf.Exp(-age * 2.2f);
                }
            }
            if (!live) return;
            _vCore.Clear();
            _vHot.Clear();
            _vDebris.Clear();
            _vDebrisGlow.Clear();
            var c = _explodedFrom;

            // The fireball: a white core and an orange shell that swell fast and then collapse.
            float swell = Mathf.Clamp01(age / 0.35f);
            float collapse = Mathf.Clamp01((age - 0.6f) / 1.2f);
            float core = 16f * swell * (1f - collapse);
            float shell = 22f * Mathf.Sqrt(swell) * (1f - collapse * collapse);
            if (core > 0.05f)
            {
                Box(_vCore, c, Quaternion.Euler(age * 90f, age * 140f, 0f), Vector3.one * core);
                Box(_vCore, c, Quaternion.Euler(45f, 45f + age * 60f, 45f), Vector3.one * core * 0.8f);
            }
            if (shell > 0.05f)
            {
                for (int k = 0; k < 6; k++)
                {
                    var spin = Quaternion.Euler(k * 30f + age * 50f, k * 60f - age * 70f, k * 15f);
                    Box(_vHot, c, spin, new Vector3(shell, shell * 0.35f, shell * 0.35f));
                }
            }

            // A shockwave running out along the floor.
            float floor = Near(RaidArena.Center).y + 0.15f;
            float ring = Mathf.Clamp01(age / 1.4f);
            if (ring < 1f)
            {
                float radius = 3f + 45f * ring;
                const int segs = 40;
                float segLength = 2f * Mathf.PI * radius / segs * 1.1f;
                for (int k = 0; k < segs; k++)
                {
                    float a = 2f * Mathf.PI * k / segs;
                    var p = new Vector3(c.x + Mathf.Sin(a) * radius, floor, c.z + Mathf.Cos(a) * radius);
                    Box(_vHot, p, Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f), new Vector3(segLength, 0.6f * (1f - ring) + 0.1f, 0.5f));
                }
            }

            // Armor plates flying out and falling, a third of them still glowing.
            for (int i = 0; i < Debris; i++)
            {
                var v = _debrisDir[i];
                var p = c + v * age + Vector3.down * (6f * age * age);
                if (p.y < floor - 30f) continue;
                var rot = Quaternion.Euler(age * 300f + i * 37f, age * 220f + i * 53f, i * 11f);
                Box(i % 3 == 0 ? _vDebrisGlow : _vDebris, p, rot, new Vector3(_debrisSize[i], _debrisSize[i] * 0.4f, _debrisSize[i] * 0.8f));
            }

            DrawList(_vCore, _blastCoreMat);
            DrawList(_vHot, _blastHotMat);
            DrawList(_vDebris, _debrisMat);
            DrawList(_vDebrisGlow, _debrisGlowMat);
        }
    }
}
