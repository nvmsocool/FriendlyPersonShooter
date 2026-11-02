using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class CombatHitTests
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
        public void Damage_PlainHitDealsBaseDamage()
        {
            var shooter = new PlayerVitals(1, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var r = _hits.HitEnemy(shooter, skitter, PayloadKind.Might, 10, false);
            Assert.That(r.DamageDealt, Is.EqualTo(10));
            Assert.That(skitter.Health, Is.EqualTo(50));
            Assert.That(r.Killed, Is.False);
        }

        [Test]
        public void Damage_MultipliesMightExposeAndWeakPoint()
        {
            var buffer = new PlayerVitals(1, _s);
            var shooter = new PlayerVitals(2, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);

            _hits.HitPlayer(buffer, shooter, PayloadKind.Might);            // shooter +25%
            _hits.HitEnemy(buffer, skitter, PayloadKind.Might, 0, false);   // skitter Exposed +20%

            var r = _hits.HitEnemy(shooter, skitter, PayloadKind.Vigor, 10, true);
            Assert.That(r.DamageDealt, Is.EqualTo(10 * 1.25f * 1.2f * 2f).Within(1e-4));
            Assert.That(r.WeakPoint, Is.True);
        }

        [Test]
        public void Damage_OwnExposeLandsAfterTheShotsDamage()
        {
            var shooter = new PlayerVitals(1, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            Assert.That(_hits.HitEnemy(shooter, skitter, PayloadKind.Might, 10, false).DamageDealt, Is.EqualTo(10));
            Assert.That(_hits.HitEnemy(shooter, skitter, PayloadKind.Might, 10, false).DamageDealt, Is.EqualTo(12).Within(1e-4));
        }

        [Test]
        public void Wither_TicksDamageOverItsDuration()
        {
            var shooter = new PlayerVitals(1, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var r = _hits.HitEnemy(shooter, skitter, PayloadKind.Vigor, 0, false);
            Assert.That(r.Effect, Is.EqualTo(EffectKind.Wither));
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.Added));

            float total = 0;
            for (int i = 0; i < 60 * 7; i++) total += skitter.Tick(1f / 60f);
            Assert.That(total, Is.EqualTo(_s.WitherDps * _s.WitherDuration).Within(0.1));
            Assert.That(skitter.Debuffs.Has(EffectKind.Wither), Is.False);
        }

        [Test]
        public void Wither_FromTwoShootersStacksAndIsBoostedByExpose()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            _hits.HitEnemy(a, skitter, PayloadKind.Vigor, 0, false);
            Assert.That(_hits.HitEnemy(b, skitter, PayloadKind.Vigor, 0, false).EffectOutcome, Is.EqualTo(EffectOutcome.Stacked));
            Assert.That(skitter.Debuffs.StrengthOf(EffectKind.Wither), Is.EqualTo(6));

            _hits.HitEnemy(a, skitter, PayloadKind.Might, 0, false);
            Assert.That(skitter.Tick(1f), Is.EqualTo(6 * 1.2f).Within(1e-4));
        }

        [Test]
        public void Enemy_DebuffsFollowTheSameSlotCap()
        {
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            Assert.That(enemy.Debuffs.MaxSlots, Is.EqualTo(3));
        }

        [Test]
        public void Mites_DieInOneHit()
        {
            var shooter = new PlayerVitals(1, _s);
            var mite = new EnemyVitals(100, EnemyFamily.Mite, _s);
            var r = _hits.HitEnemy(shooter, mite, PayloadKind.Vigor, 1, false);
            Assert.That(r.Killed, Is.True);
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.None), "no debuff on a corpse");

            var again = _hits.HitEnemy(shooter, mite, PayloadKind.Vigor, 10, false);
            Assert.That(again.Refused, Is.EqualTo(RefuseReason.TargetDead));
            Assert.That(again.DamageDealt, Is.EqualTo(0));
        }

        [Test]
        public void Kill_ReportsHealthActuallyLost()
        {
            var wisp = new EnemyVitals(100, EnemyFamily.Wisp, _s);
            var r = _hits.HitEnemy(new PlayerVitals(1, _s), wisp, PayloadKind.Might, 50, false);
            Assert.That(r.Killed, Is.True);
            Assert.That(r.DamageDealt, Is.EqualTo(_s.WispHealth));
        }

        [Test]
        public void Bullets_NeverDamagePlayers()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            b.SetHealth(60);
            _hits.HitPlayer(a, b, PayloadKind.Might);
            _hits.HitPlayer(a, b, PayloadKind.Vigor);
            Assert.That(b.Health, Is.EqualTo(72));
        }

        [Test]
        public void DownPlayers_RefuseBuffs()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            b.TakeEnemyDamage(500);
            Assert.That(b.IsDown, Is.True);
            Assert.That(_hits.HitPlayer(a, b, PayloadKind.Vigor).Refused, Is.EqualTo(RefuseReason.TargetDown));
            Assert.That(b.Health, Is.EqualTo(0));
        }
    }
}
