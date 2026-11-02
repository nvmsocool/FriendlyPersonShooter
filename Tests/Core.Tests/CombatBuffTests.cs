using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class CombatBuffTests
    {
        private CombatSettings _s;
        private HitResolver _hits;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _hits = new HitResolver(_s);
        }

        [Test]
        public void EffectSet_FourthEffectReplacesOldest()
        {
            var set = new EffectSet(3);
            Assert.That(set.Apply(EffectKind.Shield, 1, 5, 4), Is.EqualTo(EffectOutcome.Added));
            Assert.That(set.Apply(EffectKind.Empower, 1, 0.25f, 6), Is.EqualTo(EffectOutcome.Added));
            Assert.That(set.Apply(EffectKind.Wither, 1, 4, 5), Is.EqualTo(EffectOutcome.Added));
            Assert.That(set.Apply(EffectKind.Expose, 1, 0.2f, 6), Is.EqualTo(EffectOutcome.ReplacedOldest));
            Assert.That(set.Count, Is.EqualTo(3));
            Assert.That(set.Has(EffectKind.Shield), Is.False);
            Assert.That(set.Has(EffectKind.Expose), Is.True);
        }

        [Test]
        public void EffectSet_RefreshingAnEffectMakesItYoungest()
        {
            var set = new EffectSet(3);
            set.Apply(EffectKind.Shield, 1, 5, 4);
            set.Apply(EffectKind.Empower, 1, 0.25f, 6);
            set.Apply(EffectKind.Wither, 1, 4, 5);
            set.Apply(EffectKind.Shield, 1, 5, 4); // refresh: Empower is now oldest
            set.Apply(EffectKind.Expose, 1, 0.2f, 6);
            Assert.That(set.Has(EffectKind.Shield), Is.True);
            Assert.That(set.Has(EffectKind.Empower), Is.False);
        }

        [Test]
        public void EffectSet_SameSourceOnlyRefreshesDuration()
        {
            var set = new EffectSet(3);
            set.Apply(EffectKind.Empower, 7, 0.25f, 6);
            set.Tick(4);
            Assert.That(set.Apply(EffectKind.Empower, 7, 0.25f, 6), Is.EqualTo(EffectOutcome.Refreshed));
            Assert.That(set.StrengthOf(EffectKind.Empower), Is.EqualTo(0.25f));
            Assert.That(set.RemainingOf(EffectKind.Empower), Is.EqualTo(6f));
        }

        [Test]
        public void EffectSet_SecondSourceAddsHalfAndCapsAtDouble()
        {
            var set = new EffectSet(3);
            set.Apply(EffectKind.Empower, 1, 0.25f, 6);
            set.Tick(3);
            Assert.That(set.Apply(EffectKind.Empower, 2, 0.25f, 6), Is.EqualTo(EffectOutcome.Stacked));
            Assert.That(set.StrengthOf(EffectKind.Empower), Is.EqualTo(0.375f).Within(1e-6));
            Assert.That(set.RemainingOf(EffectKind.Empower), Is.EqualTo(6f));

            set.Apply(EffectKind.Empower, 3, 0.25f, 6);
            Assert.That(set.StrengthOf(EffectKind.Empower), Is.EqualTo(0.5f).Within(1e-6));
            set.Apply(EffectKind.Empower, 4, 0.25f, 6);
            set.Apply(EffectKind.Empower, 5, 0.25f, 6);
            Assert.That(set.StrengthOf(EffectKind.Empower), Is.EqualTo(0.5f).Within(1e-6), "capped at 2x one source");
        }

        [Test]
        public void EffectSet_EffectsExpire()
        {
            var set = new EffectSet(3);
            set.Apply(EffectKind.Empower, 1, 0.25f, 6);
            set.Apply(EffectKind.Shield, 1, 10, 4);
            set.Tick(3.9f);
            Assert.That(set.Count, Is.EqualTo(2));
            set.Tick(0.2f);
            Assert.That(set.Has(EffectKind.Shield), Is.False);
            Assert.That(set.Has(EffectKind.Empower), Is.True);
            set.Tick(2f);
            Assert.That(set.Count, Is.EqualTo(0));
        }

        [Test]
        public void Player_CannotBuffThemselves()
        {
            var a = new PlayerVitals(1, _s);
            a.SetHealth(50);
            var r = _hits.HitPlayer(a, a, PayloadKind.Vigor);
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.Refused));
            Assert.That(r.Refused, Is.EqualTo(RefuseReason.SelfBuff));
            Assert.That(a.Health, Is.EqualTo(50));
            Assert.That(_hits.HitPlayer(a, a, PayloadKind.Might).Refused, Is.EqualTo(RefuseReason.SelfBuff));
            Assert.That(a.DamageBonus, Is.EqualTo(0));
        }

        [Test]
        public void Player_OptOutRefusesBuffs()
        {
            var shooter = new PlayerVitals(1, _s, partyId: 10);
            var friend = new PlayerVitals(2, _s, partyId: 10);
            var stranger = new PlayerVitals(3, _s);

            friend.Acceptance = BuffAcceptance.NoOne;
            Assert.That(_hits.HitPlayer(shooter, friend, PayloadKind.Might).Refused, Is.EqualTo(RefuseReason.OptedOut));

            friend.Acceptance = BuffAcceptance.PartyOnly;
            Assert.That(_hits.HitPlayer(shooter, friend, PayloadKind.Might).EffectOutcome, Is.EqualTo(EffectOutcome.Added));

            stranger.Acceptance = BuffAcceptance.PartyOnly;
            Assert.That(_hits.HitPlayer(shooter, stranger, PayloadKind.Might).Refused, Is.EqualTo(RefuseReason.NotInParty));

            stranger.Acceptance = BuffAcceptance.Everyone;
            Assert.That(_hits.HitPlayer(shooter, stranger, PayloadKind.Might).Refused, Is.EqualTo(RefuseReason.None));
            Assert.That(stranger.DamageBonus, Is.EqualTo(0.25f));
        }

        [Test]
        public void PartyOnly_SoloPlayersAreNotAParty()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s) { Acceptance = BuffAcceptance.PartyOnly };
            Assert.That(HitResolver.CheckBuff(a, b), Is.EqualTo(RefuseReason.NotInParty));
        }

        [Test]
        public void Vigor_HealsAndOverflowBecomesCappedShield()
        {
            var healer = new PlayerVitals(1, _s);
            var ally = new PlayerVitals(2, _s);
            ally.SetHealth(95);

            var r = _hits.HitPlayer(healer, ally, PayloadKind.Vigor);
            Assert.That(r.Healed, Is.EqualTo(5));
            Assert.That(r.ShieldGained, Is.EqualTo(7));
            Assert.That(ally.Health, Is.EqualTo(100));
            Assert.That(ally.Shield, Is.EqualTo(7));

            _hits.HitPlayer(healer, ally, PayloadKind.Vigor); // 19
            r = _hits.HitPlayer(healer, ally, PayloadKind.Vigor); // capped at 25
            Assert.That(r.ShieldGained, Is.EqualTo(6));
            Assert.That(ally.Shield, Is.EqualTo(25));
            r = _hits.HitPlayer(healer, ally, PayloadKind.Vigor);
            Assert.That(r.ShieldGained, Is.EqualTo(0));
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.Refreshed));
        }

        [Test]
        public void Vigor_HealBelowMaxGivesNoShield()
        {
            var ally = new PlayerVitals(2, _s);
            ally.SetHealth(40);
            var r = _hits.HitPlayer(new PlayerVitals(1, _s), ally, PayloadKind.Vigor);
            Assert.That(r.Healed, Is.EqualTo(12));
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.None));
            Assert.That(ally.Shield, Is.EqualTo(0));
        }

        [Test]
        public void Shield_AbsorbsEnemyDamageFirstAndExpires()
        {
            var ally = new PlayerVitals(2, _s);
            _hits.HitPlayer(new PlayerVitals(1, _s), ally, PayloadKind.Vigor); // full health: 12 shield

            Assert.That(ally.TakeEnemyDamage(8), Is.EqualTo(0));
            Assert.That(ally.Shield, Is.EqualTo(4));
            Assert.That(ally.TakeEnemyDamage(10), Is.EqualTo(6));
            Assert.That(ally.Health, Is.EqualTo(94));
            Assert.That(ally.Buffs.Has(EffectKind.Shield), Is.False, "an empty shield frees its slot");

            _hits.HitPlayer(new PlayerVitals(1, _s), ally, PayloadKind.Vigor);
            _hits.HitPlayer(new PlayerVitals(1, _s), ally, PayloadKind.Vigor);
            Assert.That(ally.Shield, Is.GreaterThan(0));
            ally.Tick(_s.ShieldDuration + 0.01f);
            Assert.That(ally.Shield, Is.EqualTo(0));
        }

        [Test]
        public void Might_BuffExpiresAfterItsDuration()
        {
            var ally = new PlayerVitals(2, _s);
            _hits.HitPlayer(new PlayerVitals(1, _s), ally, PayloadKind.Might);
            ally.Tick(5.9f);
            Assert.That(ally.DamageBonus, Is.EqualTo(0.25f));
            ally.Tick(0.2f);
            Assert.That(ally.DamageBonus, Is.EqualTo(0));
        }

        [Test]
        public void Player_HasAtMostThreeBuffSlots()
        {
            var ally = new PlayerVitals(2, _s);
            Assert.That(ally.Buffs.MaxSlots, Is.EqualTo(3));
        }
    }
}
