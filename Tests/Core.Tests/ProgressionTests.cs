using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Progression;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ProgressionTests
    {
        [Test]
        public void XpLevelsUpAndGivesPoints()
        {
            const int L = SkillTree.XpPerLevel;
            var p = new PlayerProgress();
            Assert.That(p.AddXp(L - 1), Is.EqualTo(0));
            Assert.That(p.AddXp(1), Is.EqualTo(1));
            Assert.That(p.Level, Is.EqualTo(2));
            Assert.That(p.Points, Is.EqualTo(1));
            Assert.That(p.Xp, Is.EqualTo(0));
            // Two more levels in one go.
            Assert.That(p.AddXp(2 * L + 10), Is.EqualTo(2));
            Assert.That(p.Level, Is.EqualTo(4));
            Assert.That(p.Xp, Is.EqualTo(10));
        }

        [Test]
        public void XpGainIsLinearUpToMaxLevel()
        {
            var p = new PlayerProgress();
            for (int level = 1; level < SkillTree.MaxLevel; level++)
                Assert.That(SkillTree.XpToNext(level), Is.EqualTo(SkillTree.XpPerLevel), $"level {level}");
            p.AddXp(SkillTree.XpPerLevel * (SkillTree.MaxLevel - 1));
            Assert.That(p.Level, Is.EqualTo(SkillTree.MaxLevel));
            Assert.That(p.Points, Is.EqualTo(SkillTree.MaxPoints));
            Assert.That(p.AddXp(100000), Is.EqualTo(0), "capped");
        }

        [Test]
        public void TreeStaysInsideThePowerBudget()
        {
            // Max level can't buy everything, so builds differ.
            Assert.That(SkillTree.MaxPoints, Is.LessThan(SkillTree.TotalRanks));
            // Even a full branch keeps levels near their +20% share of the +40% budget.
            float dps = (1f + SkillTree.MaxRank * SkillTree.DamagePerRank) * (1f + SkillTree.MaxRank * SkillTree.FireRatePerRank);
            Assert.That(dps, Is.LessThanOrEqualTo(1.25f), "maxed offense damage per second");
            Assert.That(SkillTree.MaxRank * SkillTree.PotencyPerRank, Is.LessThanOrEqualTo(0.25f), "maxed potency");
            Assert.That(SkillTree.MaxRank * SkillTree.HealthPerRank / new CombatSettings().PlayerMaxHealth, Is.LessThanOrEqualTo(0.25f), "maxed toughness");
            Assert.That(SkillTree.MaxRank * SkillTree.MovePerRank, Is.LessThanOrEqualTo(0.10f), "maxed move speed");
        }

        [Test]
        public void SkillsNeedPointsAndTheSkillBefore()
        {
            var p = new PlayerProgress();
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.False, "no points yet");
            p.AddXp(4 * SkillTree.XpPerLevel);
            Assert.That(p.Points, Is.EqualTo(4));
            Assert.That(p.TryBuy(SkillId.RapidFire), Is.False, "needs Sharpshooter first");
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.TryBuy(SkillId.RapidFire), Is.True);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.Rank(SkillId.Sharpshooter), Is.EqualTo(3));
            p.AddXp(5 * SkillTree.XpPerLevel);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.False, "max rank");
            Assert.That(PlayerProgress.RankIn(p.PackedRanks, SkillId.RapidFire), Is.EqualTo(1));
            Assert.That(p.FireRateMultiplier, Is.EqualTo(1f + SkillTree.FireRatePerRank).Within(1e-5f));
        }

        [Test]
        public void SkillsChangeCombatStats()
        {
            var settings = new CombatSettings();
            var v = new PlayerVitals(1, settings);
            var p = new PlayerProgress();
            p.AddXp(3 * SkillTree.XpPerLevel);
            p.TryBuy(SkillId.Toughness);
            p.TryBuy(SkillId.FleetFoot);
            p.TryBuy(SkillId.Sharpshooter);
            p.ApplyTo(v, settings.PlayerMaxHealth);
            Assert.That(v.MaxHealth, Is.EqualTo(settings.PlayerMaxHealth + SkillTree.HealthPerRank));
            Assert.That(v.Health, Is.EqualTo(v.MaxHealth), "full health stays full");
            Assert.That(v.MoveSpeedMultiplier, Is.EqualTo(1f + SkillTree.MovePerRank).Within(1e-5f));
            Assert.That(v.DamageBonus, Is.EqualTo(SkillTree.DamagePerRank).Within(1e-5f));
        }

        [Test]
        public void SharedBankIsOnePoolForTheParty()
        {
            var pool = new EssenceAmounts();
            var a = new EssenceWallet(1, pool);
            var b = new EssenceWallet(2, pool);
            a.PickUp(EssenceColor.Yellow, 30);
            b.PickUp(EssenceColor.Yellow, 15);
            a.BankAll();
            Assert.That(b.Banked(EssenceColor.Yellow), Is.EqualTo(30), "b sees what a banked");
            Assert.That(b.Carried(EssenceColor.Yellow), Is.EqualTo(15), "carried stays personal");
            b.BankAll();
            var gate = new Gate(new GateSite(1, default), 1u);
            Assert.That(gate.Shortfall(a), Is.EqualTo(0));
            Assert.That(gate.Unlock(a), Is.EqualTo(GateUnlockOutcome.Unlocked));
            Assert.That(b.Banked(EssenceColor.Yellow), Is.EqualTo(5), "the gate took 40 from the shared pool");
            gate.GrantAccess(2);
            Assert.That(gate.HasAccess(2), Is.True);
        }

        [Test]
        public void GlimmersSometimesDropARandomColorAndNeverAttack()
        {
            var ledger = new DropLedger(seed: 42);
            int drops = 0;
            var colors = new HashSet<EssenceColor>();
            for (int i = 0; i < 2000; i++)
            {
                ledger.RecordHit(i, 7);
                foreach (var a in ledger.Killed(i, EnemyFamily.Glimmer))
                {
                    Assert.That(a.PlayerId, Is.EqualTo(7));
                    Assert.That(a.Amount, Is.EqualTo(1));
                    drops++;
                    colors.Add(a.Color);
                }
            }
            Assert.That(drops, Is.InRange(420, 580), "about 25% of kills drop");
            Assert.That(colors, Is.EquivalentTo(EssenceColors.All), "any of the six colors");
            Assert.That(EnemyAttackSettings.For(EnemyFamily.Glimmer).Damage, Is.EqualTo(0f));
            Assert.That(new CombatSettings().EnemyHealth(EnemyFamily.Glimmer), Is.LessThanOrEqualTo(10f), "one base shot kills");
        }
    }
}
