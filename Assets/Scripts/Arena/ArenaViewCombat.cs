using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
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
        private Material _hasteMat, _relayMat, _slowMat, _tagMat, _fragmentMat, _fragmentFadeMat, _downedMat, _chainMat;
        private EntityQuery _enemies, _vitals, _fragmentQuery;
        private readonly List<Matrix4x4> _g = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _h = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _a = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _b = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _c = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _d = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _e = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _f = new List<Matrix4x4>();
        private readonly List<(Vector3 From, Vector3 To, PayloadKind Payload, float Time, byte Frame)> _localTracers =
            new List<(Vector3, Vector3, PayloadKind, float, byte)>();
        private readonly List<Matrix4x4> _plainTracers = new List<Matrix4x4>();
        private float _shotsHeardUpTo;
        /// <summary>Our own gun's fire timing, mirrored from the server's rules so our tracers don't wait for it.</summary>
        private GunState _localGun;
        private CombatRandom _localRng = new CombatRandom(97);

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
            _hasteMat = MakeMat(new Color(0.3f, 0.85f, 1f));
            _relayMat = MakeMat(new Color(0.95f, 0.4f, 1f));
            _slowMat = MakeMat(new Color(0.3f, 0.5f, 1f));
            _tagMat = MakeMat(new Color(1f, 0.3f, 0.75f));
            _fragmentMat = MakeMat(new Color(0.75f, 1f, 1f));
            _fragmentFadeMat = MakeMat(new Color(0.35f, 0.5f, 0.55f));
            _downedMat = MakeMat(new Color(0.5f, 0.5f, 0.6f));
            _chainMat = MakeMat(new Color(1f, 0.85f, 0.2f));
        }

        private Material PayloadMat(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return _vigorMat;
                case PayloadKind.Haste: return _hasteMat;
                case PayloadKind.Relay: return _relayMat;
                case PayloadKind.None: return _plainMat;
                default: return _mightMat;
            }
        }

        private void CreateCombatQueries(EntityManager em)
        {
            _itemQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaGroundItem>(), ComponentType.ReadOnly<LocalTransform>());
            _enemies = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaEnemy>(), ComponentType.ReadOnly<LocalTransform>());
            _vitals = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ArenaVitals>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<ArenaLoadout>(),
                ComponentType.ReadOnly<ArenaBuffs>());
            _fragmentQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaFragment>(), ComponentType.ReadOnly<LocalTransform>());
        }

        private void DrawCombat(World world, Entity local)
        {
            var camRot = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
            var barRot = Quaternion.Euler(0f, camRot.eulerAngles.y, 0f);
            DrawEnemies(barRot);
            DrawAllyBars(world, local, barRot);
            DrawFragments(world, local);
            DrawTracers(world, local);
        }

        /// <summary>Soul fragments: small glowing cubes, dim as they fade. Your own are drawn dim (you can't shoot them).</summary>
        private void DrawFragments(World world, Entity local)
        {
            int myId = local != Entity.Null ? world.EntityManager.GetComponentData<GhostOwner>(local).NetworkId : -1;
            using var frags = _fragmentQuery.ToComponentDataArray<ArenaFragment>(Allocator.Temp);
            using var transforms = _fragmentQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            _g.Clear(); _h.Clear();
            float spin = Time.time * 360f;
            for (int i = 0; i < frags.Length; i++)
            {
                var f = frags[i];
                bool dim = f.Opacity < 0.6f || f.OwnerNetworkId == myId;
                float size = 0.4f * math.lerp(0.5f, 1f, math.saturate(f.Opacity));
                (dim ? _h : _g).Add(Matrix4x4.TRS(Near(transforms[i].Position), Quaternion.Euler(spin, spin * 0.7f, 0f), Vector3.one * size));
            }
            DrawList(_g, _fragmentMat);
            DrawList(_h, _fragmentFadeMat);
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
            var slowed = new List<Matrix4x4>();
            var tagged = new List<Matrix4x4>();
            var glimmers = new List<Matrix4x4>();
            _bossSeen = false;
            for (int i = 0; i < enemies.Length; i++)
            {
                var en = enemies[i];
                Vector3 p = Near(transforms[i].Position);
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
                    case EnemyFamily.Glimmer:
                        // A small spinning yellow diamond: one shot pops it for a little Yellow.
                        size = 0.5f;
                        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 9f);
                        glimmers.Add(Matrix4x4.TRS(p, Quaternion.Euler(45f, Time.time * 200f, 45f), new Vector3(size, size * 1.4f, size) * pulse));
                        break;
                    case EnemyFamily.Bulwark:
                        // The raid boss: a big shell with a plate on one side (its health bar is on the HUD).
                        size = 4.2f;
                        AddBoss(en, p);
                        break;
                    default:
                        size = 0.3f;
                        (en.Telegraph ? _f : mites).Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * size));
                        break;
                }

                if (en.Family != EnemyFamily.Mite && en.Family != EnemyFamily.Glimmer && en.Family != EnemyFamily.Bulwark)
                {
                    AddBar(p + Vector3.up * (size * 0.5f + 0.5f), barRot, 1f, en.Health / math.max(en.MaxHealth, 1f), 0f, _d, _e, null);
                }
                Vector3 top = p + Vector3.up * (size * 0.5f + 0.75f);
                if (en.Withered) withered.Add(Matrix4x4.TRS(top + barRot * Vector3.left * 0.2f, barRot, Vector3.one * 0.15f));
                if (en.Exposed) exposed.Add(Matrix4x4.TRS(top + barRot * Vector3.right * 0.2f, barRot, Vector3.one * 0.15f));
                if (en.Slowed) slowed.Add(Matrix4x4.TRS(top + barRot * Vector3.left * 0.6f, barRot, Vector3.one * 0.15f));
                if (en.Tagged) tagged.Add(Matrix4x4.TRS(top + barRot * Vector3.right * 0.6f, barRot, Vector3.one * 0.15f));
            }
            DrawList(_a, _wispMat);
            DrawList(_b, _wispDimMat);
            DrawList(_c, _skitterMat);
            DrawList(cores, _coreMat);
            DrawList(mites, _miteMat);
            DrawList(glimmers, _glimmerMat);
            DrawBosses();
            DrawList(_f, _telegraphMat);
            DrawList(_d, _barBackMat);
            DrawList(_e, _barHealthMat);
            DrawList(withered, _witherMat);
            DrawList(exposed, _exposeMat);
            DrawList(slowed, _slowMat);
            DrawList(tagged, _tagMat);
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
            using var buffs = _vitals.ToComponentDataArray<ArenaBuffs>(Allocator.Temp);
            _d.Clear(); _e.Clear(); _f.Clear(); _g.Clear(); _h.Clear();
            var revive = new List<Matrix4x4>();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                var v = vitals[i];
                Vector3 feet = Near(transforms[i].Position);
                if (v.Health <= 0f)
                {
                    // Downed: a slab on the floor and a revive bar that fills as fragments are shot.
                    _g.Add(Matrix4x4.TRS(feet + Vector3.up * 0.2f, barRot, new Vector3(0.9f, 0.4f, 1.7f)));
                    AddBar(feet + Vector3.up * 1.2f, barRot, 1.4f, v.ReviveProgress, 0f, _d, revive, null);
                    continue;
                }
                Vector3 top = feet + Vector3.up * 2.2f;
                AddBar(top, barRot, 1.2f, v.Health / math.max(v.MaxHealth, 1f), v.Shield / 25f, _d, _e, _f);
                // A spinning gold cube over anyone holding a chain: buff them or let them cash it.
                if (buffs[i].ChainLinks > 0)
                {
                    _h.Add(Matrix4x4.TRS(top + Vector3.up * 0.35f, Quaternion.Euler(0f, Time.time * 180f, 45f),
                        Vector3.one * (0.12f + 0.06f * buffs[i].ChainLinks)));
                }
            }
            DrawList(_g, _downedMat);
            DrawList(_d, _barBackMat);
            DrawList(_e, _barHealthMat);
            DrawList(_f, _barShieldMat);
            DrawList(revive, _fragmentMat);
            DrawList(_h, _chainMat);
        }

        private void DrawTracers(World world, Entity local)
        {
            float now = Time.unscaledTime;
            var em = world.EntityManager;
            int myId = -1;
            bool alive = false;
            int ranks = 0;
            Vector3 eye = Vector3.zero;
            ArenaLoadout lo = default;
            if (local != Entity.Null)
            {
                myId = em.GetComponentData<GhostOwner>(local).NetworkId;
                lo = em.GetComponentData<ArenaLoadout>(local);
                alive = em.GetComponentData<ArenaVitals>(local).Health > 0f;
                if (em.HasComponent<ArenaProgress>(local)) ranks = em.GetComponentData<ArenaProgress>(local).Ranks;
                eye = (Vector3)em.GetComponentData<LocalTransform>(local).Position + Vector3.up * ArenaSettings.EyeHeight;
            }
            var stats = LocalGunStats(lo, now);
            _localZoom = stats != null ? stats.Zoom : 1f;

            // Draw our own shots straight away, at the gun's rate, rather than waiting for the server.
            // The server has the final say on ammo, spread and hits; launcher shells are drawn from its reply.
            bool firing = Cursor.lockState == CursorLockMode.Locked && UnityEngine.InputSystem.Mouse.current != null &&
                          UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            if (stats != null && alive && local != Entity.Null)
            {
                bool loaded = lo.Reload >= 0.999f && lo.Ammo > 0;
                _localGun.Ammo = 999;
                _localGun.ReloadEnds = 0;
                if (!loaded) _localGun.BurstLeft = 0;
                int shots = loaded ? FireControl.Step(ref _localGun, stats, now, firing, false, PlayerProgress.FireRateFor(ranks)) : 0;
                for (int n = 0; n < shots; n++)
                {
                    var look = ArenaLocalInputSystem.LocalLook;
                    var aim = ArenaSettings.Forward(look.x, look.y);
                    var d = FireControl.Spread(new System.Numerics.Vector3(aim.x, aim.y, aim.z), stats.SpreadFor(ArenaLocalInputSystem.Zooming), ref _localRng);
                    var dir = new float3(d.X, d.Y, d.Z);
                    float reach = stats.Range * PlayerProgress.RangeFor(ranks);
                    float range = math.min(reach, ArenaLayout.RaycastCover(eye, dir, reach));
                    var gun = eye + viewCamera.transform.right * 0.25f - Vector3.up * 0.25f;
                    if (stats.Mode != FireMode.Projectile) _localTracers.Add((gun, eye + (Vector3)dir * range, lo.Payload, now, (byte)stats.Frame.Kind));
                    if (n == 0) ArenaAudio.Shot(lo.Payload, gun, own: true);
                }
            }

            // Everyone else's shots (players and bots), heard once each from where they were fired; shells start flying.
            float heardUpTo = _shotsHeardUpTo;
            for (int i = 0; i < ArenaFx.Shots.Count; i++)
            {
                var s = ArenaFx.Shots[i];
                if (s.Time <= _shotsHeardUpTo) continue;
                heardUpTo = Mathf.Max(heardUpTo, s.Time);
                Vector3 from = Near(s.Shot.From);
                if (s.Shot.Frame == (byte)FrameKind.Launcher)
                {
                    AddShell(from, from + (Vector3)(s.Shot.To - s.Shot.From), s.Shot.Payload, s.Time);
                }
                if (s.Shot.ShooterNetworkId != myId) ArenaAudio.Shot(s.Shot.Payload, from, own: false);
            }
            _shotsHeardUpTo = heardUpTo;

            _a.Clear(); _b.Clear(); _g.Clear(); _h.Clear(); _plainTracers.Clear();
            for (int i = _localTracers.Count - 1; i >= 0; i--)
            {
                var t = _localTracers[i];
                if (now - t.Time > TracerLife(t.Frame)) { _localTracers.RemoveAt(i); continue; }
                AddLine(t.From, t.To, TracerList(t.Payload), TracerWidth(t.Frame));
            }
            for (int i = ArenaFx.Shots.Count - 1; i >= 0; i--)
            {
                var s = ArenaFx.Shots[i];
                if (now - s.Time > TracerLife(s.Shot.Frame)) { ArenaFx.Shots.RemoveAt(i); continue; }
                if (s.Shot.ShooterNetworkId == myId || s.Shot.Frame == (byte)FrameKind.Launcher) continue;
                // Move the whole tracer by the same offset as its start, so it isn't stretched across the wrap.
                Vector3 from = Near(s.Shot.From);
                AddLine(from, from + (Vector3)(s.Shot.To - s.Shot.From), TracerList(s.Shot.Payload), TracerWidth(s.Shot.Frame));
            }
            DrawShells(now);
            DrawList(_a, _vigorMat);
            DrawList(_b, _mightMat);
            DrawList(_g, _hasteMat);
            DrawList(_h, _relayMat);
            DrawList(_plainTracers, _plainMat);
        }

        /// <summary>The beam's pulses overlap into a steady line; a marksman's shot lingers a moment.</summary>
        private static float TracerLife(byte frame) =>
            frame == (byte)FrameKind.Beam ? 0.13f : frame == (byte)FrameKind.Marksman ? 0.18f : TracerSeconds;

        private static float TracerWidth(byte frame)
        {
            switch ((FrameKind)frame)
            {
                case FrameKind.Beam: return 0.09f;
                case FrameKind.Marksman: return 0.07f;
                case FrameKind.Smg: return 0.03f;
                default: return 0.04f;
            }
        }

        private List<Matrix4x4> TracerList(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return _a;
                case PayloadKind.Haste: return _g;
                case PayloadKind.Relay: return _h;
                case PayloadKind.None: return _plainTracers;
                default: return _b;
            }
        }

        private static void AddLine(Vector3 from, Vector3 to, List<Matrix4x4> list, float width = 0.04f)
        {
            var d = to - from;
            if (d.sqrMagnitude < 0.01f) return;
            list.Add(Matrix4x4.TRS(from + d * 0.5f, Quaternion.LookRotation(d), new Vector3(width, width, d.magnitude)));
        }

        private void WriteCombatHud(World world, Entity local)
        {
            float now = Time.unscaledTime;
            var em = world.EntityManager;
            var hud = _hud;
            int localId = 0;
            if (local != Entity.Null)
            {
                var v = em.GetComponentData<ArenaVitals>(local);
                var b = em.GetComponentData<ArenaBuffs>(local);
                var loadout = em.GetComponentData<ArenaLoadout>(local);
                var payload = loadout.Payload;
                localId = em.GetComponentData<GhostOwner>(local).NetworkId;
                hud.SetVitals(true, v.Health, v.MaxHealth, v.Shield);

                if (b.EmpowerRemaining > 0f) hud.Buffs.Append($"<color=#FF9A4D>MIGHT</color> +{b.Empower * 100f:0}% damage  {b.EmpowerRemaining:0.0}s{(b.Blitz ? "   <color=#FFD84D>BLITZ</color> (Haste + Might: double damage bonus)" : "")}\n");
                if (b.ShieldRemaining > 0f) hud.Buffs.Append($"<color=#7FDBFF>SHIELD</color>  {b.ShieldRemaining:0.0}s\n");
                if (b.HasteRemaining > 0f) hud.Buffs.Append($"<color=#66D9FF>HASTE</color> x{b.MoveMultiplier:0.00} speed  {b.HasteRemaining:0.0}s\n");
                if (b.RelayHits > 0) hud.Buffs.Append($"<color=#D98CFF>RELAY</color>  next {b.RelayHits} hits also carry {b.RelayPayload}\n");

                string chain = null;
                if (b.ChainLinks > 0)
                {
                    chain = b.ChainLinks >= 2
                        ? $"<color=#FFD84D>CHAIN x{b.ChainLinks}</color>  {b.ChainTimeLeft:0.0}s   hit an enemy to cash it, or buff someone new to grow it"
                        : $"<color=#FFD84D>Chain started</color>  {b.ChainTimeLeft:0.0}s   buff someone new to grow it";
                }
                hud.SetGun(payload, GunTitle(loadout), GunInfo(loadout), chain);
                hud.SetPrompt(_prompt);
                if (v.Health <= 0f)
                {
                    _center.text = $"DOWN!  Allies: shoot your soul fragments to revive you   {v.ReviveProgress * 100f:0}%   bleed-out {v.RespawnIn:0.0}s";
                }
            }
            else
            {
                hud.SetVitals(false, 0f, 0f, 0f);
            }
            _announceLocalId = localId;
            WriteAnnouncements(now);

            hud.Stats.Append($"Damage {ArenaFx.DamageDealt:0}   Healing {ArenaFx.HealingGiven:0}   Kills {ArenaFx.Kills}\n");
            hud.Stats.Append($"Ally hits {ArenaFx.AllyHits}" + (ArenaFx.AllyHitsRefused > 0 ? $" ({ArenaFx.AllyHitsRefused} had no effect)" : "") +
                             $"   Chains {ArenaFx.ChainsCashed} (best x{ArenaFx.BestChain})   Fragments {ArenaFx.FragmentHits}");

            using var entities = _vitals.ToEntityArray(Allocator.Temp);
            using var vitals = _vitals.ToComponentDataArray<ArenaVitals>(Allocator.Temp);
            using var owners = _vitals.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var loadouts = _vitals.ToComponentDataArray<ArenaLoadout>(Allocator.Temp);
            using var allyBuffs = _vitals.ToComponentDataArray<ArenaBuffs>(Allocator.Temp);
            if (localId != 0) hud.Team.Append($"<b>You: {PlayerColors.Tag(localId)}</b>\n");
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                var v = vitals[i];
                var pc = ArenaHud.PayloadColor(loadouts[i].Payload);
                var allyGun = Gun.Unpack(loadouts[i].Active == 0 ? loadouts[i].GunA : loadouts[i].GunB);
                string allyPayload = loadouts[i].Payload == PayloadKind.None ? "plain" : loadouts[i].Payload.ToString();
                hud.Team.Append($"{PlayerColors.Tag(owners[i].NetworkId)}  <color=#{ColorUtility.ToHtmlStringRGB(pc)}>{allyPayload} {GunFrames.Name(allyGun.Frame)}</color>  " +
                                (v.Health <= 0f ? $"<color=#FF6B6B>DOWN</color> {v.ReviveProgress * 100f:0}%, {v.RespawnIn:0}s"
                                    : $"{v.Health:0} hp{(v.Shield > 0f ? $" +{v.Shield:0}" : "")}") +
                                (allyBuffs[i].ChainLinks > 0 ? $"  <color=#FFD84D>x{allyBuffs[i].ChainLinks}</color>" : "") + "\n");
            }
            if (entities.Length <= 1 && localId != 0) hud.Team.Append("<color=#B8B0D8>No one else here yet.</color>\n");

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
                if (hit.Fragment != 0)
                {
                    _crosshair.color = hit.Refused != 0 ? Color.gray : new Color(0.75f, 1f, 1f);
                    _crosshair.text = hit.Refused != 0 ? "o" : $"* {hit.ReviveProgress * 100f:0}%";
                }
                else if (hit.ChainCashed != 0)
                {
                    _crosshair.color = new Color(1f, 0.85f, 0.2f);
                    _crosshair.text = $"x CHAIN x{hit.ChainLinks} +{hit.ChainBonus * 100f:0}%";
                }
                else if (hit.TargetIsPlayer != 0)
                {
                    _crosshair.color = hit.Refused != 0 ? Color.gray : new Color(0.5f, 1f, 0.6f);
                    _crosshair.text = hit.Refused != 0 ? "o" : hit.ChainLinks > 0 ? $"+ {hit.Healed:0}  chain x{hit.ChainLinks}" : $"+ {hit.Healed:0}";
                }
                else
                {
                    _crosshair.color = hit.Killed != 0 ? Color.red : hit.WeakPoint != 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    _crosshair.text = hit.Killed != 0 ? "X" : hit.WeakPoint != 0 ? "x!" : "x";
                }
                break;
            }
        }
    
        private const float AnnounceSeconds = 3.5f;
        private int _announceLocalId;

        /// <summary>Recent server announcements, newest first, under the crosshair.</summary>
        private void WriteAnnouncements(float now)
        {
            var lines = new System.Text.StringBuilder();
            for (int i = ArenaFx.Announcements.Count - 1; i >= 0; i--)
            {
                var (m, time) = ArenaFx.Announcements[i];
                if (now - time > AnnounceSeconds)
                {
                    ArenaFx.Announcements.RemoveAt(i);
                    continue;
                }
                switch (m.Kind)
                {
                    case AnnounceKind.ComboDiscovered:
                        var kind = (ComboKind)m.Value;
                        lines.Append(kind == ComboKind.Plague
                            ? "COMBO DISCOVERED: PLAGUE  (Wither + Tag: damage over time spreads between enemies)\n"
                            : "COMBO DISCOVERED: BLITZ  (Haste + Might on one ally: damage bonus doubled)\n");
                        break;
                    case AnnounceKind.Revived:
                        lines.Append($"{PlayerColors.Tag(m.PlayerNetworkId)} revived by {PlayerColors.Tag(m.OtherNetworkId)}\n");
                        break;
                    case AnnounceKind.BledOut:
                        lines.Append($"{PlayerColors.Tag(m.PlayerNetworkId)} bled out\n");
                        break;
                    case AnnounceKind.LevelUp:
                        lines.Append(m.PlayerNetworkId == _announceLocalId
                            ? $"<color=#D9B3FF>LEVEL {m.Value}!</color>  Esc to spend your skill point\n"
                            : $"{PlayerColors.Tag(m.PlayerNetworkId)} reached level {m.Value}\n");
                        break;
                    case AnnounceKind.ChainCashed:
                        if (m.Value >= 3) lines.Append($"{PlayerColors.Tag(m.PlayerNetworkId)} cashed a x{m.Value} chain!\n");
                        break;
                    default:
                        var raid = RaidAnnouncement(m);
                        if (raid != null) lines.Append(raid);
                        break;
                }
            }
            if (lines.Length > 0) _center.text = (_center.text.Length > 0 ? _center.text + "\n" : "") + lines;
        }
}
}
