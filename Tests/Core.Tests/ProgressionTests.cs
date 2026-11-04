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
            var p = new PlayerProgress();
            Assert.That(p.AddXp(99), Is.EqualTo(0));
            Assert.That(p.AddXp(1), Is.EqualTo(1));
            Assert.That(p.Level, Is.EqualTo(2));
            Assert.That(p.Points, Is.EqualTo(1));
            Assert.That(p.Xp, Is.EqualTo(0));
            // 150 to level 3 and 200 to level 4 in one go.
            Assert.That(p.AddXp(360), Is.EqualTo(2));
            Assert.That(p.Level, Is.EqualTo(4));
            Assert.That(p.Xp, Is.EqualTo(10));
        }

        [Test]
        public void SkillsNeedPointsAndTheSkillBefore()
        {
            var p = new PlayerProgress();
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.False, "no points yet");
            p.AddXp(100 + 150 + 200 + 250);
            Assert.That(p.Points, Is.EqualTo(4));
            Assert.That(p.TryBuy(SkillId.RapidFire), Is.False, "needs Sharpshooter first");
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.TryBuy(SkillId.RapidFire), Is.True);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.True);
            Assert.That(p.Rank(SkillId.Sharpshooter), Is.EqualTo(3));
            p.AddXp(1000);
            Assert.That(p.TryBuy(SkillId.Sharpshooter), Is.False, "max rank");
            Assert.That(PlayerProgress.RankIn(p.PackedRanks, SkillId.RapidFire), Is.EqualTo(1));
            Assert.That(p.FireRateMultiplier, Is.EqualTo(1.1f).Within(1e-5f));
        }

        [Test]
        public void SkillsChangeCombatStats()
        {
            var settings = new CombatSettings();
            var v = new PlayerVitals(1, settings);
            var p = new PlayerProgress();
            p.AddXp(100 + 150 + 200);
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
        public void GlimmersDropALittleYellowAndNeverAttack()
        {
            Assert.That(EssenceColors.Of(EnemyFamily.Glimmer), Is.EqualTo(EssenceColor.Yellow));
            Assert.That(new EssenceDropSettings().DropFor(EnemyFamily.Glimmer), Is.EqualTo(1f));
            Assert.That(EnemyAttackSettings.For(EnemyFamily.Glimmer).Damage, Is.EqualTo(0f));
            Assert.That(new CombatSettings().EnemyHealth(EnemyFamily.Glimmer), Is.LessThanOrEqualTo(10f), "one base shot kills");
        }
    }
}
