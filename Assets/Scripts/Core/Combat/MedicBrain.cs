using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Medic bot's choices. Metres and seconds.</summary>
    public sealed class MedicSettings
    {
        /// <summary>The Medic only considers targets this close.</summary>
        public float Range { get; set; } = 30f;
        /// <summary>Allies below this health fraction get healed (Vigor only).</summary>
        public float HealBelow { get; set; } = 0.7f;
        /// <summary>An ally's buff is refreshed when it has less than this long left (or is missing).</summary>
        public float RefreshBelow { get; set; } = 2f;
        /// <summary>A chain holder's buff is topped up unless it was given within this many seconds.</summary>
        public float HolderFreshFor { get; set; } = 1f;
        /// <summary>Fragments about to fade are skipped: they'd be gone before the flick lands.</summary>
        public float MinFragmentTimeLeft { get; set; } = 0.4f;
    }

    /// <summary>What a Medic bot sees of an ally.</summary>
    public struct MedicAlly
    {
        public int Id;
        /// <summary>Where to aim at them (body centre).</summary>
        public Vector3 Position;
        public float HealthFraction;
        public bool IsDown;
        /// <summary>Their soul fragments while down (may be null).</summary>
        public IReadOnlyList<SoulFragment> Fragments;
        /// <summary>Their buff slots (may be null when unknown).</summary>
        public EffectSet Buffs;
    }

    /// <summary>What a Medic bot sees of an enemy.</summary>
    public struct MedicEnemy
    {
        public int Id;
        /// <summary>Where to aim (weak point if the bot wants it, else body centre).</summary>
        public Vector3 Position;
        public bool Hittable;
    }

    /// <summary>Why the Medic picked its target, in priority order.</summary>
    public enum MedicTargetKind : byte
    {
        None = 0,
        /// <summary>A downed ally's soul fragment. TargetId is the fragment id, OwnerId the downed ally.</summary>
        Fragment,
        /// <summary>The most hurt ally below the heal threshold (Vigor only).</summary>
        Heal,
        /// <summary>The Medic holds a chain: buff an ally not yet in it so the chain grows.</summary>
        ExtendChain,
        /// <summary>An ally holds a chain: keep their buff fresh for the cashing hit.</summary>
        ChainHolder,
        /// <summary>The ally whose buff from this payload runs out soonest.</summary>
        Buff,
        /// <summary>The nearest hittable enemy.</summary>
        Enemy,
    }

    /// <summary>What a Medic bot should shoot next.</summary>
    public struct MedicDecision
    {
        public MedicTargetKind Kind;
        /// <summary>Ally id, enemy id or fragment id (see <see cref="Kind"/>).</summary>
        public int TargetId;
        /// <summary>For fragments: the downed ally they belong to. Otherwise the same as TargetId.</summary>
        public int OwnerId;
        public Vector3 AimPoint;
    }

    /// <summary>
    /// Decides what a Medic bot shoots next. Priority:
    /// 1. Fragments of a downed ally in range (nearest first).
    /// 2. Heal the most hurt ally below <see cref="MedicSettings.HealBelow"/> (only with Vigor).
    /// 3. If the Medic holds a chain, buff the nearest ally not in it (the chain grows).
    /// 4. If an ally holds a chain, top up their buff (unless it's fresh) so their cash hits hard.
    /// 5. Keep buffs up: the ally whose buff from this payload expires soonest, if under
    ///    <see cref="MedicSettings.RefreshBelow"/>.
    /// 6. Shoot the nearest hittable enemy.
    /// Buffing a chain holder never grows their chain (only the holder can pass it on), which is why
    /// rule 3 is about the Medic's own chain and rule 4 only keeps the holder's buff fresh.
    /// </summary>
    public static class MedicBrain
    {
        public static MedicDecision Decide(int selfId, Vector3 selfPosition, PayloadKind payload,
            IReadOnlyList<MedicAlly> allies, IReadOnlyList<MedicEnemy> enemies, ChainTracker chains, double time,
            MedicSettings s, CombatSettings combat)
        {
            float range2 = s.Range * s.Range;

            // 1. Fragments.
            float best = float.MaxValue;
            var decision = new MedicDecision();
            for (int i = 0; i < allies.Count; i++)
            {
                var a = allies[i];
                if (a.Id == selfId || !a.IsDown || a.Fragments == null) continue;
                for (int f = 0; f < a.Fragments.Count; f++)
                {
                    var frag = a.Fragments[f];
                    if (frag.TimeLeft < s.MinFragmentTimeLeft) continue;
                    float d2 = Vector3.DistanceSquared(selfPosition, frag.Position);
                    if (d2 > range2 || d2 >= best) continue;
                    best = d2;
                    decision = new MedicDecision { Kind = MedicTargetKind.Fragment, TargetId = frag.Id, OwnerId = a.Id, AimPoint = frag.Position };
                }
            }
            if (decision.Kind != MedicTargetKind.None) return decision;

            // 2. Heal.
            if (payload == PayloadKind.Vigor)
            {
                int pick = -1;
                float worst = s.HealBelow;
                for (int i = 0; i < allies.Count; i++)
                {
                    if (!CanBuff(allies[i], selfId, selfPosition, range2)) continue;
                    if (allies[i].HealthFraction < worst)
                    {
                        worst = allies[i].HealthFraction;
                        pick = i;
                    }
                }
                if (pick >= 0) return Ally(MedicTargetKind.Heal, allies[pick]);
            }

            var effect = Payloads.AllyEffect(payload);
            if (chains != null)
            {
                // 3. Grow our own chain.
                if (chains.TryGetHeld(selfId, time, out _))
                {
                    int pick = Nearest(allies, selfId, selfPosition, range2, a => chains.WouldGrow(selfId, a.Id, time));
                    if (pick >= 0) return Ally(MedicTargetKind.ExtendChain, allies[pick]);
                }

                // 4. Keep a chain holder's buff fresh.
                float fresh = BuffDuration(payload, combat) - s.HolderFreshFor;
                int holder = Nearest(allies, selfId, selfPosition, range2,
                    a => chains.TryGetHeld(a.Id, time, out _) && Remaining(a, effect) < fresh);
                if (holder >= 0) return Ally(MedicTargetKind.ChainHolder, allies[holder]);
            }

            // 5. Buff upkeep.
            {
                int pick = -1;
                float soonest = s.RefreshBelow;
                float pickDist = float.MaxValue;
                for (int i = 0; i < allies.Count; i++)
                {
                    var a = allies[i];
                    if (!CanBuff(a, selfId, selfPosition, range2)) continue;
                    float left = Remaining(a, effect);
                    float d2 = Vector3.DistanceSquared(selfPosition, a.Position);
                    if (left < soonest || (pick >= 0 && left == soonest && d2 < pickDist))
                    {
                        soonest = left;
                        pick = i;
                        pickDist = d2;
                    }
                }
                if (pick >= 0) return Ally(MedicTargetKind.Buff, allies[pick]);
            }

            // 6. Enemies.
            best = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.Hittable) continue;
                float d2 = Vector3.DistanceSquared(selfPosition, e.Position);
                if (d2 > range2 || d2 >= best) continue;
                best = d2;
                decision = new MedicDecision { Kind = MedicTargetKind.Enemy, TargetId = e.Id, OwnerId = e.Id, AimPoint = e.Position };
            }
            return decision;
        }

        private static bool CanBuff(in MedicAlly a, int selfId, Vector3 selfPosition, float range2)
        {
            return a.Id != selfId && !a.IsDown && Vector3.DistanceSquared(selfPosition, a.Position) <= range2;
        }

        private static float Remaining(in MedicAlly a, EffectKind effect)
        {
            return a.Buffs == null ? 0f : a.Buffs.RemainingOf(effect);
        }

        private static int Nearest(IReadOnlyList<MedicAlly> allies, int selfId, Vector3 selfPosition, float range2,
            System.Func<MedicAlly, bool> want)
        {
            int pick = -1;
            float best = float.MaxValue;
            for (int i = 0; i < allies.Count; i++)
            {
                var a = allies[i];
                if (!CanBuff(a, selfId, selfPosition, range2) || !want(a)) continue;
                float d2 = Vector3.DistanceSquared(selfPosition, a.Position);
                if (d2 >= best) continue;
                best = d2;
                pick = i;
            }
            return pick;
        }

        private static MedicDecision Ally(MedicTargetKind kind, in MedicAlly a)
        {
            return new MedicDecision { Kind = kind, TargetId = a.Id, OwnerId = a.Id, AimPoint = a.Position };
        }

        private static float BuffDuration(PayloadKind payload, CombatSettings c)
        {
            switch (payload)
            {
                case PayloadKind.Vigor: return c.ShieldDuration;
                case PayloadKind.Haste: return c.HasteDuration;
                case PayloadKind.Relay: return c.RelayDuration;
                default: return c.MightDuration;
            }
        }
    }
}
