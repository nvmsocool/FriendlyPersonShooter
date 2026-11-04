using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Feedback for the human player: enemy attack bolts, the "you got hit" warning (red flash, a marker pointing at
    /// the attacker, a sound and a line saying what hit you), floating damage numbers, the minimap, the XP bar and
    /// the "stuck?" hint.
    /// </summary>
    public partial class ArenaView
    {
        private const float BoltSeconds = 0.3f;
        private const float HurtSeconds = 2.5f;
        private const float NumberSeconds = 0.9f;
        private const float StuckSeconds = 6f;
        private const float StuckDistance = 2f;

        private Material _boltMat, _glimmerMat;
        private float _attacksSeenUpTo = -1f, _hitsSeenUpTo = -1f;
        private float _flash;
        private Vector3 _lastAttacker;
        private float _lastHurtTime = -100f;
        private string _hurtLine;
        private readonly List<(Vector3 Point, string Text, Color Color, float Time, float Scale)> _floating =
            new List<(Vector3, string, Color, float, float)>();
        private Vector3 _stuckAnchor;
        private float _stuckPressing;
        private int _lastLevel;

        private void CreateFeedbackMaterials()
        {
            _boltMat = MakeMat(new Color(1f, 0.15f, 0.1f));
            _glimmerMat = MakeMat(new Color(1f, 0.95f, 0.35f));
        }

        /// <summary>World-space parts: bolts from enemies to whoever they hit. Also notices hits on you and your own hits.</summary>
        private void DrawFeedback(World world, Entity local)
        {
            float now = Time.unscaledTime;
            int myId = local != Entity.Null ? world.EntityManager.GetComponentData<GhostOwner>(local).NetworkId : -1;

            float damage = 0f;
            EnemyFamily family = EnemyFamily.Wisp;
            float seen = _attacksSeenUpTo;
            _a.Clear();
            for (int i = ArenaFx.EnemyAttacks.Count - 1; i >= 0; i--)
            {
                var (attack, time) = ArenaFx.EnemyAttacks[i];
                if (time > _attacksSeenUpTo && attack.VictimNetworkId == myId)
                {
                    damage += attack.Damage;
                    family = attack.Family;
                    _lastAttacker = Near(attack.From);
                }
                seen = Mathf.Max(seen, time);
                if (now - time > BoltSeconds)
                {
                    ArenaFx.EnemyAttacks.RemoveAt(i);
                    continue;
                }
                Vector3 from = Near(attack.From);
                Vector3 to = from + (Vector3)(attack.To - attack.From);
                var d = to - from;
                if (d.sqrMagnitude < 0.01f) continue;
                float w = 0.12f * (1f - (now - time) / BoltSeconds) + 0.03f;
                _a.Add(Matrix4x4.TRS(from + d * 0.5f, Quaternion.LookRotation(d), new Vector3(w, w, d.magnitude)));
            }
            _attacksSeenUpTo = seen;
            DrawList(_a, _boltMat);

            if (damage > 0f)
            {
                _flash = 1f;
                _lastHurtTime = now;
                _hurtLine = $"{FamilyName(family)} hit you  -{damage:0}";
                ArenaAudio.Hurt();
            }

            // Your hits, as floating numbers (collected before the crosshair marker code clears them).
            float hitsSeen = _hitsSeenUpTo;
            foreach (var (hit, time) in ArenaFx.Hits)
            {
                if (time <= _hitsSeenUpTo) continue;
                hitsSeen = Mathf.Max(hitsSeen, time);
                if (hit.Fragment != 0) continue;
                Vector3 p = Near(hit.Point) + new Vector3(Random.Range(-0.3f, 0.3f), 0.3f, Random.Range(-0.3f, 0.3f));
                if (hit.TargetIsPlayer != 0)
                {
                    if (hit.Refused == 0 && hit.Healed >= 0.5f) _floating.Add((p, $"+{hit.Healed:0}", new Color(0.45f, 1f, 0.55f), now, 1f));
                }
                else if (hit.Damage > 0f)
                {
                    var color = hit.Killed != 0 ? new Color(1f, 0.35f, 0.3f) : hit.WeakPoint != 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    _floating.Add((p, hit.Damage >= 10f ? $"{hit.Damage:0}" : $"{hit.Damage:0.#}", color, now, hit.WeakPoint != 0 || hit.Killed != 0 ? 1.3f : 1f));
                }
            }
            _hitsSeenUpTo = hitsSeen;
        }

        private static string FamilyName(EnemyFamily f)
        {
            switch (f)
            {
                case EnemyFamily.Wisp: return "A Wisp (yellow, flies)";
                case EnemyFamily.Skitter: return "A Skitter (red, ground)";
                case EnemyFamily.Mite: return "Mites (green swarm)";
                case EnemyFamily.Bulwark: return "The Bulwark (raid boss)";
                default: return f.ToString();
            }
        }

        /// <summary>Screen-space parts, after the rest of the HUD is written.</summary>
        private void WriteFeedbackHud(World world, Entity local)
        {
            float now = Time.unscaledTime;
            var em = world.EntityManager;
            var look = ArenaLocalInputSystem.LocalLook;

            // Hit warning.
            _flash = Mathf.Max(0f, _flash - Time.unscaledDeltaTime * 2.2f);
            float since = now - _lastHurtTime;
            float? angle = null;
            float fade = 1f - since / HurtSeconds;
            if (local != Entity.Null && fade > 0f)
            {
                Vector3 me = em.GetComponentData<LocalTransform>(local).Position;
                var v = _lastAttacker - me;
                angle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg - look.x;
            }
            _hud.SetHurt(_flash, angle, fade, fade > 0f ? $"<color=#FF5A4D>{_hurtLine}</color>" : null);

            // Floating numbers.
            for (int i = _floating.Count - 1; i >= 0; i--)
            {
                if (now - _floating[i].Time > NumberSeconds) _floating.RemoveAt(i);
            }
            int slot = 0;
            for (int i = _floating.Count - 1; i >= 0 && slot < ArenaHud.NumberSlots; i--)
            {
                var f = _floating[i];
                float age = (now - f.Time) / NumberSeconds;
                if (viewCamera == null) break;
                var screen = viewCamera.WorldToScreenPoint(f.Point + Vector3.up * (age * 1.2f));
                if (screen.z <= 0f) continue;
                var c = f.Color;
                c.a = 1f - age * age;
                float pop = f.Scale * (1f + 0.4f * Mathf.Max(0f, 1f - age * 6f));
                _hud.SetNumber(slot++, f.Text, screen, c, pop);
            }
            for (; slot < ArenaHud.NumberSlots; slot++) _hud.SetNumber(slot, null);

            // Minimap and XP.
            _hud.ShowMap(local != Entity.Null);
            ArenaFx.HasProgress = false;
            if (local == Entity.Null)
            {
                _hud.SetProgress(false, 0, 0, 0, 0);
                return;
            }
            Vector3 feet = em.GetComponentData<LocalTransform>(local).Position;
            float yaw = look.x * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(forward.y, -forward.x);
            int dot = 0;
            using (var entities = _players.ToEntityArray(Allocator.Temp))
            using (var transforms = _players.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var owners = _players.ToComponentDataArray<GhostOwner>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length && dot < ArenaHud.MapDotSlots; i++)
                {
                    if (entities[i] == local) continue;
                    Vector3 p = Near(transforms[i].Position);
                    var o = new Vector2(p.x - feet.x, p.z - feet.z);
                    _hud.SetMapDot(dot++, true, new Vector2(Vector2.Dot(o, right), Vector2.Dot(o, forward)), PlayerColors.For(owners[i].NetworkId));
                }
            }
            for (; dot < ArenaHud.MapDotSlots; dot++) _hud.SetMapDot(dot, false);

            if (em.HasComponent<ArenaProgress>(local))
            {
                var progress = em.GetComponentData<ArenaProgress>(local);
                ArenaFx.LocalProgress = progress;
                ArenaFx.HasProgress = true;
                _hud.SetProgress(true, progress.Level, progress.Xp, progress.XpNext, progress.Points);
                if (_lastLevel > 0 && progress.Level > _lastLevel) ArenaAudio.LevelUp();
                _lastLevel = progress.Level;
            }

            // Stuck: holding a move key for a while without getting anywhere.
            var keyboard = Keyboard.current;
            bool pressing = Cursor.lockState == CursorLockMode.Locked && keyboard != null &&
                            (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);
            bool alive = em.GetComponentData<ArenaVitals>(local).Health > 0f;
            var moved = new Vector2(feet.x - _stuckAnchor.x, feet.z - _stuckAnchor.z);
            if (!alive || moved.magnitude > StuckDistance || moved.magnitude > 50f)
            {
                _stuckAnchor = feet;
                _stuckPressing = 0f;
            }
            else if (pressing)
            {
                _stuckPressing += Time.unscaledDeltaTime;
            }
            if (_stuckPressing >= StuckSeconds)
            {
                _center.text = (_center.text.Length > 0 ? _center.text + "\n" : "") +
                               "<color=#FFD84D>Stuck? Press Esc, then Reset position.</color>";
            }
        }
    }
}
