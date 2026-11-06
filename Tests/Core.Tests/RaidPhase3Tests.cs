using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Raid runs, phase 3: Lanterns, Sentinel Eye, the Weaver, Low Gravity and Fog.</summary>
    public class RaidPhase3Tests
    {
        [Test]
        public void LanternsOnlyLetGlowingPlayersLightTheShades()
        {
            var l = new Lanterns(4);
            Assert.That(l.Target, Is.EqualTo(14));
            var players = new List<(int, Vector3, bool)> { (1, Vector3.Zero, true), (2, new Vector3(20f, 0f, 0f), true) };
            var shade = new Vector3(3f, 1f, 0f);
            Assert.That(l.InLight(shade, players, 0), Is.False, "nobody glows yet");

            l.Lit(1, 0);
            Assert.That(l.Glowing(1, 5), Is.True);
            Assert.That(l.InLight(shade, players, 5), Is.True);
            Assert.That(l.InLight(new Vector3(12f, 1f, 0f), players, 5), Is.False, "too far from the light");
            Assert.That(l.InLight(shade, players, Lanterns.GlowSeconds + 0.1), Is.False, "the glow wears off");

            players[0] = (1, Vector3.Zero, false);
            Assert.That(l.InLight(shade, players, 1), Is.False, "a downed player's light is out");

            var alone = new Lanterns(1);
            Assert.That(alone.InLight(shade, new List<(int, Vector3, bool)> { (1, Vector3.Zero, true) }, 0), Is.True, "a lone player always glows");

            for (int i = 0; i < l.Target - 1; i++) Assert.That(l.Killed(), Is.False);
            Assert.That(l.Killed(), Is.True);
            Assert.That(Lanterns.KillsOf(l.Pack()), Is.EqualTo(l.Target));
            Assert.That(Lanterns.TargetOf(l.Pack()), Is.EqualTo(l.Target));
        }

        [Test]
        public void SentinelEyeWarnsOpensAndCountsHits()
        {
            var eye = new SentinelEye(3, 4, 0);
            Assert.That(eye.Target, Is.EqualTo(10));
            Assert.That(eye.Step(1), Is.EqualTo(EyeEvent.None));
            Assert.That(eye.Step(SentinelEye.FirstOpen), Is.EqualTo(EyeEvent.Warn));
            Assert.That(eye.IsWarning, Is.True);
            Assert.That(eye.Hit(SentinelEye.FirstOpen + 0.1), Is.False, "a warning spot isn't open yet");
            double open = SentinelEye.FirstOpen + SentinelEye.WarnSeconds;
            Assert.That(eye.Step(open), Is.EqualTo(EyeEvent.Opened));
            int packed = eye.Pack();
            Assert.That(SentinelEye.OpenOf(packed), Is.True);
            Assert.That(SentinelEye.AzimuthOf(packed), Is.EqualTo(eye.Azimuth));
            Assert.That(SentinelEye.ElevationOf(packed), Is.EqualTo(eye.Elevation));
            Assert.That(eye.Hit(open + 0.2), Is.True);
            Assert.That(eye.IsOpen, Is.False, "a hit shuts it");
            Assert.That(eye.Hit(open + 0.3), Is.False);

            // Left alone, the next one shuts by itself.
            double t = open + 0.3;
            int last = eye.Azimuth;
            while (eye.Step(t) != EyeEvent.Warn) t += 0.05;
            int gap = System.Math.Abs(((eye.Azimuth - last + 24) % 16) - 8);
            Assert.That(gap, Is.GreaterThanOrEqualTo(3), "the eye moves to another side");
            while (eye.Step(t) != EyeEvent.Opened) t += 0.05;
            while (eye.Step(t) != EyeEvent.Missed) t += 0.05;
            Assert.That(eye.Misses, Is.EqualTo(1));

            // The eye sits on the shell, and turns with it.
            var a = SentinelEye.EyeAt(4, 1, 0);
            var b = SentinelEye.EyeAt(4, 1, 5);
            Assert.That(Vector3.Distance(a, SentinelEye.ShellCenter), Is.EqualTo(SentinelEye.EyeOut).Within(1e-3));
            Assert.That(Vector3.Distance(a, b), Is.GreaterThan(1f));
        }

        [Test]
        public void WeaverFloorCracksThenFallsAndNeverLosesThePillarTiles()
        {
            var w = new RaidWeaver(5, 4, 0);
            Assert.That(w.StepFloor(1), Is.False);
            Assert.That(w.StepFloor(RaidWeaver.FirstFall), Is.False, "a crack first");
            Assert.That(w.Cracking, Is.Not.Zero);
            Assert.That(w.StepFloor(RaidWeaver.FirstFall + RaidWeaver.CrackSeconds), Is.True);
            Assert.That(w.TilesFallen, Is.EqualTo(1));

            double t = RaidWeaver.FirstFall;
            for (int i = 0; i < 400; i++)
            {
                t += 0.5;
                w.StepFloor(t);
                int fallen = 0;
                for (int k = 0; k < RaidWeaver.Grid * RaidWeaver.Grid; k++)
                {
                    if (!w.Standing(k)) fallen++;
                    if (RaidWeaver.Safe(k)) Assert.That(w.Standing(k), Is.True);
                }
                Assert.That(fallen, Is.LessThanOrEqualTo(RaidWeaver.MaxFallen));
            }
            Assert.That(w.TilesFallen, Is.GreaterThan(RaidWeaver.MaxFallen), "the oldest holes fill back in");

            int safe = w.NearestSafeTile(new Vector3(0f, 0f, 0f));
            Assert.That(w.Standing(safe), Is.True);
            Assert.That(RaidWeaver.IsSet(w.Cracking, safe), Is.False);
            Assert.That(RaidWeaver.TileAt(-23.9f, -23.9f), Is.EqualTo(0));
            Assert.That(RaidWeaver.TileAt(23.9f, 23.9f), Is.EqualTo(63));
            Assert.That(RaidWeaver.TileAt(30f, 0f), Is.EqualTo(-1));
            Assert.That(RaidWeaver.TileAt(10f, 10f), Is.EqualTo(5 + 5 * 8), "a pillar's tile");
            Assert.That(RaidWeaver.Safe(RaidWeaver.TileAt(-10f, 10f)), Is.True);
        }

        [Test]
        public void WeaverThreadsShieldItUntilSomeoneElseCutsThem()
        {
            var w = new RaidWeaver(5, 3, 0);
            var up = new List<int> { 1, 2, 3 };
            var snapped = new List<WeaverTether>();
            Assert.That(w.StepTethers(1, up, snapped), Is.Zero);
            Assert.That(w.DamageMultiplier, Is.EqualTo(1f));
            Assert.That(w.StepTethers(RaidWeaver.FirstTether, up, snapped), Is.EqualTo(2), "a pair, and the odd one out to the Weaver");
            Assert.That(w.DamageMultiplier, Is.EqualTo(RaidWeaver.ShieldedDamage));

            int pair = w.Tethers[0].B == WeaverTether.WeaverEnd ? 1 : 0;
            var t = w.Tethers[pair];
            Assert.That(w.HitTether(pair, t.A), Is.EqualTo(TetherHit.Refused), "you can't cut your own thread");
            int outsider = 6 - t.A - t.B;
            for (int i = 0; i < RaidWeaver.TetherHits - 1; i++) Assert.That(w.HitTether(pair, outsider), Is.EqualTo(TetherHit.Hit));
            Assert.That(w.HitTether(pair, outsider), Is.EqualTo(TetherHit.Cut));
            Assert.That(w.Tethers.Count, Is.EqualTo(1));

            ulong packed = w.PackTethers();
            Assert.That(RaidWeaver.UnpackTether(packed, 0, out int a, out int b, out int hits), Is.True);
            Assert.That(b, Is.EqualTo(WeaverTether.WeaverEnd));
            Assert.That(a, Is.EqualTo(w.Tethers[0].A));
            Assert.That(hits, Is.EqualTo(RaidWeaver.TetherHits));
            Assert.That(RaidWeaver.UnpackTether(packed, 1, out _, out _, out _), Is.False);

            w.StepTethers(RaidWeaver.FirstTether + RaidWeaver.TetherLife, up, snapped);
            Assert.That(snapped.Count, Is.EqualTo(1), "left too long, it snaps");
            Assert.That(w.DamageMultiplier, Is.EqualTo(1f));

            // A thread goes when one of its players goes down.
            w.StepTethers(RaidWeaver.FirstTether + RaidWeaver.TetherEvery, up, snapped);
            Assert.That(w.Tethers.Count, Is.EqualTo(2));
            w.StepTethers(RaidWeaver.FirstTether + RaidWeaver.TetherEvery + 1, new List<int>(), snapped);
            Assert.That(w.Tethers.Count, Is.Zero);
        }

        [Test]
        public void RunsCanRollThePhase3RoomsBossAndModifiers()
        {
            var r = new RaidRun(4, 4, first: RaidRoomKind.Lanterns, modifier: RaidModifier.LowGravity, boss: RaidBossKind.Weaver);
            r.Begin(0);
            Assert.That(r.Lanterns, Is.Not.Null);
            Assert.That(r.GravityMultiplier, Is.LessThan(1f));
            Assert.That(new RaidRun(4, 4, modifier: RaidModifier.Fog).GravityMultiplier, Is.EqualTo(1f));
            r.MarkSolved();
            r.Advance(10);
            r.Advance(20);
            r.Advance(30);
            Assert.That(r.AtBoss, Is.True);
            Assert.That(r.Weaver, Is.Not.Null);
            Assert.That(r.StageName, Is.EqualTo("The Weaver"));

            var e = new RaidRun(4, 4, first: RaidRoomKind.SentinelEye);
            e.Begin(0);
            Assert.That(e.Eye, Is.Not.Null);
            Assert.That(e.Layout.Targets[0], Is.EqualTo(SentinelEye.ShellCenter));
            Assert.That(RaidRoomLayout.NameOf(RaidModifier.LowGravity), Is.EqualTo("Low Gravity"));
        }
    }
}
