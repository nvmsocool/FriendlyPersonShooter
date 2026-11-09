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
                if (attack.BoltId != 0) _boltsEnded.Add(attack.BoltId);
                // Bolts and shockwaves were drawn on their way; only melee hits get the quick line.
                if (now - time > BoltSeconds || attack.BoltId != 0 || attack.Family == EnemyFamily.Bulwark)
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
            DrawBolts(now);
            DrawShockwaves(now);

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

        private readonly HashSet<int> _boltsEnded = new HashSet<int>();
        private readonly List<Matrix4x4> _boltOrbs = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _boltTrails = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _waveSegs = new List<Matrix4x4>();
        private Material _boltOrbMat, _boltTrailMat, _waveMat;
        private const int WaveSegments = 56;

        /// <summary>
        /// Enemy bolts in flight: a glowing orb with a short trail, flown along the server's line until it
        /// ends or hits someone. They're slow enough to sidestep.
        /// </summary>
        private void DrawBolts(float now)
        {
            if (_boltOrbMat == null)
            {
                _boltOrbMat = MakeGlow(new Color(1f, 0.3f, 0.15f), 2.4f);
                _boltTrailMat = MakeGlow(new Color(1f, 0.55f, 0.2f), 1.2f);
            }
            _boltOrbs.Clear();
            _boltTrails.Clear();
            for (int i = ArenaFx.Bolts.Count - 1; i >= 0; i--)
            {
                var (b, time) = ArenaFx.Bolts[i];
                float age = now - time;
                if (age > b.Life || _boltsEnded.Contains(b.Id))
                {
                    ArenaFx.Bolts.RemoveAt(i);
                    _boltsEnded.Remove(b.Id);
                    continue;
                }
                Vector3 v = b.Velocity;
                Vector3 p = Near(b.From) + v * age;
                float size = Mathf.Max(0.35f, b.Radius * 1.6f) * (1f + 0.15f * Mathf.Sin(now * 30f + b.Id));
                _boltOrbs.Add(Matrix4x4.TRS(p, Quaternion.Euler(now * 400f, now * 300f, 0f), Vector3.one * size));
                float trail = Mathf.Min(age * v.magnitude, 2.5f);
                if (trail > 0.1f && v.sqrMagnitude > 0.01f)
                {
                    var dir = v.normalized;
                    _boltTrails.Add(Matrix4x4.TRS(p - dir * (trail * 0.5f), Quaternion.LookRotation(dir), new Vector3(size * 0.35f, size * 0.35f, trail)));
                }
            }
            if (_boltsEnded.Count > 200) _boltsEnded.Clear();
            DrawList(_boltOrbs, _boltOrbMat);
            DrawList(_boltTrails, _boltTrailMat);
        }

        /// <summary>Shockwave rings: a glowing band hugging the ground as it runs out. Jump it.</summary>
        private void DrawShockwaves(float now)
        {
            if (_waveMat == null) _waveMat = MakeGlow(new Color(1f, 0.5f, 0.1f), 2.2f);
            _waveSegs.Clear();
            for (int i = ArenaFx.Waves.Count - 1; i >= 0; i--)
            {
                var (w, time) = ArenaFx.Waves[i];
                float r = (now - time) * w.Speed;
                if (r > w.Range)
                {
                    ArenaFx.Waves.RemoveAt(i);
                    continue;
                }
                Vector3 c = Near(w.Center);
                float seg = 2f * Mathf.PI * Mathf.Max(r, 0.5f) / WaveSegments * 1.15f;
                for (int k = 0; k < WaveSegments; k++)
                {
                    float a = k * 2f * Mathf.PI / WaveSegments;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var p = c + dir * r;
                    float y = ExpeditionTerrain.Active && ExpeditionTerrain.SurfaceNear(p.x, p.z, c.y, 4f, 4f, out float g) ? g : c.y;
                    _waveSegs.Add(Matrix4x4.TRS(new Vector3(p.x, y + 0.35f, p.z), Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up)),
                        new Vector3(w.Width * 0.6f, 0.7f, seg)));
                }
            }
            DrawList(_waveSegs, _waveMat);
        }

        private static string FamilyName(EnemyFamily f)
        {
            switch (f)
            {
                case EnemyFamily.Wisp: return "A Wisp (yellow, flies)";
                case EnemyFamily.Skitter: return "A Skitter (red, ground)";
                case EnemyFamily.Mite: return "Mites (green swarm)";
                case EnemyFamily.Sentinel: return "A Sentinel (blue pillar, shoots from afar)";
                case EnemyFamily.Diver: return "A Diver (orange, dives from above)";
                case EnemyFamily.Bulwark: return "A Bulwark or boss";
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

            // Enemies as small diamonds in their essence color (Glimmers left out; the Colossus is big).
            int enemyDot = 0;
            using (var enemies = _enemies.ToComponentDataArray<ArenaEnemy>(Allocator.Temp))
            using (var enemyAt = _enemies.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < enemies.Length && enemyDot < ArenaHud.EnemyDotSlots; i++)
                {
                    var family = enemies[i].Family;
                    if (family == EnemyFamily.Glimmer) continue;
                    bool boss = family == EnemyFamily.Bulwark && (enemies[i].World || enemies[i].Boss);
                    Vector3 p = Near(enemyAt[i].Position);
                    var o = new Vector2(p.x - feet.x, p.z - feet.z);
                    if (o.magnitude > ArenaHud.MapRange) continue;
                    // Each in its essence color; bosses white.
                    var color = Color.white;
                    if (!boss) ColorUtility.TryParseHtmlString(EssencePack.Hex(Core.Expedition.EssenceColors.Of(family)), out color);
                    float size = boss ? 14f : family == EnemyFamily.Bulwark ? 11f : family == EnemyFamily.Mite ? 5f
                        : enemies[i].Elite != EliteKind.None ? 11f : 8f;
                    if (_hud.SetEnemyDot(enemyDot, true, new Vector2(Vector2.Dot(o, right), Vector2.Dot(o, forward)), color, size)) enemyDot++;
                }
            }
            for (; enemyDot < ArenaHud.EnemyDotSlots; enemyDot++) _hud.SetEnemyDot(enemyDot, false);
            NotePingOwner(world, local);
            WritePingHud(local, feet, right, forward);

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
