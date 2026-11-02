namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>What one bullet hit did, so the game can show hit markers, numbers and sounds.</summary>
    public struct HitResult
    {
        public PayloadKind Payload;
        public bool TargetIsPlayer;
        public int TargetId;

        // Enemy hits.
        /// <summary>Health the enemy lost from the bullet itself (Wither comes later, in ticks).</summary>
        public float DamageDealt;
        public bool WeakPoint;
        public bool Killed;

        // Player hits.
        public float Healed;
        public float ShieldGained;

        /// <summary>Which effect the payload tried to apply (meaningless when Effect is None).</summary>
        public EffectKind Effect;
        public EffectOutcome EffectOutcome;
        public RefuseReason Refused;
    }

    /// <summary>
    /// Turns "this shooter's bullet hit that target" into changes to health and effects.
    /// Enemy damage = base x (1 + shooter's Empower) x (1 + target's Expose) x weak-point multiplier.
    /// The bullet's own debuff lands after its damage, so a first Might shot isn't boosted by the
    /// Expose it applies.
    /// </summary>
    public sealed class HitResolver
    {
        public HitResolver(CombatSettings settings)
        {
            Settings = settings;
        }

        public CombatSettings Settings { get; }

        /// <summary>Damage a hit would deal before the target's health caps it.</summary>
        public float EnemyDamage(PlayerVitals shooter, EnemyVitals target, float baseDamage, bool weakPoint)
        {
            float damage = baseDamage * (1f + shooter.DamageBonus) * target.DamageTakenMultiplier;
            if (weakPoint) damage *= Settings.WeakPointMultiplier;
            return damage;
        }

        public HitResult HitEnemy(PlayerVitals shooter, EnemyVitals target, PayloadKind payload, float baseDamage, bool weakPoint)
        {
            var result = new HitResult
            {
                Payload = payload,
                TargetIsPlayer = false,
                TargetId = target.Id,
                WeakPoint = weakPoint,
                Effect = payload == PayloadKind.Vigor ? EffectKind.Wither : EffectKind.Expose,
            };
            if (target.IsDead)
            {
                result.EffectOutcome = EffectOutcome.Refused;
                result.Refused = RefuseReason.TargetDead;
                return result;
            }

            result.DamageDealt = target.TakeDamage(EnemyDamage(shooter, target, baseDamage, weakPoint));
            result.Killed = target.IsDead;
            if (result.Killed)
            {
                // No point debuffing a corpse.
                result.EffectOutcome = EffectOutcome.None;
                return result;
            }

            var s = Settings;
            result.EffectOutcome = payload == PayloadKind.Vigor
                ? target.Debuffs.Apply(EffectKind.Wither, shooter.Id, s.WitherDps, s.WitherDuration)
                : target.Debuffs.Apply(EffectKind.Expose, shooter.Id, s.ExposeBonus, s.ExposeDuration);
            return result;
        }

        /// <summary>Why <paramref name="target"/> would refuse a buff from <paramref name="shooter"/>, or None.</summary>
        public static RefuseReason CheckBuff(PlayerVitals shooter, PlayerVitals target)
        {
            if (shooter.Id == target.Id) return RefuseReason.SelfBuff;
            if (target.IsDown) return RefuseReason.TargetDown;
            switch (target.Acceptance)
            {
                case BuffAcceptance.NoOne: return RefuseReason.OptedOut;
                case BuffAcceptance.PartyOnly:
                    return target.InPartyWith(shooter) ? RefuseReason.None : RefuseReason.NotInParty;
                default: return RefuseReason.None;
            }
        }

        public HitResult HitPlayer(PlayerVitals shooter, PlayerVitals target, PayloadKind payload)
        {
            var result = new HitResult
            {
                Payload = payload,
                TargetIsPlayer = true,
                TargetId = target.Id,
                Effect = payload == PayloadKind.Vigor ? EffectKind.Shield : EffectKind.Empower,
            };

            result.Refused = CheckBuff(shooter, target);
            if (result.Refused != RefuseReason.None)
            {
                result.EffectOutcome = EffectOutcome.Refused;
                return result;
            }

            var s = Settings;
            if (payload == PayloadKind.Vigor)
            {
                float overflow = target.Heal(s.VigorHeal, out float healed);
                result.Healed = healed;
                if (overflow > 0f)
                {
                    result.ShieldGained = target.Buffs.AddShield(shooter.Id, overflow, s.ShieldCap, s.ShieldDuration, out var outcome);
                    result.EffectOutcome = outcome;
                }
                else
                {
                    result.EffectOutcome = EffectOutcome.None;
                }
            }
            else
            {
                result.EffectOutcome = target.Buffs.Apply(EffectKind.Empower, shooter.Id, s.MightDamageBonus, s.MightDuration);
            }
            return result;
        }
    }
}
