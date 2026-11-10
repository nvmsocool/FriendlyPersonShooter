using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ChainTrackerTests
    {
        private const int A = 1, B = 2, C = 3, D = 4, E = 5;
        private CombatSettings _s;
        private ChainTracker _chains;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _chains = new ChainTracker(_s);
        }

        [Test]
        public void EachLinkRemembersItsPayload()
        {
            _chains.OnBuff(A, B, 0, payload: PayloadKind.Haste);
            _chains.OnBuff(B, C, 1, relayed: true, payload: PayloadKind.Might);
            Assert.That(_chains.TryGetHeld(C, 1, out var info), Is.True);
            Assert.That(info.Links, Is.EqualTo(3), "Haste, Might, and the Relay link");
            Assert.That(ChainTracker.PayloadAt(info.Payloads, 0), Is.EqualTo(PayloadKind.Haste));
            Assert.That(ChainTracker.PayloadAt(info.Payloads, 1), Is.EqualTo(PayloadKind.Might));
            Assert.That(ChainTracker.PayloadAt(info.Payloads, 2), Is.EqualTo(PayloadKind.Relay));
            Assert.That(ChainTracker.PayloadAt(info.Payloads, 3), Is.EqualTo(PayloadKind.None), "no fourth link");
        }

        [Test]
        public void OneBuffStartsAOneLinkChainWorthNothing()
        {
            var buff = _chains.OnBuff(A, B, 0);
            Assert.That(buff.Event, Is.EqualTo(ChainEvent.Started));
            Assert.That(buff.Links, Is.EqualTo(1));
            Assert.That(_chains.TryGetHeld(B, 1, out var info), Is.True);
            Assert.That(info.StarterId, Is.EqualTo(A));
            Assert.That(info.TimeLeft, Is.EqualTo(4f).Within(1e-5));
            Assert.That(_chains.TryGetHeld(A, 1, out _), Is.False);

            var cash = _chains.OnEnemyHit(B, 1, false);
            Assert.That(cash.Cashed, Is.True);
            Assert.That(cash.Bonus, Is.EqualTo(0));
            Assert.That(_chains.TryGetHeld(B, 1, out _), Is.False, "used up even for no bonus");
        }

        [Test]
        public void DocExample_AHastesB_BMightsC_CCashesTwoLinksFor15Percent()
        {
            var lobby = new LobbyCombat(_s);
            var a = new PlayerVitals(A, _s);
            var b = new PlayerVitals(B, _s);
            var c = new PlayerVitals(C, _s);
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);

            Assert.That(lobby.HitPlayer(a, b, PayloadKind.Haste, 0).ChainEvent, Is.EqualTo(ChainEvent.Started));
            var pass = lobby.HitPlayer(b, c, PayloadKind.Might, 2);
            Assert.That(pass.ChainEvent, Is.EqualTo(ChainEvent.Grew));
            Assert.That(pass.ChainLinks, Is.EqualTo(2));

            var hit = lobby.HitEnemy(c, enemy, PayloadKind.Might, 10, false, 4);
            Assert.That(hit.ChainEvent, Is.EqualTo(ChainEvent.Cashed));
            Assert.That(hit.ChainLinks, Is.EqualTo(2));
            Assert.That(hit.ChainBonus, Is.EqualTo(0.15f).Within(1e-5));
            // On top of Might: 10 x 1.25 x 1.15.
            Assert.That(hit.DamageDealt, Is.EqualTo(10 * 1.25f * 1.15f).Within(1e-4));

            var next = lobby.HitEnemy(c, enemy, PayloadKind.Might, 1, false, 4.5);
            Assert.That(next.ChainEvent, Is.EqualTo(ChainEvent.None));
            Assert.That(next.ChainBonus, Is.EqualTo(0));
        }

        [Test]
        public void BonusesFor3And4LinksAndTheCap()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 1);
            _chains.OnBuff(C, D, 2);
            Assert.That(_chains.OnEnemyHit(D, 3, false).Bonus, Is.EqualTo(0.35f).Within(1e-5));

            _chains.OnBuff(A, B, 10);
            _chains.OnBuff(B, C, 11);
            _chains.OnBuff(C, D, 12);
            _chains.OnBuff(D, E, 13);
            var info = Held(E, 13);
            Assert.That(info.Links, Is.EqualTo(4));
            _chains.OnBuff(E, 6, 14);
            Assert.That(Held(6, 14).Links, Is.EqualTo(4), "capped at 4");
            Assert.That(_chains.OnEnemyHit(6, 15, false).Bonus, Is.EqualTo(0.6f).Within(1e-5));
        }

        [Test]
        public void WeakPointCashGetsOneAndAHalfTimesTheBonus()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 1);
            var cash = _chains.OnEnemyHit(C, 2, true);
            Assert.That(cash.Bonus, Is.EqualTo(0.15f * 1.5f).Within(1e-5));
        }

        [Test]
        public void WeakPointBonusFlowsIntoDamage()
        {
            var lobby = new LobbyCombat(_s);
            var a = new PlayerVitals(A, _s);
            var b = new PlayerVitals(B, _s);
            var c = new PlayerVitals(C, _s);
            var d = new PlayerVitals(D, _s);
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            lobby.HitPlayer(a, b, PayloadKind.Haste, 0);
            lobby.HitPlayer(b, c, PayloadKind.Haste, 0);
            lobby.HitPlayer(c, d, PayloadKind.Haste, 0);
            var hit = lobby.HitEnemy(d, enemy, PayloadKind.Haste, 10, true, 1);
            Assert.That(hit.DamageDealt, Is.EqualTo(10 * 2f * (1 + 0.35f * 1.5f)).Within(1e-4));
        }

        [Test]
        public void PassingMustHappenWithinFiveSeconds()
        {
            _chains.OnBuff(A, B, 0);
            var late = _chains.OnBuff(B, C, 5.5);
            Assert.That(late.Event, Is.EqualTo(ChainEvent.Started), "B's chain had run out, so this is a new one");
            Assert.That(late.Links, Is.EqualTo(1));

            _chains.OnBuff(A, B, 10);
            Assert.That(_chains.OnBuff(B, C, 15).Event, Is.EqualTo(ChainEvent.Grew), "exactly 5 s still counts");
        }

        [Test]
        public void CashMustHappenWithinFiveSecondsOfGettingTheChain()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 4);
            Assert.That(_chains.OnEnemyHit(C, 8.9, false).Cashed, Is.True, "each link gives a fresh 5 s");

            _chains.OnBuff(A, B, 20);
            _chains.OnBuff(B, C, 21);
            var late = _chains.OnEnemyHit(C, 26.5, false);
            Assert.That(late.Cashed, Is.False);
            Assert.That(late.Bonus, Is.EqualTo(0));
        }

        [Test]
        public void LoopsAddNothing()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 1);
            var loop = _chains.OnBuff(C, A, 2);
            Assert.That(loop.Event, Is.EqualTo(ChainEvent.Looped));
            var info = Held(C, 2);
            Assert.That(info.Links, Is.EqualTo(2));
            Assert.That(info.TimeLeft, Is.EqualTo(4f).Within(1e-5), "a loop doesn't refresh the timer");

            // A -> B -> A: B keeps the 1-link chain; A gets only a new 1-link chain of their own.
            var t = new ChainTracker(_s);
            t.OnBuff(A, B, 0);
            Assert.That(t.OnBuff(B, A, 1).Event, Is.EqualTo(ChainEvent.Looped));
            Assert.That(t.TryGetHeld(B, 1, out var b), Is.True);
            Assert.That(b.Links, Is.EqualTo(1));
            Assert.That(t.OnEnemyHit(A, 2, false).Bonus, Is.EqualTo(0));
        }

        [Test]
        public void PingPongBetweenTwoPlayersNeverEarnsABonus()
        {
            for (int i = 0; i < 10; i++)
            {
                _chains.OnBuff(i % 2 == 0 ? A : B, i % 2 == 0 ? B : A, i * 0.5);
            }
            Assert.That(_chains.OnEnemyHit(A, 6, true).Bonus, Is.EqualTo(0));
        }

        [Test]
        public void HolderKeepsTheLongerChain()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 1); // C holds 2 links
            var other = _chains.OnBuff(D, C, 2); // a fresh 1-link chain for C
            Assert.That(other.Event, Is.EqualTo(ChainEvent.None));
            Assert.That(Held(C, 2).Links, Is.EqualTo(2));
            var all = new List<ChainInfo>();
            Assert.That(_chains.GetChains(2, all), Is.EqualTo(1));
        }

        [Test]
        public void PassingOnMovesTheChain()
        {
            _chains.OnBuff(A, B, 0);
            _chains.OnBuff(B, C, 1);
            Assert.That(_chains.TryGetHeld(B, 1, out _), Is.False);
            Assert.That(_chains.OnEnemyHit(B, 1.5, false).Cashed, Is.False);
            Assert.That(_chains.OnEnemyHit(C, 1.5, false).Links, Is.EqualTo(2));
        }

        [Test]
        public void RelayCarriedCashCountsAsALink()
        {
            // A relays B: chain [A, B] = 1 link, and B's relayed cash adds one more = 2 links.
            var lobby = new LobbyCombat(_s);
            var a = new PlayerVitals(A, _s);
            var b = new PlayerVitals(B, _s);
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            lobby.HitPlayer(a, b, PayloadKind.Relay, 0);
            var hit = lobby.HitEnemy(b, enemy, PayloadKind.Might, 10, false, 1);
            Assert.That(hit.Relayed, Is.True);
            Assert.That(hit.ChainLinks, Is.EqualTo(2));
            Assert.That(hit.ChainBonus, Is.EqualTo(0.15f).Within(1e-5));
        }

        [Test]
        public void RelayCarriedPassCountsAsALinkOncePerChain()
        {
            var lobby = new LobbyCombat(_s);
            var a = new PlayerVitals(A, _s);
            var b = new PlayerVitals(B, _s);
            var c = new PlayerVitals(C, _s);
            lobby.HitPlayer(a, b, PayloadKind.Relay, 0);
            var pass = lobby.HitPlayer(b, c, PayloadKind.Might, 1);
            Assert.That(pass.ChainEvent, Is.EqualTo(ChainEvent.Grew));
            Assert.That(pass.ChainLinks, Is.EqualTo(3), "A -> B -> C plus the relay link");

            var cash = lobby.Chains.OnEnemyHit(C, 2, false, relayed: true);
            Assert.That(cash.Links, Is.EqualTo(3), "relay link counts once per chain");
        }

        [Test]
        public void DroppedHolderLosesTheChain()
        {
            _chains.OnBuff(A, B, 0);
            _chains.DropPlayer(B);
            Assert.That(_chains.TryGetHeld(B, 0, out _), Is.False);
        }

        [Test]
        public void RefusedBuffsDontStartChains()
        {
            var lobby = new LobbyCombat(_s);
            var a = new PlayerVitals(A, _s);
            var b = new PlayerVitals(B, _s) { Acceptance = BuffAcceptance.NoOne };
            var r = lobby.HitPlayer(a, b, PayloadKind.Might, 0);
            Assert.That(r.ChainEvent, Is.EqualTo(ChainEvent.None));
            Assert.That(lobby.Chains.TryGetHeld(B, 0, out _), Is.False);
        }

        [Test]
        public void WouldGrowOnlyForTheHolderAndNewPlayers()
        {
            _chains.OnBuff(A, B, 0);
            Assert.That(_chains.WouldGrow(B, C, 1), Is.True);
            Assert.That(_chains.WouldGrow(B, A, 1), Is.False);
            Assert.That(_chains.WouldGrow(A, C, 1), Is.False);
            Assert.That(_chains.WouldGrow(B, C, 6), Is.False);
        }

        private ChainInfo Held(int id, double time)
        {
            Assert.That(_chains.TryGetHeld(id, time, out var info), Is.True);
            return info;
        }
    }
}
