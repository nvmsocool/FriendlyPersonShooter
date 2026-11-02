using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for being downed and revived. Metres and seconds.</summary>
    public sealed class ReviveSettings
    {
        /// <summary>Seconds a downed player waits for a revive before respawning at a shrine.</summary>
        public float BleedOutTime { get; set; } = 30f;
        /// <summary>Fragment hits needed to revive (a Vigor hit counts <see cref="VigorHitValue"/>).</summary>
        public int HitsToRevive { get; set; } = 8;
        public int VigorHitValue { get; set; } = 2;
        /// <summary>Share of max health a revived player comes back with.</summary>
        public float ReviveHealthFraction { get; set; } = 0.5f;
        /// <summary>Share of carried essence dropped as a cache on bleeding out.</summary>
        public float EssenceDropShare { get; set; } = 0.5f;

        // Waves.
        /// <summary>Seconds after going down before the first wave bursts out.</summary>
        public float FirstWaveDelay { get; set; } = 0.5f;
        public float WaveInterval { get; set; } = 3.5f;
        public int FragmentsPerWave { get; set; } = 5;
        /// <summary>Share of each wave that zigzags (flick targets); the rest fly in arcs (tracking targets).</summary>
        public float ZigzagShare { get; set; } = 0.4f;
        /// <summary>Seconds a fragment lives before it has faded out.</summary>
        public float FragmentLifetime { get; set; } = 5f;
        /// <summary>The last this many seconds of a fragment's life it fades (for drawing only; it can still be hit).</summary>
        public float FadeTime { get; set; } = 1f;
        public float FragmentRadius { get; set; } = 0.2f;

        // Paths, around the body centre.
        public float MinOrbitRadius { get; set; } = 1.5f;
        public float MaxOrbitRadius { get; set; } = 3.5f;
        /// <summary>Height range above the body centre (can be negative to dip near the ground).</summary>
        public float MinHeight { get; set; } = 0f;
        public float MaxHeight { get; set; } = 2f;
        /// <summary>Seconds an arc fragment takes to fly out from the body to its orbit.</summary>
        public float BurstTime { get; set; } = 0.4f;
        /// <summary>Arc fragments' speed along their orbit, m/s.</summary>
        public float ArcSpeed { get; set; } = 3f;
        /// <summary>How far arc fragments bob up and down.</summary>
        public float ArcBob { get; set; } = 0.4f;
        /// <summary>Zigzag fragments' dash speed between corners, m/s.</summary>
        public float ZigzagSpeed { get; set; } = 7f;
        /// <summary>Each zigzag leg is at least this long, so every corner needs a flick.</summary>
        public float MinZigzagLeg { get; set; } = 1.5f;
        /// <summary>Zigzag fragments pause at each corner for a random time in this range.</summary>
        public float MinZigzagPause { get; set; } = 0.15f;
        public float MaxZigzagPause { get; set; } = 0.4f;
    }

    /// <summary>How a soul fragment moves.</summary>
    public enum FragmentMotion : byte
    {
        /// <summary>Circles the body in a bobbing arc: a tracking target.</summary>
        Arc = 0,
        /// <summary>Dashes between random corners with short pauses: a flick target.</summary>
        Zigzag = 1,
    }

    /// <summary>One soul fragment. Read <see cref="Position"/> to draw and hit-test it.</summary>
    public struct SoulFragment
    {
        /// <summary>Unique within its <see cref="ReviveState"/> (counts up from 0).</summary>
        public int Id;
        public FragmentMotion Motion;
        public Vector3 Position;
        public float Age;
        public float Lifetime;
        public int Wave;

        // Arc path.
        internal float Angle;
        internal float AngularSpeed;
        internal float Radius;
        internal float Height;
        internal float BobPhase;

        // Zigzag path.
        internal Vector3 Corner;
        internal float Pause;

        public float TimeLeft => Lifetime - Age;
    }

    /// <summary>Where a revive stands.</summary>
    public enum ReviveStatus : byte
    {
        /// <summary>Down and waiting; fragments are flying.</summary>
        Bleeding = 0,
        /// <summary>Enough fragments were shot: the player is back up where they fell.</summary>
        Revived = 1,
        /// <summary>Time ran out: the player respawns at a shrine and drops part of their carried essence.</summary>
        BledOut = 2,
    }

    /// <summary>Why a fragment hit didn't count.</summary>
    public enum ReviveRefusal : byte
    {
        None = 0,
        /// <summary>The downed player can't shoot their own fragments.</summary>
        SelfHit,
        /// <summary>No fragment with that id (already shot or faded).</summary>
        NoSuchFragment,
        /// <summary>The revive already ended.</summary>
        Finished,
    }

    /// <summary>What one fragment hit did.</summary>
    public struct ReviveHit
    {
        public bool Accepted;
        public ReviveRefusal Refused;
        public int FragmentId;
        /// <summary>Progress this hit added (1, or 2 for Vigor).</summary>
        public int Value;
        public int Progress;
        public int Required;
        /// <summary>True when this hit finished the revive.</summary>
        public bool Revived;
    }

    /// <summary>
    /// A downed player's revive: soul fragments burst out of the body in waves and fly in arcs and
    /// zigzags; any other player's bullets on them add to one shared total, and reaching
    /// <see cref="ReviveSettings.HitsToRevive"/> before the bleed-out timer ends revives the player.
    /// Fragments nobody hits fade while new waves keep coming. Fully deterministic for a seed: the
    /// same seed and the same Step calls give the same fragments.
    ///
    /// Start one when <see cref="PlayerVitals.TakeEnemyDamage(float, out bool)"/> reports the player
    /// went down. It updates the player itself: <see cref="PlayerVitals.Revive"/> on success,
    /// <see cref="PlayerVitals.Respawn"/> on bleed-out (the game then moves them to a shrine and drops
    /// <see cref="ReviveSettings.EssenceDropShare"/> of their carried essence).
    /// </summary>
    public sealed class ReviveState
    {
        private readonly List<SoulFragment> _fragments = new List<SoulFragment>();
        private readonly Dictionary<int, int> _contributions = new Dictionary<int, int>();
        private CombatRandom _random;
        private float _nextWaveIn;
        private int _nextFragmentId;

        public ReviveState(PlayerVitals player, Vector3 bodyCenter, uint seed, ReviveSettings settings)
        {
            Player = player;
            BodyCenter = bodyCenter;
            Settings = settings;
            _random = new CombatRandom(seed);
            BleedOutLeft = settings.BleedOutTime;
            _nextWaveIn = settings.FirstWaveDelay;
        }

        public PlayerVitals Player { get; }
        public int DownedId => Player.Id;
        public ReviveSettings Settings { get; }
        /// <summary>Centre the fragments fly around. Set it if the body moves.</summary>
        public Vector3 BodyCenter { get; set; }

        public ReviveStatus Status { get; private set; }
        public bool IsFinished => Status != ReviveStatus.Bleeding;
        public float BleedOutLeft { get; private set; }
        public int Progress { get; private set; }
        public int Required => Settings.HitsToRevive;
        public float ProgressFraction => Required <= 0 ? 1f : Math.Min(1f, (float)Progress / Required);
        public int WavesSpawned { get; private set; }

        /// <summary>Live fragments, in spawn order.</summary>
        public IReadOnlyList<SoulFragment> Fragments => _fragments;

        /// <summary>Fragment progress each shooter added (for kudos and stats).</summary>
        public IReadOnlyDictionary<int, int> Contributions => _contributions;

        /// <summary>Advances time: moves fragments, fades old ones, bursts new waves, counts down bleed-out.</summary>
        public ReviveStatus Step(float dt)
        {
            if (IsFinished) return Status;

            BleedOutLeft -= dt;
            if (BleedOutLeft <= 0f)
            {
                BleedOutLeft = 0f;
                Finish(ReviveStatus.BledOut);
                return Status;
            }

            for (int i = _fragments.Count - 1; i >= 0; i--)
            {
                var f = _fragments[i];
                f.Age += dt;
                if (f.Age >= f.Lifetime)
                {
                    _fragments.RemoveAt(i);
                    continue;
                }
                Move(ref f, dt);
                _fragments[i] = f;
            }

            _nextWaveIn -= dt;
            while (_nextWaveIn <= 0f)
            {
                SpawnWave();
                _nextWaveIn += Settings.WaveInterval;
            }
            return Status;
        }

        /// <summary>
        /// A bullet from <paramref name="shooterId"/> hit fragment <paramref name="fragmentId"/>.
        /// Any payload counts 1; Vigor counts <see cref="ReviveSettings.VigorHitValue"/>. The fragment
        /// is destroyed. (Use <see cref="HitResolver.HitFragment"/> so a relayed Vigor counts too.)
        /// </summary>
        public ReviveHit RegisterHit(int fragmentId, int shooterId, PayloadKind payload)
        {
            return RegisterHit(fragmentId, shooterId, payload == PayloadKind.Vigor ? Settings.VigorHitValue : 1);
        }

        internal ReviveHit RegisterHit(int fragmentId, int shooterId, int value)
        {
            var hit = new ReviveHit { FragmentId = fragmentId, Progress = Progress, Required = Required };
            if (IsFinished) hit.Refused = ReviveRefusal.Finished;
            else if (shooterId == DownedId) hit.Refused = ReviveRefusal.SelfHit;
            else if (!RemoveFragment(fragmentId)) hit.Refused = ReviveRefusal.NoSuchFragment;
            if (hit.Refused != ReviveRefusal.None) return hit;

            hit.Accepted = true;
            hit.Value = value;
            Progress += value;
            _contributions.TryGetValue(shooterId, out int had);
            _contributions[shooterId] = had + value;
            hit.Progress = Progress;
            if (Progress >= Required)
            {
                Finish(ReviveStatus.Revived);
                hit.Revived = true;
            }
            return hit;
        }

        public bool TryGetFragment(int fragmentId, out SoulFragment fragment)
        {
            for (int i = 0; i < _fragments.Count; i++)
            {
                if (_fragments[i].Id != fragmentId) continue;
                fragment = _fragments[i];
                return true;
            }
            fragment = default;
            return false;
        }

        /// <summary>Opacity for drawing (1 = solid, fading to 0 over the last <see cref="ReviveSettings.FadeTime"/>).</summary>
        public float Opacity(in SoulFragment f)
        {
            if (Settings.FadeTime <= 0f) return 1f;
            return Math.Clamp(f.TimeLeft / Settings.FadeTime, 0f, 1f);
        }

        /// <summary>
        /// Adds every fragment as a hit target. Target id = <paramref name="idBase"/> + fragment id,
        /// so the game can keep fragment ids apart from player and enemy ids; subtract it again
        /// before calling <see cref="RegisterHit(int, int, PayloadKind)"/>.
        /// </summary>
        public void AppendHitTargets(List<HitTarget> into, int idBase)
        {
            for (int i = 0; i < _fragments.Count; i++)
            {
                into.Add(HitTarget.Body(idBase + _fragments[i].Id, _fragments[i].Position, Settings.FragmentRadius));
            }
        }

        private void Finish(ReviveStatus status)
        {
            Status = status;
            _fragments.Clear();
            if (status == ReviveStatus.Revived) Player.Revive(Settings.ReviveHealthFraction);
            else Player.Respawn();
        }

        private bool RemoveFragment(int fragmentId)
        {
            for (int i = 0; i < _fragments.Count; i++)
            {
                if (_fragments[i].Id != fragmentId) continue;
                _fragments.RemoveAt(i);
                return true;
            }
            return false;
        }

        private void SpawnWave()
        {
            var s = Settings;
            int zigzags = (int)MathF.Round(s.FragmentsPerWave * s.ZigzagShare);
            // Spread the wave evenly around the body, starting at a random angle.
            float start = _random.Angle();
            for (int n = 0; n < s.FragmentsPerWave; n++)
            {
                var f = new SoulFragment
                {
                    Id = _nextFragmentId++,
                    Motion = n < zigzags ? FragmentMotion.Zigzag : FragmentMotion.Arc,
                    Position = BodyCenter,
                    Lifetime = s.FragmentLifetime,
                    Wave = WavesSpawned,
                };
                float angle = start + n * (2f * MathF.PI / s.FragmentsPerWave);
                if (f.Motion == FragmentMotion.Arc)
                {
                    f.Angle = angle;
                    f.Radius = _random.Range(s.MinOrbitRadius, s.MaxOrbitRadius);
                    f.Height = _random.Range(s.MinHeight, s.MaxHeight);
                    f.BobPhase = _random.Angle();
                    float dir = _random.Chance(0.5f) ? 1f : -1f;
                    f.AngularSpeed = dir * s.ArcSpeed / f.Radius;
                }
                else
                {
                    float r = _random.Range(s.MinOrbitRadius, s.MaxOrbitRadius);
                    f.Corner = BodyCenter + new Vector3(MathF.Cos(angle) * r, _random.Range(s.MinHeight, s.MaxHeight), MathF.Sin(angle) * r);
                }
                _fragments.Add(f);
            }
            WavesSpawned++;
        }

        private void Move(ref SoulFragment f, float dt)
        {
            var s = Settings;
            if (f.Motion == FragmentMotion.Arc)
            {
                f.Angle += f.AngularSpeed * dt;
                float r = f.Radius * Math.Min(1f, s.BurstTime <= 0f ? 1f : f.Age / s.BurstTime);
                float h = f.Height + s.ArcBob * MathF.Sin(f.Age * 2.5f + f.BobPhase);
                f.Position = BodyCenter + new Vector3(MathF.Cos(f.Angle) * r, h, MathF.Sin(f.Angle) * r);
                return;
            }

            float left = dt;
            for (int guard = 0; guard < 4 && left > 0f; guard++)
            {
                if (f.Pause > 0f)
                {
                    float wait = Math.Min(f.Pause, left);
                    f.Pause -= wait;
                    left -= wait;
                    continue;
                }
                Vector3 to = f.Corner - f.Position;
                float dist = to.Length();
                float step = s.ZigzagSpeed * left;
                if (step < dist)
                {
                    f.Position += to * (step / dist);
                    return;
                }
                // Reached the corner: pause, then dash to a new one.
                f.Position = f.Corner;
                left -= s.ZigzagSpeed > 0f ? dist / s.ZigzagSpeed : left;
                f.Pause = _random.Range(s.MinZigzagPause, s.MaxZigzagPause);
                f.Corner = NextCorner(f.Position);
            }
        }

        private Vector3 NextCorner(Vector3 from)
        {
            var s = Settings;
            Vector3 p = from;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = _random.Angle();
                float r = _random.Range(s.MinOrbitRadius, s.MaxOrbitRadius);
                p = BodyCenter + new Vector3(MathF.Cos(angle) * r, _random.Range(s.MinHeight, s.MaxHeight), MathF.Sin(angle) * r);
                if (Vector3.Distance(p, from) >= s.MinZigzagLeg) return p;
            }
            return p;
        }
    }
}
