using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// One lobby's (or arena's) shared combat state: hits, chains and combo discovery. Use its
    /// <see cref="HitEnemy"/> and <see cref="HitPlayer"/> instead of calling <see cref="HitResolver"/>
    /// directly, so every hit feeds the chain tracker and combo tracker in the right order:
    /// - Relay state is read before the hit (the hit uses up a relayed hit).
    /// - An enemy hit cashes the shooter's chain and the bonus goes into that same hit's damage.
    /// - An accepted ally hit passes or starts a chain.
    /// - The target is then checked for combos that just formed.
    /// </summary>
    public sealed class LobbyCombat
    {
        public LobbyCombat(CombatSettings settings)
        {
            Settings = settings;
            Hits = new HitResolver(settings);
            Chains = new ChainTracker(settings);
            Combos = new ComboTracker();
        }

        public CombatSettings Settings { get; }
        public HitResolver Hits { get; }
        public ChainTracker Chains { get; }
        public ComboTracker Combos { get; }

        /// <summary>
        /// A bullet hit an enemy at server time <paramref name="time"/>. Combos that formed are added
        /// to <paramref name="combos"/> when it isn't null. Splash (if the enemy was Tagged) is left to
        /// the caller: see <see cref="HitResult.SplashDamage"/>.
        /// </summary>
        public HitResult HitEnemy(PlayerVitals shooter, EnemyVitals target, PayloadKind payload, float baseDamage, bool weakPoint,
            double time, List<ComboEvent> combos = null)
        {
            return HitEnemy(shooter, target, ShotEffects.Of(payload), baseDamage, weakPoint, time, combos);
        }

        /// <summary>An enemy hit carrying <paramref name="fx"/> (a found gun's payloads at their strengths).</summary>
        public HitResult HitEnemy(PlayerVitals shooter, EnemyVitals target, ShotEffects fx, float baseDamage, bool weakPoint,
            double time, List<ComboEvent> combos = null)
        {
            if (target.IsDead) return Hits.HitEnemy(shooter, target, fx, baseDamage, weakPoint, 0f);

            var cash = Chains.OnEnemyHit(shooter.Id, time, weakPoint, shooter.HasRelay);
            var result = Hits.HitEnemy(shooter, target, fx, baseDamage, weakPoint, cash.Bonus);
            if (cash.Cashed)
            {
                result.ChainEvent = ChainEvent.Cashed;
                result.ChainLinks = cash.Links;
            }
            Combos.Check(target, combos);
            return result;
        }

        /// <summary>A bullet hit another player at server time <paramref name="time"/>.</summary>
        public HitResult HitPlayer(PlayerVitals shooter, PlayerVitals target, PayloadKind payload, double time,
            List<ComboEvent> combos = null)
        {
            return HitPlayer(shooter, target, ShotEffects.Of(payload), time, combos);
        }

        /// <summary>
        /// An ally hit carrying <paramref name="fx"/>. A hit with no payload (and no relayed one) only
        /// heals a tiny amount: it starts and passes no chain. <paramref name="conductor"/> gives a chain
        /// this hit starts one extra link.
        /// </summary>
        public HitResult HitPlayer(PlayerVitals shooter, PlayerVitals target, ShotEffects fx, double time,
            List<ComboEvent> combos = null, bool conductor = false)
        {
            bool relayed = shooter.HasRelay;
            var result = Hits.HitPlayer(shooter, target, fx);
            if (result.Refused != RefuseReason.None) return result;
            if (!fx.HasPayload && !relayed) return result;

            // The link's colour: the shot's payload, or Relay for a plain shot that a Relay carried.
            var linkPayload = fx.Payload != PayloadKind.None ? fx.Payload : fx.Second != PayloadKind.None ? fx.Second : PayloadKind.Relay;
            var chain = Chains.OnBuff(shooter.Id, target.Id, time, relayed, conductor, linkPayload);
            result.ChainEvent = chain.Event;
            result.ChainLinks = chain.Links;
            Combos.Check(target, combos);
            return result;
        }

        /// <summary>A bullet hit one of a downed player's soul fragments (see <see cref="HitResolver.HitFragment"/>).</summary>
        public ReviveHit HitFragment(PlayerVitals shooter, ReviveState revive, int fragmentId, PayloadKind payload)
        {
            return Hits.HitFragment(shooter, revive, fragmentId, payload);
        }

        /// <summary>Call when a player goes down or leaves: the chain they held ends.</summary>
        public void PlayerGone(int playerId)
        {
            Chains.DropPlayer(playerId);
            Combos.Forget(playerId, true);
        }
    }
}
