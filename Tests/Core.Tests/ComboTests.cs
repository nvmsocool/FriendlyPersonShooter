using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ComboTests
    {
        private CombatSettings _s;
        private LobbyCombat _lobby;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _lobby = new LobbyCombat(_s);
        }

        [Test]
        public void Plague_SpreadsWitherToNearbyEnemiesOnly()
        {
            var p = new PlayerVitals(1, _s);
            var source = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var near = new EnemyVitals(101, EnemyFamily.Skitter, _s);
            var far = new EnemyVitals(102, EnemyFamily.Skitter, _s);
            var enemies = new[] { source, near, far };
            var positions = new[] { Vector3.Zero, new Vector3(3, 0, 0), new Vector3(10, 0, 0) };

            _lobby.HitEnemy(p, source, PayloadKind.Vigor, 0, false, 0);
            Assert.That(source.IsPlagued, Is.False);
            Assert.That(Plague.Step(0.1f, enemies, positions, _s), Is.EqualTo(0));

            _lobby.HitEnemy(p, source, PayloadKind.Relay, 0, false, 0);
            Assert.That(source.IsPlagued, Is.True);
            Assert.That(Plague.Step(0.1f, enemies, positions, _s), Is.EqualTo(1), "spreads as soon as it forms");
            Assert.That(near.Debuffs.StrengthOf(EffectKind.Wither), Is.EqualTo(_s.WitherDps * _s.PlagueShare).Within(1e-5));
            Assert.That(far.Debuffs.Has(EffectKind.Wither), Is.False);

            // The spread Wither deals damage over time on the neighbour.
            float before = near.Health;
            near.Tick(1f);
            Assert.That(before - near.Health, Is.EqualTo(_s.WitherDps * _s.PlagueShare).Within(1e-4));

            // Next spread waits for the interval.
            Assert.That(Plague.Step(0.5f, enemies, positions, _s), Is.EqualTo(0));
            Assert.That(Plague.Step(0.5f, enemies, positions, _s), Is.EqualTo(1));
        }

        [Test]
        public void Plague_SpreadWitherDoesNotCarryTagSoItStopsAtOneHop()
        {
            var p = new PlayerVitals(1, _s);
            var a = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var b = new EnemyVitals(101, EnemyFamily.Skitter, _s);
            var c = new EnemyVitals(102, EnemyFamily.Skitter, _s);
            var enemies = new[] { a, b, c };
            var positions = new[] { Vector3.Zero, new Vector3(3, 0, 0), new Vector3(6, 0, 0) };
            _lobby.HitEnemy(p, a, PayloadKind.Vigor, 0, false, 0);
            _lobby.HitEnemy(p, a, PayloadKind.Relay, 0, false, 0);
            Plague.Step(0.1f, enemies, positions, _s);
            Plague.Step(1f, enemies, positions, _s);
            Assert.That(b.Debuffs.Has(EffectKind.Wither), Is.True);
            Assert.That(b.IsPlagued, Is.False);
            Assert.That(c.Debuffs.Has(EffectKind.Wither), Is.False);

            // Tag b too and the plague jumps on.
            _lobby.HitEnemy(p, b, PayloadKind.Relay, 0, false, 0);
            Plague.Step(0.1f, enemies, positions, _s);
            Assert.That(c.Debuffs.Has(EffectKind.Wither), Is.True);
        }

        [Test]
        public void Plague_StopsWhenTheComboEnds()
        {
            var p = new PlayerVitals(1, _s);
            var a = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var b = new EnemyVitals(101, EnemyFamily.Skitter, _s);
            var enemies = new[] { a, b };
            var positions = new[] { Vector3.Zero, new Vector3(1, 0, 0) };
            _lobby.HitEnemy(p, a, PayloadKind.Vigor, 0, false, 0);
            _lobby.HitEnemy(p, a, PayloadKind.Relay, 0, false, 0);
            a.Tick(5.1f); // Wither (5 s) ends; Tag (6 s) remains.
            Assert.That(a.IsPlagued, Is.False);
            Assert.That(Plague.Step(0.1f, enemies, positions, _s), Is.EqualTo(0));
        }

        [Test]
        public void Blitz_HasteAndMightDoubleTheEmpowerBonus()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            var c = new PlayerVitals(3, _s);
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            _lobby.HitPlayer(a, c, PayloadKind.Might, 0);
            Assert.That(c.IsBlitzed, Is.False);
            Assert.That(c.DamageBonus, Is.EqualTo(0.25f).Within(1e-5));

            _lobby.HitPlayer(b, c, PayloadKind.Haste, 10); // later, so no chain is cashed below
            Assert.That(c.IsBlitzed, Is.True);
            Assert.That(c.DamageBonus, Is.EqualTo(0.5f).Within(1e-5));
            var hit = _lobby.HitEnemy(c, enemy, PayloadKind.Might, 10, false, 30);
            Assert.That(hit.DamageDealt, Is.EqualTo(15f).Within(1e-4));

            c.Tick(6.1f);
            Assert.That(c.IsBlitzed, Is.False);
        }

        [Test]
        public void Discovery_FirstComboInLobbyIsFlaggedThenNotAgain()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            var c = new PlayerVitals(3, _s);
            var d = new PlayerVitals(4, _s);
            var events = new List<ComboEvent>();

            _lobby.HitPlayer(a, c, PayloadKind.Might, 0, events);
            Assert.That(events, Is.Empty);
            _lobby.HitPlayer(b, c, PayloadKind.Haste, 0, events);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(ComboKind.Blitz));
            Assert.That(events[0].TargetId, Is.EqualTo(3));
            Assert.That(events[0].TargetIsPlayer, Is.True);
            Assert.That(events[0].FirstDiscovery, Is.True);
            Assert.That(_lobby.Combos.IsDiscovered(ComboKind.Blitz), Is.True);
            Assert.That(_lobby.Combos.IsDiscovered(ComboKind.Plague), Is.False);

            // Refreshing an active combo doesn't fire again.
            events.Clear();
            _lobby.HitPlayer(b, c, PayloadKind.Haste, 1, events);
            Assert.That(events, Is.Empty);

            // A second player forming it: an event, but not a discovery.
            _lobby.HitPlayer(a, d, PayloadKind.Haste, 2, events);
            _lobby.HitPlayer(b, d, PayloadKind.Might, 2, events);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].FirstDiscovery, Is.False);
        }

        [Test]
        public void Discovery_EnemyComboAndReformingAfterItEnds()
        {
            var p = new PlayerVitals(1, _s);
            var enemy = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var events = new List<ComboEvent>();
            _lobby.HitEnemy(p, enemy, PayloadKind.Relay, 0, false, 0, events);
            _lobby.HitEnemy(p, enemy, PayloadKind.Vigor, 0, false, 0, events);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Kind, Is.EqualTo(ComboKind.Plague));
            Assert.That(events[0].TargetIsPlayer, Is.False);
            Assert.That(events[0].FirstDiscovery, Is.True);

            enemy.Tick(5.5f); // Wither gone
            _lobby.Combos.Check(enemy, events);
            Assert.That(_lobby.Combos.ActiveOn(100, false), Is.EqualTo(ComboKind.None));
            events.Clear();
            _lobby.HitEnemy(p, enemy, PayloadKind.Vigor, 0, false, 10, events);
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].FirstDiscovery, Is.False);
        }

        [Test]
        public void Discovery_CarriesOverOnMerge()
        {
            var other = new ComboTracker();
            other.MarkDiscovered(_lobby.Combos.Discovered | ComboKind.Plague);
            Assert.That(other.IsDiscovered(ComboKind.Plague), Is.True);
            Assert.That(other.IsDiscovered(ComboKind.Blitz), Is.False);
        }
    }
}
