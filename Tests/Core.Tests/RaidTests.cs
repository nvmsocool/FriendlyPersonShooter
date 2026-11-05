using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class RaidTests
    {
        private static readonly int[] None = new int[0];

        [Test]
        public void StrangersPoolTheKeyCarriedFirstThenBanked()
        {
            var door = new RaidDoor();
            var bank = new EssenceAmounts();
            var a = new EssenceWallet(1, bank);
            var b = new EssenceWallet(2);
            a.PickUp(EssenceColor.Red, 8);
            a.PickUp(EssenceColor.Yellow, 30);
            a.BankAll();
            a.PickUp(EssenceColor.Red, 5);
            Assert.That(door.Pledge(a), Is.EqualTo(13));
            Assert.That(a.Carried(EssenceColor.Red), Is.EqualTo(0));
            Assert.That(a.Banked(EssenceColor.Red), Is.EqualTo(0));
            Assert.That(a.Banked(EssenceColor.Yellow), Is.EqualTo(30), "the key never takes other colors");

            b.PickUp(EssenceColor.Red, 30);
            b.PickUp(EssenceColor.Green, 12);
            Assert.That(door.Pledge(b), Is.EqualTo(7 + 12), "only what the key still needs");
            Assert.That(b.Carried(EssenceColor.Red), Is.EqualTo(23));
            Assert.That(door.KeyFull, Is.True);
        }

        [Test]
        public void FullKeyGathersWhoeverIsAtTheDoorThenRuns()
        {
            var door = new RaidDoor();
            door.FillKey();
            Assert.That(door.Update(0, None), Is.EqualTo(RaidDoorEvent.None), "waits for someone at the door");
            Assert.That(door.Update(1, new[] { 5 }), Is.EqualTo(RaidDoorEvent.GatherStarted));
            Assert.That(door.Update(5, new[] { 5, 6 }), Is.EqualTo(RaidDoorEvent.None));
            Assert.That(door.Pledge(new EssenceWallet(9)), Is.EqualTo(0), "no pledges while gathering");
            Assert.That(door.Update(1 + door.Settings.GatherSeconds, new[] { 5, 6, 7 }), Is.EqualTo(RaidDoorEvent.Started));
            Assert.That(door.Participants, Is.EqualTo(new[] { 5, 6, 7 }));
            Assert.That(door.IsInside(6), Is.True);
            Assert.That(door.KeyFull, Is.False, "the key is used up");
        }

        [Test]
        public void EmptyDoorAtTheEndOfTheCountdownKeepsTheKey()
        {
            var door = new RaidDoor();
            door.FillKey();
            door.Update(0, new[] { 1 });
            Assert.That(door.Update(100, None), Is.EqualTo(RaidDoorEvent.GatherCancelled));
            Assert.That(door.Phase, Is.EqualTo(RaidPhase.Collecting));
            Assert.That(door.KeyFull, Is.True);
        }

        [Test]
        public void AtMostEightGoIn()
        {
            var door = new RaidDoor();
            door.FillKey();
            door.Update(0, new[] { 1 });
            var crowd = new List<int>();
            for (int i = 1; i <= 12; i++) crowd.Add(i);
            door.Update(100, crowd);
            Assert.That(door.Participants.Count, Is.EqualTo(8));
        }

        [Test]
        public void RunEndsOnWipeOrTimeoutAndTheDoorReopens()
        {
            var door = new RaidDoor();
            door.FillKey();
            door.Update(0, new[] { 1, 2 });
            door.Update(20, new[] { 1, 2 });
            Assert.That(door.Check(30, 2), Is.EqualTo(RaidOutcome.None));
            Assert.That(door.Check(30, 0), Is.EqualTo(RaidOutcome.Wiped), "everyone down at once");
            Assert.That(door.Check(20 + door.Settings.TimeLimit, 2), Is.EqualTo(RaidOutcome.TimedOut));
            door.Leave(1);
            door.Leave(2);
            Assert.That(door.Check(31, 0), Is.EqualTo(RaidOutcome.Wiped), "everyone left");

            door.Finish(40, RaidOutcome.Cleared);
            Assert.That(door.Phase, Is.EqualTo(RaidPhase.Resetting));
            Assert.That(door.LastOutcome, Is.EqualTo(RaidOutcome.Cleared));
            Assert.That(door.Update(41, None), Is.EqualTo(RaidDoorEvent.None));
            Assert.That(door.Update(40 + door.Settings.ResetSeconds, None), Is.EqualTo(RaidDoorEvent.Reopened));
            Assert.That(door.Phase, Is.EqualTo(RaidPhase.Collecting));
        }

        [Test]
        public void BulwarkPlateOpensOnARhythmAndOnlyItTakesFullDamage()
        {
            var s = new BulwarkSettings();
            var boss = BulwarkBehavior.Create(Vector3.Zero, 0f, s);
            boss.HitOutcome(true, s, out float shutMultiplier, out bool shutWeak);
            Assert.That(shutWeak, Is.False);
            Assert.That(shutMultiplier, Is.EqualTo(s.ArmorMultiplier));

            float t = 0f;
            while (!boss.Open && t < 20f)
            {
                boss.Step(0.05f, 1f, 1f, s);
                t += 0.05f;
                if (!boss.Open && t > s.ClosedTime - s.WarnTime + 0.06f && t < s.ClosedTime - 0.06f) Assert.That(boss.OpeningSoon(s), Is.True);
            }
            Assert.That(t, Is.EqualTo(s.ClosedTime).Within(0.06f));
            boss.HitOutcome(true, s, out float openMultiplier, out bool openWeak);
            Assert.That(openWeak, Is.True);
            Assert.That(openMultiplier, Is.EqualTo(1f));
            boss.HitOutcome(false, s, out float bodyMultiplier, out _);
            Assert.That(bodyMultiplier, Is.EqualTo(s.ArmorMultiplier), "the shell stays armored while the plate is open");
            Assert.That(boss.Yaw, Is.GreaterThan(0f), "it turns");
        }

        [Test]
        public void SlowSlowsItsTurnAndItTurnsFasterBelowHalf()
        {
            var s = new BulwarkSettings();
            var normal = BulwarkBehavior.Create(Vector3.Zero, 0f, s);
            var slowed = normal;
            var enraged = normal;
            normal.Step(1f, 1f, 1f, s);
            slowed.Step(1f, 1f, 0.6f, s);
            enraged.Step(1f, 0.4f, 1f, s);
            Assert.That(slowed.Yaw, Is.LessThan(normal.Yaw));
            Assert.That(enraged.Yaw, Is.GreaterThan(normal.Yaw));
        }

        [Test]
        public void AddWavesComeOnceEachAtTheirThresholds()
        {
            var s = new BulwarkSettings();
            var boss = BulwarkBehavior.Create(Vector3.Zero, 0f, s);
            Assert.That(boss.TakeWaveDue(0.9f, s), Is.EqualTo(0));
            Assert.That(boss.TakeWaveDue(0.74f, s), Is.EqualTo(1));
            Assert.That(boss.TakeWaveDue(0.70f, s), Is.EqualTo(0));
            // A big burst past two thresholds: one wave now, the next on the following check.
            Assert.That(boss.TakeWaveDue(0.2f, s), Is.EqualTo(2));
            Assert.That(boss.TakeWaveDue(0.2f, s), Is.EqualTo(3));
            Assert.That(boss.TakeWaveDue(0.0f, s), Is.EqualTo(0));
        }

        [Test]
        public void BossHealthScalesWithTheGroup()
        {
            var combat = new CombatSettings();
            Assert.That(BulwarkSettings.HealthFor(4, combat), Is.EqualTo(4 * combat.BulwarkHealth));
            Assert.That(BulwarkSettings.HealthFor(0, combat), Is.EqualTo(combat.BulwarkHealth));
            var v = new EnemyVitals(1, EnemyFamily.Bulwark, combat, 3000f);
            Assert.That(v.MaxHealth, Is.EqualTo(3000f));
            Assert.That(v.Health, Is.EqualTo(3000f));
        }

        [Test]
        public void SurfacePlateCantBeShotThroughTheShell()
        {
            var s = new BulwarkSettings();
            var center = new Vector3(0f, 0f, 0f);
            var plate = BulwarkBehavior.PlateCenterOf(center, 0f, s); // faces +z
            var target = HitTarget.WithWeakPoint(1, center, s.BodyRadius, plate, s.PlateRadius);
            target.WeakPointOnSurface = true;
            var targets = new[] { target };

            Assert.That(HitScan.Cast(new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, -1f), 100f, targets, out var front), Is.True);
            Assert.That(front.WeakPoint, Is.True, "from the plate's side");
            Assert.That(HitScan.Cast(new Vector3(0f, 0f, -20f), new Vector3(0f, 0f, 1f), 100f, targets, out var back), Is.True);
            Assert.That(back.WeakPoint, Is.False, "from behind, through the shell");

            // Without the flag the old rule holds: a pass through the weak point counts.
            target.WeakPointOnSurface = false;
            Assert.That(HitScan.Cast(new Vector3(0f, 0f, -20f), new Vector3(0f, 0f, 1f), 100f, new[] { target }, out var through), Is.True);
            Assert.That(through.WeakPoint, Is.True);
        }

        [Test]
        public void DoorStandsByAShrineAwayFromStartAndEdges()
        {
            var map = LayerGenerator.Generate(2026, LayerSettings.MvpTiling(), LayerSettings.ForMvp());
            var door = RaidSite.DoorFor(map);
            Assert.That(RaidSite.ClearOfSeam(map, door), Is.True);
            Assert.That(map.Distance(map.StartPoint.World, door), Is.GreaterThanOrEqualTo(RaidSite.MinFromStart));
            Assert.That(RaidSite.DoorFor(map), Is.EqualTo(door), "same map, same door");
        }

        [Test]
        public void BulwarkIsPurpleAndPaysOnlyThroughTheRaid()
        {
            Assert.That(EssenceColors.Of(EnemyFamily.Bulwark), Is.EqualTo(EssenceColor.Purple));
            Assert.That(new EssenceDropSettings().DropFor(EnemyFamily.Bulwark), Is.EqualTo(0f));
            Assert.That(Progression.XpRewards.Kill(EnemyFamily.Bulwark), Is.EqualTo(0));
            Assert.That(EnemyAttackSettings.For(EnemyFamily.Bulwark).Damage, Is.GreaterThan(EnemyAttackSettings.ForSkitter().Damage));
        }
    }
}
