using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class CombatHasteRelayTests
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
        public void Haste_SpeedsUpAllyMovesAndJumpsUntilItExpires()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            Assert.That(b.MoveSpeedMultiplier, Is.EqualTo(1f));
            var r = _hits.HitPlayer(a, b, PayloadKind.Haste);
            Assert.That(r.Effect, Is.EqualTo(EffectKind.Hasted));
            Assert.That(r.EffectOutcome, Is.EqualTo(EffectOutcome.Added));
            Assert.That(b.MoveSpeedMultiplier, Is.EqualTo(1.3f).Within(1e-5));
            Assert.That(b.JumpMultiplier, Is.EqualTo(1.2f).Within(1e-5));

            b.Tick(5.9f);
            Assert.That(b.MoveSpeedMultiplier, Is.GreaterThan(1f));
            b.Tick(0.2f);
            Assert.That(b.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(b.JumpMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Haste_TwoSourcesStackLikeOtherBuffs()
        {
            var a = new PlayerVitals(1, _s);
            var c = new PlayerVitals(3, _s);
            var b = new PlayerVitals(2, _s);
            _hits.HitPlayer(a, b, PayloadKind.Haste);
            _hits.HitPlayer(c, b, PayloadKind.Haste);
            Assert.That(b.MoveSpeedMultiplier, Is.EqualTo(1.45f).Within(1e-5));
            Assert.That(b.JumpMultiplier, Is.EqualTo(1.3f).Within(1e-5));
        }

        [Test]
        public void Slow_SlowsEnemyUntilItExpiresAndHasAFloor()
        {
            var p = new PlayerVitals(1, _s);
            var skitter = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var r = _hits.HitEnemy(p, skitter, PayloadKind.Haste, 1, false);
            Assert.That(r.Effect, Is.EqualTo(EffectKind.Slow));
            Assert.That(skitter.MoveMultiplier, Is.EqualTo(0.65f).Within(1e-5));
            Assert.That(skitter.TurnMultiplier, Is.EqualTo(0.65f).Within(1e-5));

            skitter.Tick(5.1f);
            Assert.That(skitter.MoveMultiplier, Is.EqualTo(1f));

            _s.SlowAmount = 0.7f;
            var q = new PlayerVitals(2, _s);
            _hits.HitEnemy(p, skitter, PayloadKind.Haste, 1, false);
            _hits.HitEnemy(q, skitter, PayloadKind.Haste, 1, false);
            Assert.That(skitter.MoveMultiplier, Is.EqualTo(_s.SlowFloor).Within(1e-5));
        }

        [Test]
        public void Relay_AllyShotsCarryRelayersPayloadForFiveHits()
        {
            var relayer = new PlayerVitals(1, _s) { RelayCarry = PayloadKind.Haste };
            var b = new PlayerVitals(2, _s);
            var c = new PlayerVitals(3, _s);
            var r = _hits.HitPlayer(relayer, b, PayloadKind.Relay);
            Assert.That(r.Effect, Is.EqualTo(EffectKind.Relayed));
            Assert.That(b.HasRelay, Is.True);
            Assert.That(b.RelayPayload, Is.EqualTo(PayloadKind.Haste));
            Assert.That(b.RelaySourceId, Is.EqualTo(1));
            Assert.That(b.RelayHitsLeft, Is.EqualTo(5));

            // B's Might shot on C also Hastes C, with the relayer as the source.
            var hit = _hits.HitPlayer(b, c, PayloadKind.Might);
            Assert.That(hit.Relayed, Is.True);
            Assert.That(hit.RelayedPayload, Is.EqualTo(PayloadKind.Haste));
            Assert.That(hit.RelayedOutcome, Is.EqualTo(EffectOutcome.Added));
            Assert.That(c.Buffs.Has(EffectKind.Empower), Is.True);
            Assert.That(c.Buffs.Has(EffectKind.Hasted), Is.True);
            Assert.That(b.RelayHitsLeft, Is.EqualTo(4));

            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            for (int i = 0; i < 4; i++) _hits.HitEnemy(b, enemy, PayloadKind.Might, 0, false);
            Assert.That(enemy.Debuffs.Has(EffectKind.Slow), Is.True);
            Assert.That(b.HasRelay, Is.False);
            Assert.That(b.Buffs.Has(EffectKind.Relayed), Is.False, "used-up relay frees its slot");
            Assert.That(_hits.HitPlayer(b, c, PayloadKind.Might).Relayed, Is.False);
        }

        [Test]
        public void Relay_ExpiresAfterItsDuration()
        {
            var relayer = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            _hits.HitPlayer(relayer, b, PayloadKind.Relay);
            b.Tick(6.1f);
            Assert.That(b.HasRelay, Is.False);
            Assert.That(b.RelayHitsLeft, Is.EqualTo(0));
        }

        [Test]
        public void Relay_CarriedVigorHealsAndCarriedPayloadNeverBuffsTheRelayer()
        {
            var relayer = new PlayerVitals(1, _s) { RelayCarry = PayloadKind.Vigor };
            var b = new PlayerVitals(2, _s);
            var c = new PlayerVitals(3, _s);
            _hits.HitPlayer(relayer, b, PayloadKind.Relay);

            c.SetHealth(50);
            var hit = _hits.HitPlayer(b, c, PayloadKind.Haste);
            Assert.That(hit.Healed, Is.EqualTo(_s.VigorHeal));
            Assert.That(c.Buffs.Has(EffectKind.Hasted), Is.True);

            relayer.SetHealth(50);
            var back = _hits.HitPlayer(b, relayer, PayloadKind.Haste);
            Assert.That(back.RelayedOutcome, Is.EqualTo(EffectOutcome.Refused));
            Assert.That(relayer.Health, Is.EqualTo(50));
            Assert.That(relayer.Buffs.Has(EffectKind.Hasted), Is.True, "B's own Haste still lands");
        }

        [Test]
        public void Relay_CantCarryRelay()
        {
            var p = new PlayerVitals(1, _s);
            Assert.Throws<System.ArgumentException>(() => p.RelayCarry = PayloadKind.Relay);
        }

        [Test]
        public void Tag_SplashesAShareOfLaterHitsToNearbyEnemies()
        {
            var p = new PlayerVitals(1, _s);
            var tagged = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var first = _hits.HitEnemy(p, tagged, PayloadKind.Relay, 10, false);
            Assert.That(first.Effect, Is.EqualTo(EffectKind.Tag));
            Assert.That(first.SplashDamage, Is.EqualTo(0), "the tagging shot itself doesn't splash");
            Assert.That(tagged.IsTagged, Is.True);

            var second = _hits.HitEnemy(p, tagged, PayloadKind.Might, 10, false);
            Assert.That(second.SplashDamage, Is.EqualTo(3f).Within(1e-5));

            var targets = new List<HitTarget>
            {
                HitTarget.Body(100, Vector3.Zero, 0.5f),
                HitTarget.Body(101, new Vector3(2, 0, 0), 0.5f),
                HitTarget.Body(102, new Vector3(0, 0, 3.5f), 0.5f),
                HitTarget.Body(7, new Vector3(1, 0, 0), 0.5f, isPlayer: true),
            };
            var hidden = HitTarget.Body(103, new Vector3(0, 1, 0), 0.5f);
            hidden.Hittable = false;
            targets.Add(hidden);

            var ids = new List<int>();
            Assert.That(_hits.SplashTargets(Vector3.Zero, 100, targets, ids), Is.EqualTo(1));
            Assert.That(ids, Is.EqualTo(new[] { 101 }));

            var neighbour = new EnemyVitals(101, EnemyFamily.Skitter, _s);
            Assert.That(_hits.ApplySplash(neighbour, second.SplashDamage), Is.EqualTo(3f).Within(1e-5));
            Assert.That(neighbour.Debuffs.Count, Is.EqualTo(0), "splash carries no debuffs");
        }

        [Test]
        public void Tag_SplashesEvenWhenTheHitKills()
        {
            var p = new PlayerVitals(1, _s);
            var wisp = new EnemyVitals(100, EnemyFamily.Wisp, _s);
            _hits.HitEnemy(p, wisp, PayloadKind.Relay, 1, false);
            var kill = _hits.HitEnemy(p, wisp, PayloadKind.Might, 100, false);
            Assert.That(kill.Killed, Is.True);
            Assert.That(kill.SplashDamage, Is.EqualTo(30f).Within(1e-4));
        }

        [Test]
        public void Downed_ClearsBuffsAndRelay()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            _hits.HitPlayer(a, b, PayloadKind.Relay);
            _hits.HitPlayer(a, b, PayloadKind.Haste);
            b.TakeEnemyDamage(500, out bool wentDown);
            Assert.That(wentDown, Is.True);
            Assert.That(b.IsDown, Is.True);
            Assert.That(b.Buffs.Count, Is.EqualTo(0));
            Assert.That(b.HasRelay, Is.False);
            b.TakeEnemyDamage(5, out wentDown);
            Assert.That(wentDown, Is.False, "already down");
        }
    }
}
