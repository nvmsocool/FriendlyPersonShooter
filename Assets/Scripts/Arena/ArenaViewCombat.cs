using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>The combat half of the arena view: enemies, tracers, health bars, hit markers and the HUD.</summary>
    public partial class ArenaView
    {
        private const float TracerSeconds = 0.08f;
        private const float MarkerSeconds = 0.25f;

        private Material _wispMat, _wispDimMat, _skitterMat, _coreMat, _miteMat, _telegraphMat;
        private Material _vigorMat, _mightMat, _barBackMat, _barHealthMat, _barShieldMat, _witherMat, _exposeMat;
        private EntityQuery _enemies, _vitals;
        private readonly List<Matrix4x4> _a = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _b = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _c = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _d = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _e = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _f = new List<Matrix4x4>();
        private readonly List<(Vector3 From, Vector3 To, PayloadKind Payload, float Time)> _localTracers =
            new List<(Vector3, Vector3, PayloadKind, float)>();
        private float _nextLocalShot;

        private void CreateCombatMaterials()
        {
            _wispMat = MakeMat(new Color(1f, 0.9f, 0.2f));
            _wispDimMat = MakeMat(new Color(0.45f, 0.42f, 0.3f));
            _skitterMat = MakeMat(new Color(0.85f, 0.15f, 0.12f));
            _coreMat = MakeMat(new Color(1f, 0.95f, 0.8f));
            _miteMat = MakeMat(new Color(0.3f, 0.9f, 0.3f));
            _telegraphMat = MakeMat(new Color(1f, 0.55f, 0.1f));
            _vigorMat = MakeMat(new Color(0.55f, 1f, 0.6f));
            _mightMat = MakeMat(new Color(1f, 0.4f, 0.2f));
            _barBackMat = MakeMat(new Color(0.1f, 0.1f, 0.1f));
            _barHealthMat = MakeMat(new Color(0.3f, 0.95f, 0.35f));
            _barShieldMat = MakeMat(new Color(0.4f, 0.85f, 1f));
            _witherMat = MakeMat(new Color(0.6f, 0.2f, 0.8f));
            _exposeMat = MakeMat(new Color(1f, 0.6f, 0f));
        }

        private void CreateCombatQueries(EntityManager em)
        {
            _enemies = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaEnemy>(), ComponentType.ReadOnly<LocalTransform>());
            _vitals = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ArenaVitals>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<ArenaLoadout>());
        }

        private void DrawCombat(World world, Entity local)
        {
            var camRot = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
            var barRot = Quaternion.Euler(0f, camRot.eulerAngles.y, 0f);
            DrawEnemies(barRot);
            DrawAllyBars(world, local, barRot);
            DrawTracers(world, local);
        }

        private void DrawEnemies(Quaternion barRot)
        {
            using var enemies = _enemies.ToComponentDataArray<ArenaEnemy>(Allocator.Temp);
            using var transforms = _enemies.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            _a.Clear(); _b.Clear(); _c.Clear(); _d.Clear(); _e.Clear(); _f.Clear();
            var cores = new List<Matrix4x4>();
            var mites = new List<Matrix4x4>();
            var withered = new List<Matrix4x4>();
            var exposed = new List<Matrix4x4>();
            for (int i = 0; i < enemies.Length; i++)
            {
                var en = enemies[i];
                Vector3 p = transforms[i].Position;
                float size;
                switch (en.Family)
                {
                    case EnemyFamily.Wisp:
                        size = 0.7f;
                        var wm = Matrix4x4.TRS(p, Quaternion.Euler(45f, Time.time * 90f, 45f), Vector3.one * (en.Hittable ? size : size * 0.6f));
                        (en.Telegraph ? _f : en.Hittable ? _a : _b).Add(wm);
                        break;
                    case EnemyFamily.Skitter:
                        size = 1.2f;
                        (en.Telegraph ? _f : _c).Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(size, 0.9f, size)));
                        cores.Add(Matrix4x4.TRS(p + Vector3.up * 0.1f, Quaternion.identity, Vector3.one * 0.4f));
                        break;
                    default:
                        size = 0.3f;
                        (en.Telegraph ? _f : mites).Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * size));
                        break;
                }

                if (en.Family != EnemyFamily.Mite)
                {
                    AddBar(p + Vector3.up * (size * 0.5f + 0.5f), barRot, 1f, en.Health / math.max(en.MaxHealth, 1f), 0f, _d, _e, null);
                }
                Vector3 top = p + Vector3.up * (size * 0.5f + 0.75f);
                if (en.Withered) withered.Add(Matrix4x4.TRS(top + barRot * Vector3.left * 0.2f, barRot, Vector3.one * 0.15f));
                if (en.Exposed) exposed.Add(Matrix4x4.TRS(top + barRot * Vector3.right * 0.2f, barRot, Vector3.one * 0.15f));
            }
            DrawList(_a, _wispMat);
            DrawList(_b, _wispDimMat);
            DrawList(_c, _skitterMat);
            DrawList(cores, _coreMat);
            DrawList(mites, _miteMat);
            DrawList(_f, _telegraphMat);
            DrawList(_d, _barBackMat);
            DrawList(_e, _barHealthMat);
            DrawList(withered, _witherMat);
            DrawList(exposed, _exposeMat);
        }

        /// <summary>A flat bar facing the camera: background, health fill and an optional shield fill on top.</summary>
        private static void AddBar(Vector3 center, Quaternion rot, float width, float health, float shield,
            List<Matrix4x4> back, List<Matrix4x4> fill, List<Matrix4x4> shieldFill)
        {
            health = Mathf.Clamp01(health);
            back.Add(Matrix4x4.TRS(center, rot, new Vector3(width + 0.06f, 0.12f, 0.02f)));
            if (health > 0f)
            {
                var c = center + rot * Vector3.left * ((1f - health) * width * 0.5f) - rot * Vector3.forward * 0.01f;
                fill.Add(Matrix4x4.TRS(c, rot, new Vector3(width * health, 0.08f, 0.02f)));
            }
            if (shieldFill != null && shield > 0f)
            {
                shield = Mathf.Clamp01(shield);
                var c = center + Vector3.up * 0.1f + rot * Vector3.left * ((1f - shield) * width * 0.5f);
                shieldFill.Add(Matrix4x4.TRS(c, rot, new Vector3(width * shield, 0.05f, 0.02f)));
            }
        }

        private void DrawAllyBars(World world, Entity local, Quaternion barRot)
        {
            using var entities = _vitals.ToEntityArray(Allocator.Temp);
            using var transforms = _vitals.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var vitals = _vitals.ToComponentDataArray<ArenaVitals>(Allocator.Temp);
            _d.Clear(); _e.Clear(); _f.Clear();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                var v = vitals[i];
                Vector3 top = (Vector3)transforms[i].Position + Vector3.up * 2.2f;
                AddBar(top, barRot, 1.2f, v.Health / math.max(v.MaxHealth, 1f), v.Shield / 25f, _d, _e, _f);
            }
            DrawList(_d, _barBackMat);
            DrawList(_e, _barHealthMat);
            DrawList(_f, _barShieldMat);
        }

        private void DrawTracers(World world, Entity local)
        {
            float now = Time.unscaledTime;
            var em = world.EntityManager;
            int myId = -1;
            PayloadKind myPayload = PayloadKind.Vigor;
            bool alive = false;
            Vector3 eye = Vector3.zero;
            if (local != Entity.Null)
            {
                myId = em.GetComponentData<GhostOwner>(local).NetworkId;
                myPayload = em.GetComponentData<ArenaLoadout>(local).Payload;
                alive = em.GetComponentData<ArenaVitals>(local).Health > 0f;
                eye = (Vector3)em.GetComponentData<LocalTransform>(local).Position + Vector3.up * ArenaSettings.EyeHeight;
            }

            // Draw our own tracers straight away, at the gun's rate, rather than waiting for the server.
            bool firing = Cursor.lockState == CursorLockMode.Locked && UnityEngine.InputSystem.Mouse.current != null &&
                          UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            if (firing && alive && now >= _nextLocalShot)
            {
                _nextLocalShot = now + ArenaWeapon.FireInterval;
                var look = ArenaLocalInputSystem.LocalLook;
                float3 dir = math.normalize(ArenaSettings.Forward(look.x, look.y));
                float range = math.min(ArenaWeapon.Range, ArenaLayout.RaycastCover(eye, dir, ArenaWeapon.Range));
                var gun = eye + viewCamera.transform.right * 0.25f - Vector3.up * 0.25f;
                _localTracers.Add((gun, eye + (Vector3)dir * range, myPayload, now));
            }

            _a.Clear(); _b.Clear();
            for (int i = _localTracers.Count - 1; i >= 0; i--)
            {
                var t = _localTracers[i];
                if (now - t.Time > TracerSeconds) { _localTracers.RemoveAt(i); continue; }
                AddLine(t.From, t.To, t.Payload == PayloadKind.Vigor ? _a : _b);
            }
            for (int i = ArenaFx.Shots.Count - 1; i >= 0; i--)
            {
                var s = ArenaFx.Shots[i];
                if (now - s.Time > TracerSeconds) { ArenaFx.Shots.RemoveAt(i); continue; }
                if (s.Shot.ShooterNetworkId == myId) continue;
                AddLine(s.Shot.From, s.Shot.To, s.Shot.Payload == PayloadKind.Vigor ? _a : _b);
            }
            DrawList(_a, _vigorMat);
            DrawList(_b, _mightMat);
        }

        private static void AddLine(Vector3 from, Vector3 to, List<Matrix4x4> list)
        {
            var d = to - from;
            if (d.sqrMagnitude < 0.01f) return;
            list.Add(Matrix4x4.TRS(from + d * 0.5f, Quaternion.LookRotation(d), new Vector3(0.04f, 0.04f, d.magnitude)));
        }

        private void WriteCombatHud(World world, Entity local)
        {
            float now = Time.unscaledTime;
            var em = world.EntityManager;
            if (local != Entity.Null)
            {
                var v = em.GetComponentData<ArenaVitals>(local);
                var b = em.GetComponentData<ArenaBuffs>(local);
                var payload = em.GetComponentData<ArenaLoadout>(local).Payload;
                _text.Append($"\nHealth {v.Health:0}/{v.MaxHealth:0}   Shield {v.Shield:0}\n");
                _text.Append(payload == PayloadKind.Vigor
                    ? "Payload: VIGOR  (allies: heal + shield, enemies: wither)\n"
                    : "Payload: MIGHT  (allies: +damage, enemies: expose)\n");
                if (b.EmpowerRemaining > 0f) _text.Append($"Buff: Might +{b.Empower * 100f:0}% ({b.EmpowerRemaining:0.0}s)\n");
                if (b.ShieldRemaining > 0f) _text.Append($"Buff: Shield ({b.ShieldRemaining:0.0}s)\n");
                _center.text = v.RespawnIn > 0f ? $"Down! Respawning in {v.RespawnIn:0.0}s" : "";
            }

            _text.Append($"\nDamage {ArenaFx.DamageDealt:0}   Healing {ArenaFx.HealingGiven:0}   Kills {ArenaFx.Kills}\n");
            _text.Append($"Ally hits {ArenaFx.AllyHits}" + (ArenaFx.AllyHitsRefused > 0 ? $" ({ArenaFx.AllyHitsRefused} had no effect)" : "") + "\n");

            using var entities = _vitals.ToEntityArray(Allocator.Temp);
            using var vitals = _vitals.ToComponentDataArray<ArenaVitals>(Allocator.Temp);
            using var owners = _vitals.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var loadouts = _vitals.ToComponentDataArray<ArenaLoadout>(Allocator.Temp);
            _text.Append("\nAllies:\n");
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                var v = vitals[i];
                _text.Append($"  player {owners[i].NetworkId} ({loadouts[i].Payload}): " +
                             (v.RespawnIn > 0f ? "down" : $"{v.Health:0} hp{(v.Shield > 0f ? $" +{v.Shield:0} shield" : "")}") + "\n");
            }

            // Hit marker on the crosshair.
            _crosshair.color = Color.white;
            _crosshair.text = "+";
            for (int i = ArenaFx.Hits.Count - 1; i >= 0; i--)
            {
                var (hit, time) = ArenaFx.Hits[i];
                if (now - time > MarkerSeconds)
                {
                    ArenaFx.Hits.RemoveAt(i);
                    continue;
                }
                if (hit.TargetIsPlayer != 0)
                {
                    _crosshair.color = hit.Refused != 0 ? Color.gray : new Color(0.5f, 1f, 0.6f);
                    _crosshair.text = hit.Refused != 0 ? "o" : $"+ {hit.Healed:0}";
                }
                else
                {
                    _crosshair.color = hit.Killed != 0 ? Color.red : hit.WeakPoint != 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    _crosshair.text = hit.Killed != 0 ? "X" : hit.WeakPoint != 0 ? "x!" : "x";
                }
                break;
            }
        }
    }
}
