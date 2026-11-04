using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The raid half of the view (MVP step 6): the raid door on the ground with a beam over it, the
    /// floating arena, the Bulwark with its turning plate, and the raid lines of the HUD.
    /// </summary>
    public partial class ArenaView
    {
        private Material _raidFloorMat, _raidWallMat, _doorMat, _doorGlowMat, _bossMat, _plateShutMat, _plateWarnMat, _plateOpenMat;
        private readonly List<Matrix4x4> _r = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossBodies = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossTelegraph = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesShut = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesWarn = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesOpen = new List<Matrix4x4>();
        private static readonly RaidSettings RaidRules = new RaidSettings();
        private static readonly BulwarkSettings BossLook = new BulwarkSettings();
        private float _bossHealth, _bossMax;
        private bool _bossSeen, _bossOpen, _bossWarn;

        private void CreateRaidMaterials()
        {
            _raidFloorMat = MakeMat(new Color(0.3f, 0.27f, 0.36f));
            _raidWallMat = MakeMat(new Color(0.42f, 0.36f, 0.52f));
            _doorMat = MakeMat(new Color(0.38f, 0.22f, 0.5f));
            _doorGlowMat = MakeMat(new Color(0.85f, 0.5f, 1f));
            _bossMat = MakeMat(new Color(0.32f, 0.2f, 0.42f));
            _plateShutMat = MakeMat(new Color(0.55f, 0.5f, 0.45f));
            _plateWarnMat = MakeMat(new Color(1f, 0.55f, 0.15f));
            _plateOpenMat = MakeMat(new Color(1f, 0.85f, 0.2f));
        }

        /// <summary>The door (posts, lintel, a panel that fills with the key) and its beam, and the arena above it.</summary>
        private void DrawRaid()
        {
            RaidArena.Ensure();
            TryGetStatus(out var status);

            // The arena: only when the camera is up there (it is hidden in the fog from the ground anyway).
            if (RaidArena.Contains(_camRef, 200f, 200f))
            {
                _r.Clear();
                _x.Clear();
                foreach (var b in RaidArena.Boxes)
                {
                    (b.IsFloor ? _r : _x).Add(Matrix4x4.TRS(Near(b.Center), Quaternion.identity, b.Size));
                }
                DrawList(_r, _raidFloorMat);
                DrawList(_x, _raidWallMat);
            }

            var door = RaidArena.Door;
            var p = Near(ExpeditionWorld.ToFloat3(door, GroundAt(door)));
            const float w = 7f, h = 8f;
            _x.Clear();
            _x.Add(Matrix4x4.TRS(p + new Vector3(-w * 0.5f, h * 0.5f, 0f), Quaternion.identity, new Vector3(1.4f, h, 1.4f)));
            _x.Add(Matrix4x4.TRS(p + new Vector3(w * 0.5f, h * 0.5f, 0f), Quaternion.identity, new Vector3(1.4f, h, 1.4f)));
            _x.Add(Matrix4x4.TRS(p + new Vector3(0f, h + 0.5f, 0f), Quaternion.identity, new Vector3(w + 2f, 1.2f, 1.6f)));
            DrawList(_x, _doorMat);

            // The panel fills from the bottom as the key fills; it pulses while the door gathers.
            int cost = RaidRules.RedCost + RaidRules.GreenCost;
            float fill = status.RaidPhase == RaidPhase.Collecting ? math.saturate((status.KeyRed + status.KeyGreen) / (float)cost)
                : status.RaidPhase == RaidPhase.Gathering ? 1f : 0f;
            _r.Clear();
            if (fill > 0f)
            {
                float ph = (h - 0.5f) * fill;
                float pulse = status.RaidPhase == RaidPhase.Gathering ? 1f + 0.08f * Mathf.Sin(Time.time * 8f) : 1f;
                _r.Add(Matrix4x4.TRS(p + new Vector3(0f, ph * 0.5f, 0f), Quaternion.identity, new Vector3((w - 1.4f) * pulse, ph, 0.2f)));
            }
            // A beam so the door can be found from across the zone.
            float beam = status.RaidPhase == RaidPhase.Gathering ? 1.2f : 0.5f;
            _r.Add(Matrix4x4.TRS(p + Vector3.up * 50f, Quaternion.identity, new Vector3(beam, 100f, beam)));
            DrawList(_r, _doorGlowMat);
        }

        /// <summary>Collects the boss for drawing (called from DrawEnemies); its health bar is on the HUD.</summary>
        private void AddBoss(ArenaEnemy en, Vector3 p)
        {
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            float size = BossLook.BodyRadius * 1.6f;
            (en.Telegraph ? _bossTelegraph : _bossBodies).Add(Matrix4x4.TRS(p, rot * Quaternion.Euler(0f, 45f, 0f), Vector3.one * size));
            var facing = rot * Vector3.forward;
            var plate = p + facing * (BossLook.BodyRadius + BossLook.PlateOut * 0.5f);
            float pulse = en.PlateWarn ? 1f + 0.1f * Mathf.Sin(Time.time * 18f) : 1f;
            var plateBox = Matrix4x4.TRS(plate, rot, new Vector3(BossLook.PlateRadius * 1.8f * pulse, BossLook.PlateRadius * 1.8f * pulse, 0.5f));
            (en.PlateOpen ? _platesOpen : en.PlateWarn ? _platesWarn : _platesShut).Add(plateBox);
            _bossSeen = true;
            _bossHealth = en.Health;
            _bossMax = en.MaxHealth;
            _bossOpen = en.PlateOpen;
            _bossWarn = en.PlateWarn;
        }

        private void DrawBosses()
        {
            DrawList(_bossBodies, _bossMat);
            DrawList(_bossTelegraph, _telegraphMat);
            DrawList(_platesShut, _plateShutMat);
            DrawList(_platesWarn, _plateWarnMat);
            DrawList(_platesOpen, _plateOpenMat);
            _bossBodies.Clear();
            _bossTelegraph.Clear();
            _platesShut.Clear();
            _platesWarn.Clear();
            _platesOpen.Clear();
        }

        /// <summary>Raid lines for the expedition HUD: the boss bar inside, the countdown, the key near the door.</summary>
        private void WriteRaidHud(ExpeditionStatus s, System.Text.StringBuilder e)
        {
            int t = (int)s.RaidTimer;
            if (s.InRaid)
            {
                if (_bossSeen && _bossMax > 0f)
                {
                    float f = math.saturate(_bossHealth / _bossMax);
                    int bars = Mathf.RoundToInt(f * 24f);
                    string plate = _bossOpen ? "<color=#FFD84D>PLATE OPEN: shoot it!</color>"
                        : _bossWarn ? "<color=#FF9A40>plate opening...</color>" : "<color=#B8B0D8>plate shut (hits do 1/5)</color>";
                    e.Append($"<color=#D9B3FF><b>BULWARK</b></color>  [{new string('|', bars)}{new string('.', 24 - bars)}]  {f * 100f:0}%   {plate}\n");
                }
                e.Append($"<color=#D9B3FF>Raid time left {t / 60}:{t % 60:00}</color>\n");
                return;
            }
            var door = RaidArena.Door;
            var delta = ExpeditionWorld.Near(ExpeditionWorld.ToFloat3(door), _camRef) - _camRef;
            float distance = math.length(delta.xz);
            float bearing = math.degrees(math.atan2(delta.x, delta.z));
            string where = $"{distance:0} m {Direction(Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing))}";
            switch (s.RaidPhase)
            {
                case RaidPhase.Collecting:
                    e.Append($"<color=#D9B3FF>Raid door</color> {where}: key <color=#FF6B5B>Red {s.KeyRed}/{RaidRules.RedCost}</color> " +
                             $"<color=#7BE07B>Green {s.KeyGreen}/{RaidRules.GreenCost}</color>\n");
                    break;
                case RaidPhase.Gathering:
                    e.Append($"<color=#D9B3FF><b>RAID DOOR OPEN</b></color> {where}: stand at it, going in in {t}s\n");
                    break;
                case RaidPhase.Running:
                    e.Append($"<color=#B8B0D8>A group is in the raid ({t / 60}:{t % 60:00} left)</color>\n");
                    break;
                default:
                    e.Append($"<color=#B8B0D8>Raid door resets in {t}s</color>\n");
                    break;
            }
        }

        private static string RaidAnnouncement(ArenaAnnounce m)
        {
            switch (m.Kind)
            {
                case AnnounceKind.RaidGathering:
                    return $"<color=#D9B3FF>RAID KEY FULL! Stand at the raid door: going in in {m.Value}s</color>\n";
                case AnnounceKind.RaidStarted:
                    return $"<color=#D9B3FF>{m.Value} player{(m.Value == 1 ? "" : "s")} went into the raid</color>\n";
                case AnnounceKind.RaidCleared:
                    return $"<color=#FFD84D>RAID CLEARED in {m.Value / 60}:{m.Value % 60:00}! +{RaidRules.ClearXp} XP, +{RaidRules.ClearYellow} Yellow</color>\n";
                case AnnounceKind.RaidFailed:
                    return (RaidOutcome)m.Value == RaidOutcome.TimedOut
                        ? "<color=#FF8080>The raid ran out of time</color>\n"
                        : "<color=#FF8080>The raid group was wiped</color>\n";
                case AnnounceKind.RaidAdds:
                    return "<color=#FF9A40>The Bulwark calls for help: adds incoming!</color>\n";
                case AnnounceKind.RaidPledged:
                    return $"{PlayerColors.Tag(m.PlayerNetworkId)} pledged {m.Value} to the raid key\n";
                default:
                    return null;
            }
        }
    }
}
