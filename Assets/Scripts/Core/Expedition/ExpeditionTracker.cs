using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>The steps of the step-4 expedition, in order.</summary>
    public enum ExpeditionPhase : byte
    {
        /// <summary>Hunt the gate's color until carried + banked covers the cost.</summary>
        Gather = 0,
        /// <summary>Enough in hand, but not enough banked: get to the gate (or raid door), which banks it.</summary>
        Bank = 1,
        /// <summary>Enough banked: walk to the gate and pay.</summary>
        Unlock = 2,
        /// <summary>Access bought: go through the gate.</summary>
        PassThrough = 3,
        /// <summary>Through the gate. The session's ending.</summary>
        Complete = 4,
    }

    /// <summary>
    /// Tunables for the expedition goal.
    ///
    /// Default tuning (40 Yellow gate, 1 Yellow per Wisp, 4x4 grid of 250 m zones) aims at
    /// 15–20 minutes for a solo player landing 10–15% of shots:
    /// <list type="bullet">
    /// <item>A Wisp has 30 health and the arena gun does 10 a shot, so 3 hits; at ~12% that's ~25
    /// shots. A Wisp is hittable for 0.6 s after each 1.2–2 s blink and the gun fires every 0.12 s,
    /// so ~5 shots and ~0.6 hits per window: ~5 windows, ~11 s of fighting per kill. Add ~9 s to
    /// find and reach the next one: ~20 s a kill, about 3 kills a minute.</item>
    /// <item>40 kills ≈ 13 minutes of hunting, plus ~3 minutes to walk the 2 zones to the gate and
    /// ~1 minute of slack: about 17 minutes. (The gate banks, so there is no detour to bank.) Blooms, other players' shared drops and
    /// chains make it faster; a bleed-out (half of carried lost to a cache) makes it slower.</item>
    /// </list>
    /// </summary>
    public sealed class ExpeditionSettings
    {
        /// <summary>The session length the HUD timer counts toward (20 minutes).</summary>
        public double TargetDuration { get; set; } = 1200.0;
        /// <summary>While gathering, point at a running bloom of the goal color this many zones away or closer.</summary>
        public int BloomDetourZones { get; set; } = 1;
    }

    /// <summary>What the HUD shows: the current step, a line of text and where to head.</summary>
    public readonly struct ExpeditionObjective
    {
        public readonly ExpeditionPhase Phase;
        public readonly string Text;
        public readonly bool HasTarget;
        /// <summary>World ground position (x, z) to point the compass at.</summary>
        public readonly Vector2 Target;
        /// <summary>Short description of the target, for example "the gate near the Old Mill in zone (1,2)".</summary>
        public readonly string TargetLabel;

        public ExpeditionObjective(ExpeditionPhase phase, string text, bool hasTarget, Vector2 target, string targetLabel)
        {
            Phase = phase;
            Text = text;
            HasTarget = hasTarget;
            Target = target;
            TargetLabel = targetLabel;
        }

        public override string ToString() => Text;
    }

    /// <summary>
    /// One player's goal for the MVP step-4 session: gather the gate's color, bank it (at the gate or raid door),
    /// pay at the gate, go through. The phase is worked out from the wallet and gate each update, so
    /// it moves back if the player bleeds out and loses carried essence, or gifts it away. Records
    /// when each phase was first reached and when the run finished.
    /// </summary>
    public sealed class ExpeditionTracker
    {
        private readonly LayerMap _map;
        private readonly Gate _gate;
        private readonly EssenceWallet _wallet;
        private readonly BloomSchedule _blooms;
        private readonly ExpeditionSettings _settings;
        private readonly Banking _banking;
        private readonly double?[] _reachedAt = new double?[5];
        private double? _completedAt;

        public ExpeditionTracker(LayerMap map, Gate gate, EssenceWallet wallet, double startTime,
            BloomSchedule blooms = null, ExpeditionSettings settings = null, Banking banking = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _blooms = blooms;
            _banking = banking;
            _settings = settings ?? new ExpeditionSettings();
            StartedAt = startTime;
            Phase = CurrentPhase();
            _reachedAt[(int)Phase] = startTime;
        }

        public int PlayerId => _wallet.PlayerId;
        public double StartedAt { get; private set; }

        /// <summary>
        /// The gate shut: if this player had finished, a new run starts now (gather for the next,
        /// pricier opening). A player who hadn't finished keeps their run; their phase follows the gate.
        /// </summary>
        public void GateClosed(double now)
        {
            if (!IsComplete) return;
            _completedAt = null;
            StartedAt = now;
            for (int i = 0; i < _reachedAt.Length; i++) _reachedAt[i] = null;
            Phase = CurrentPhase();
            _reachedAt[(int)Phase] = now;
        }
        public ExpeditionPhase Phase { get; private set; }
        public bool IsComplete => _completedAt.HasValue;
        /// <summary>When the player went through the gate, or null.</summary>
        public double? CompletedAt => _completedAt;
        public EssenceColor GoalColor => _gate.Color;
        public int GoalAmount => _gate.Cost;
        /// <summary>The latest objective from <see cref="Update"/>.</summary>
        public ExpeditionObjective Objective { get; private set; }

        /// <summary>Goal color in hand, carried plus banked, capped at the goal (the cost once access is bought).</summary>
        public int Collected => _gate.HasAccess(PlayerId) ? GoalAmount : Math.Min(GoalAmount, _wallet.Total(GoalColor));

        /// <summary>When a phase was first reached, or null if it hasn't been yet.</summary>
        public double? PhaseReachedAt(ExpeditionPhase phase) => _reachedAt[(int)phase];

        /// <summary>Seconds since the start, frozen once complete.</summary>
        public double Elapsed(double now) => (_completedAt ?? now) - StartedAt;

        /// <summary>Seconds left of the target duration (negative once over).</summary>
        public double Remaining(double now) => _settings.TargetDuration - Elapsed(now);

        /// <summary>For example "12:34 / 20:00".</summary>
        public string TimerText(double now) => $"{Clock(Elapsed(now))} / {Clock(_settings.TargetDuration)}";

        /// <summary>
        /// Call when the player walks through the gate. Only counts with access; returns true the
        /// first time it completes the expedition.
        /// </summary>
        public bool PassedGate(double now)
        {
            if (IsComplete || !_gate.HasAccess(PlayerId)) return false;
            _completedAt = now;
            Note(ExpeditionPhase.Complete, now);
            Phase = ExpeditionPhase.Complete;
            return true;
        }

        /// <summary>Re-reads the wallet and gate, and works out the objective from the player's position.</summary>
        public ExpeditionObjective Update(double now, Vector2 position)
        {
            Phase = CurrentPhase();
            Note(Phase, now);
            Objective = Build(now, position);
            return Objective;
        }

        private ExpeditionPhase CurrentPhase()
        {
            if (IsComplete) return ExpeditionPhase.Complete;
            if (_gate.HasAccess(PlayerId)) return ExpeditionPhase.PassThrough;
            if (_wallet.Banked(GoalColor) >= GoalAmount) return ExpeditionPhase.Unlock;
            if (_wallet.Total(GoalColor) >= GoalAmount) return ExpeditionPhase.Bank;
            return ExpeditionPhase.Gather;
        }

        private void Note(ExpeditionPhase phase, double now)
        {
            if (!_reachedAt[(int)phase].HasValue) _reachedAt[(int)phase] = now;
        }

        private ExpeditionObjective Build(double now, Vector2 position)
        {
            var color = GoalColor;
            int carried = _wallet.Carried(color);
            int banked = _wallet.Banked(color);
            var gatePoint = _gate.Site.Point.World;
            string gatePlace = _map.Describe(gatePoint);

            switch (Phase)
            {
                case ExpeditionPhase.Gather:
                {
                    string text = $"Gather {color} essence: {Collected}/{GoalAmount} ({carried} carried, {banked} banked)";
                    if (TryHuntTarget(now, position, out var target, out string label))
                        return new ExpeditionObjective(Phase, $"{text}. Head for {label}", true, target, label);
                    return new ExpeditionObjective(Phase, text, false, default, null);
                }
                case ExpeditionPhase.Bank:
                {
                    // The gate banks, so carrying enough means heading straight there.
                    string label = $"the gate {gatePlace}";
                    double locked = _banking != null ? _banking.LockedFor(PlayerId, now) : 0.0;
                    string text = locked > 0.0
                        ? $"{Banking.LockoutText(locked)}. Then take your {color} to {label}"
                        : $"Take your {color} to {label} to bank it: {carried} carried, {banked}/{GoalAmount} banked";
                    return new ExpeditionObjective(Phase, text, true, gatePoint, label);
                }
                case ExpeditionPhase.Unlock:
                {
                    string label = $"the gate {gatePlace}";
                    return new ExpeditionObjective(Phase,
                        $"Open {label}: costs {GoalAmount} {color}, you have {banked} banked",
                        true, gatePoint, label);
                }
                case ExpeditionPhase.PassThrough:
                {
                    string label = $"the gate {gatePlace}";
                    // Access without a running opening: the group came back up through this gate, and it stays open to them.
                    string open = _gate.IsOpen ? $"Gate open for {(int)Math.Ceiling(_gate.OpenLeft(now))}s" : "The gate is open to you";
                    return new ExpeditionObjective(Phase, $"{open}. Go through {label}", true, gatePoint, label);
                }
                default:
                    return new ExpeditionObjective(Phase,
                        $"Expedition complete in {Clock(Elapsed(now))}. The gate shuts in {(int)Math.Ceiling(_gate.OpenLeft(now))}s; " +
                        $"reopening costs {_gate.Cost + _gate.CostStep} {color}", false, default, null);
            }
        }

        /// <summary>A nearby running bloom of the goal color if there is one, else the nearest place its family spawns.</summary>
        private bool TryHuntTarget(double now, Vector2 position, out Vector2 target, out string label)
        {
            var color = GoalColor;
            var bloom = _blooms?.NearestActive(position, color, now, _settings.BloomDetourZones);
            if (bloom != null)
            {
                target = bloom.Point.World;
                label = $"the {color} bloom {_map.Describe(target)}";
                return true;
            }
            if (EssenceColors.TryGetFamily(color, out EnemyFamily family))
            {
                var spawn = _map.NearestSpawnPoint(position, family);
                if (spawn != null)
                {
                    target = spawn.Point.World;
                    label = $"{family}s {_map.Describe(target)}";
                    return true;
                }
            }
            var zone = _map.NearestZoneLeaningToward(color, position);
            if (zone != null)
            {
                target = _map.ZoneCentre(zone.Coord);
                label = $"{color} zone {zone.Coord}";
                return true;
            }
            target = default;
            label = null;
            return false;
        }

        private static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Floor(seconds);
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
