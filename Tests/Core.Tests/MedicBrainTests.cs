using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class MedicBrainTests
    {
        private const int Me = 1;
        private CombatSettings _s;
        private MedicSettings _m;
        private ChainTracker _chains;
        private List<MedicAlly> _allies;
        private List<MedicEnemy> _enemies;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _m = new MedicSettings();
            _chains = new ChainTracker(_s);
            _allies = new List<MedicAlly>();
            _enemies = new List<MedicEnemy> { new MedicEnemy { Id = 100, Position = new Vector3(0, 1, 10), Hittable = true } };
        }

        private MedicDecision Decide(PayloadKind payload = PayloadKind.Vigor, double time = 0)
        {
            return MedicBrain.Decide(Me, Vector3.Zero, payload, _allies, _enemies, _chains, time, _m, _s);
        }

        private MedicAlly Ally(int id, float x, float health = 1f, float buffLeft = 0f, EffectKind buff = EffectKind.Shield)
        {
            var buffs = new EffectSet(3);
            if (buffLeft > 0f) buffs.Apply(buff, 50, 1f, buffLeft);
            return new MedicAlly { Id = id, Position = new Vector3(x, 1, 0), HealthFraction = health, Buffs = buffs };
        }

        [Test]
        public void ShootsEnemiesWhenAlliesAreFine()
        {
            _allies.Add(Ally(2, 5, 1f, 4f));
            var d = Decide();
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.Enemy));
            Assert.That(d.TargetId, Is.EqualTo(100));
            Assert.That(d.AimPoint, Is.EqualTo(new Vector3(0, 1, 10)));
        }

        [Test]
        public void KeepsUpTheBuffThatExpiresSoonest()
        {
            _allies.Add(Ally(2, 5, 1f, 1.5f, EffectKind.Hasted));
            _allies.Add(Ally(3, 6, 1f, 0.5f, EffectKind.Hasted));
            _allies.Add(Ally(4, 7, 1f, 5f, EffectKind.Hasted));
            var d = Decide(PayloadKind.Haste);
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.Buff));
            Assert.That(d.TargetId, Is.EqualTo(3));
        }

        [Test]
        public void HealsTheMostHurtAllyBeforeBuffs()
        {
            _allies.Add(Ally(2, 5, 0.6f, 4f));
            _allies.Add(Ally(3, 6, 0.3f, 4f));
            _allies.Add(Ally(4, 7, 1f));
            var d = Decide();
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.Heal));
            Assert.That(d.TargetId, Is.EqualTo(3));

            // A Haste medic can't heal, so it keeps buffs up instead.
            Assert.That(Decide(PayloadKind.Haste).Kind, Is.EqualTo(MedicTargetKind.Buff));
        }

        [Test]
        public void FragmentsComeFirst()
        {
            var downed = new PlayerVitals(5, _s);
            downed.TakeEnemyDamage(1000);
            var revive = new ReviveState(downed, new Vector3(4, 1, 4), 3, _s.Revive);
            for (int i = 0; i < 30; i++) revive.Step(1f / 30f);

            _allies.Add(Ally(3, 6, 0.1f));
            _allies.Add(new MedicAlly { Id = 5, Position = new Vector3(4, 0.3f, 4), IsDown = true, Fragments = revive.Fragments });
            var d = Decide();
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.Fragment));
            Assert.That(d.OwnerId, Is.EqualTo(5));
            Assert.That(revive.TryGetFragment(d.TargetId, out var f), Is.True);
            Assert.That(d.AimPoint, Is.EqualTo(f.Position));
        }

        [Test]
        public void IgnoresOutOfRangeAndDownedAllies()
        {
            _allies.Add(Ally(2, 100, 0.1f));
            _allies.Add(new MedicAlly { Id = 3, Position = new Vector3(2, 1, 0), HealthFraction = 0f, IsDown = true });
            Assert.That(Decide().Kind, Is.EqualTo(MedicTargetKind.Enemy));
        }

        [Test]
        public void GrowsItsOwnChainBeforeRoutineBuffs()
        {
            _allies.Add(Ally(2, 5, 1f, 0.5f, EffectKind.Hasted)); // gave us the chain
            _allies.Add(Ally(3, 8, 1f, 5f, EffectKind.Hasted));
            _chains.OnBuff(2, Me, 0);
            var d = Decide(PayloadKind.Haste, 1);
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.ExtendChain));
            Assert.That(d.TargetId, Is.EqualTo(3), "ally 2 is already in the chain");
        }

        [Test]
        public void KeepsAChainHoldersBuffFresh()
        {
            _allies.Add(Ally(2, 5, 1f, 1f, EffectKind.Hasted));
            _allies.Add(Ally(3, 8, 1f, 3f, EffectKind.Hasted));
            _chains.OnBuff(2, 3, 0); // ally 3 holds a chain
            var d = Decide(PayloadKind.Haste, 1);
            Assert.That(d.Kind, Is.EqualTo(MedicTargetKind.ChainHolder));
            Assert.That(d.TargetId, Is.EqualTo(3));

            // Once their buff is fresh, routine upkeep resumes.
            _allies[1] = Ally(3, 8, 1f, 6f, EffectKind.Hasted);
            Assert.That(Decide(PayloadKind.Haste, 1).TargetId, Is.EqualTo(2));
        }

        [Test]
        public void HealingStillBeatsChains()
        {
            _allies.Add(Ally(2, 5, 0.2f));
            _allies.Add(Ally(3, 8));
            _chains.OnBuff(2, Me, 0);
            Assert.That(Decide(PayloadKind.Vigor, 1).Kind, Is.EqualTo(MedicTargetKind.Heal));
        }

        [Test]
        public void NothingToDo()
        {
            _enemies.Clear();
            _allies.Add(Ally(2, 5, 1f, 5f));
            Assert.That(Decide().Kind, Is.EqualTo(MedicTargetKind.None));
        }
    }
}
