using System.Collections.Generic;
using System.Numerics;

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
        /// <summary>
        /// The target was Tagged when hit: deal this much to each enemy near it (see
        /// <see cref="HitResolver.SplashTargets"/> and <see cref="HitResolver.ApplySplash"/>). 0 otherwise.
        /// </summary>
        public float SplashDamage;

        // Player hits.
        /// <summary>Health restored, by the shooter's own Vigor and by a relayed Vigor together.</summary>
        public float Healed;
        /// <summary>Shield added, by the shooter's own Vigor and by a relayed Vigor together.</summary>
        public float ShieldGained;

        /// <summary>Which effect the payload tried to apply (meaningless when Effect is None).</summary>
        public EffectKind Effect;
        public EffectOutcome EffectOutcome;
        public RefuseReason Refused;

        // Relay.
        /// <summary>True when the shooter was Relayed: this hit also carried <see cref="RelayedPayload"/> and used up one relayed hit.</summary>
        public bool Relayed;
        public PayloadKind RelayedPayload;
        /// <summary>The relayed payload's effect on the target.</summary>
        public EffectKind RelayedEffect;
        /// <summary>
        /// What happened to the relayed effect. None when the hit killed the enemy; Refused when the
        /// target is the relayer themselves (no self-buffing).
        /// </summary>
        public EffectOutcome RelayedOutcome;

        // Chains (filled in by LobbyCombat; see ChainTracker).
        /// <summary>Bonus fraction this hit got from cashing a chain (weak-point factor included). 0 if none.</summary>
        public float ChainBonus;
        /// <summary>Links in the chain this hit cashed (enemy hits) or now sits in (ally hits). 0 if none.</summary>
        public int ChainLinks;
        /// <summary>What this hit did to chains.</summary>
        public ChainEvent ChainEvent;
    }

    /// <summary>
    /// Turns "this shooter's bullet hit that target" into changes to health and effects.
    /// Enemy damage = base x (1 + shooter's Empower, doubled under Blitz) x (1 + target's Expose)
    /// x weak-point multiplier x (1 + chain bonus).
    /// The bullet's own debuff lands after its damage, so a first Might shot isn't boosted by the
    /// Expose it applies, and a first Relay shot doesn't splash from the Tag it applies.
    ///
    /// Relay: while the shooter is Relayed, each hit (on an enemy, an ally or a soul fragment) also
    /// applies the relayed payload's normal effect, with the relayer as its source, and uses up one
    /// relayed hit. Misses don't use any up.
    ///
    /// Chains and combos live per lobby; <see cref="LobbyCombat"/> wraps this class to feed them.
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

        /// <summary>Same, with a chain bonus fraction (from <see cref="ChainTracker.OnEnemyHit"/>) on top.</summary>
        public float EnemyDamage(PlayerVitals shooter, EnemyVitals target, float baseDamage, bool weakPoint, float chainBonus)
        {
            return EnemyDamage(shooter, target, baseDamage, weakPoint) * (1f + chainBonus);
        }

        public HitResult HitEnemy(PlayerVitals shooter, EnemyVitals target, PayloadKind payload, float baseDamage, bool weakPoint)
        {
            return HitEnemy(shooter, target, payload, baseDamage, weakPoint, 0f);
        }

        /// <summary>An enemy hit that also cashes a chain for <paramref name="chainBonus"/> (0.15 = +15%).</summary>
        public HitResult HitEnemy(PlayerVitals shooter, EnemyVitals target, PayloadKind payload, float baseDamage, bool weakPoint,
            float chainBonus)
        {
            var result = new HitResult
            {
                Payload = payload,
                TargetIsPlayer = false,
                TargetId = target.Id,
                WeakPoint = weakPoint,
                Effect = Payloads.EnemyEffect(payload),
            };
            if (target.IsDead)
            {
                result.EffectOutcome = EffectOutcome.Refused;
                result.Refused = RefuseReason.TargetDead;
                return result;
            }

            // Read before the hit changes anything: Tag splashes only if it was already there.
            float splashShare = target.SplashShare;
            bool relayed = shooter.HasRelay;
            int relaySource = shooter.RelaySourceId;

            float damage = EnemyDamage(shooter, target, baseDamage, weakPoint, chainBonus);
            result.ChainBonus = chainBonus;
            result.DamageDealt = target.TakeDamage(damage);
            result.SplashDamage = damage * splashShare;
            result.Killed = target.IsDead;
            if (relayed)
            {
                result.Relayed = true;
                result.RelayedPayload = shooter.RelayPayload;
                result.RelayedEffect = Payloads.EnemyEffect(shooter.RelayPayload);
                shooter.UseRelayHit();
            }
            if (result.Killed)
            {
                // No point debuffing a corpse.
                result.EffectOutcome = EffectOutcome.None;
                return result;
            }

            result.EffectOutcome = ApplyDebuff(target, shooter.Id, payload);
            if (relayed) result.RelayedOutcome = ApplyDebuff(target, relaySource, result.RelayedPayload);
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
                Effect = Payloads.AllyEffect(payload),
            };

            result.Refused = CheckBuff(shooter, target);
            if (result.Refused != RefuseReason.None)
            {
                result.EffectOutcome = EffectOutcome.Refused;
                return result;
            }

            bool relayed = shooter.HasRelay;
            int relaySource = shooter.RelaySourceId;
            PayloadKind relayPayload = shooter.RelayPayload;

            result.EffectOutcome = ApplyBuff(target, shooter.Id, payload, shooter.RelayCarry, ref result, shooter.GivePotency, shooter.GiveDuration);
            if (relayed)
            {
                result.Relayed = true;
                result.RelayedPayload = relayPayload;
                result.RelayedEffect = Payloads.AllyEffect(relayPayload);
                shooter.UseRelayHit();
                // The relayed payload still never buffs the player who relayed it.
                result.RelayedOutcome = target.Id == relaySource
                    ? EffectOutcome.Refused
                    : ApplyBuff(target, relaySource, relayPayload, PayloadKind.Might, ref result, 1f, 1f);
            }
            return result;
        }

        /// <summary>
        /// A bullet hit one of a downed player's soul fragments. Any payload counts; Vigor (the
        /// shooter's own or a relayed one) counts double. Uses up a relayed hit if the shooter has one.
        /// </summary>
        public ReviveHit HitFragment(PlayerVitals shooter, ReviveState revive, int fragmentId, PayloadKind payload)
        {
            bool relayed = shooter.HasRelay;
            bool vigor = payload == PayloadKind.Vigor || (relayed && shooter.RelayPayload == PayloadKind.Vigor);
            int value = vigor ? revive.Settings.VigorHitValue : 1;
            var hit = revive.RegisterHit(fragmentId, shooter.Id, value);
            if (hit.Accepted && relayed) shooter.UseRelayHit();
            return hit;
        }

        /// <summary>
        /// Splash damage from a hit on a Tagged enemy (<see cref="HitResult.SplashDamage"/>). Splash
        /// is plain damage: no multipliers, no debuffs, no chain. Returns the health lost.
        /// </summary>
        public float ApplySplash(EnemyVitals target, float amount)
        {
            return target.TakeDamage(amount);
        }

        /// <summary>
        /// Lists the enemies a hit on a Tagged enemy splashes to: hittable, non-player targets other
        /// than <paramref name="hitId"/> whose body centre is within the Tag splash radius of
        /// <paramref name="center"/>. Adds their ids to <paramref name="results"/>; returns the count added.
        /// </summary>
        public int SplashTargets(Vector3 center, int hitId, IReadOnlyList<HitTarget> enemies, List<int> results)
        {
            float r2 = Settings.TagSplashRadius * Settings.TagSplashRadius;
            int added = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.IsPlayer || !e.Hittable || e.Id == hitId) continue;
                if (Vector3.DistanceSquared(e.BodyCenter, center) > r2) continue;
                results.Add(e.Id);
                added++;
            }
            return added;
        }

        private EffectOutcome ApplyDebuff(EnemyVitals target, int sourceId, PayloadKind payload)
        {
            var s = Settings;
            switch (payload)
            {
                case PayloadKind.Vigor: return target.Debuffs.Apply(EffectKind.Wither, sourceId, s.WitherDps, s.WitherDuration);
                case PayloadKind.Haste: return target.Debuffs.Apply(EffectKind.Slow, sourceId, s.SlowAmount, s.SlowDuration);
                case PayloadKind.Relay: return target.Debuffs.Apply(EffectKind.Tag, sourceId, s.TagSplashShare, s.TagDuration);
                default: return target.Debuffs.Apply(EffectKind.Expose, sourceId, s.ExposeBonus, s.ExposeDuration);
            }
        }

        /// <summary>Applies a payload's buff; heals and shield are added to <paramref name="result"/>.</summary>
        /// <param name="power">Multiplies heal and buff strength (the giver's skills).</param>
        /// <param name="time">Multiplies buff duration (the giver's skills).</param>
        private EffectOutcome ApplyBuff(PlayerVitals target, int sourceId, PayloadKind payload, PayloadKind relayCarry,
            ref HitResult result, float power, float time)
        {
            var s = Settings;
            switch (payload)
            {
                case PayloadKind.Vigor:
                {
                    float overflow = target.Heal(s.VigorHeal * power, out float healed);
                    result.Healed += healed;
                    if (overflow <= 0f) return EffectOutcome.None;
                    result.ShieldGained += target.Buffs.AddShield(sourceId, overflow, s.ShieldCap * power, s.ShieldDuration * time, out var outcome);
                    return outcome;
                }
                case PayloadKind.Haste:
                    return target.Buffs.Apply(EffectKind.Hasted, sourceId, s.HasteMoveBonus * power, s.HasteDuration * time);
                case PayloadKind.Relay:
                {
                    var outcome = target.Buffs.Apply(EffectKind.Relayed, sourceId, 1f, s.RelayDuration * time);
                    target.SetRelay(sourceId, relayCarry, s.RelayHits);
                    return outcome;
                }
                default:
                    return target.Buffs.Apply(EffectKind.Empower, sourceId, s.MightDamageBonus * power, s.MightDuration * time);
            }
        }
    }
}
