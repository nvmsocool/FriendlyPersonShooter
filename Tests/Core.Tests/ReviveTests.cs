using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ReviveTests
    {
        private const float Dt = 1f / 30f;
        private CombatSettings _s;
        private PlayerVitals _downed;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _downed = new PlayerVitals(9, _s);
            _downed.TakeEnemyDamage(1000);
        }

        private ReviveState NewRevive(uint seed = 42)
        {
            return new ReviveState(_downed, new Vector3(10, 1, 5), seed, _s.Revive);
        }

        private static void Run(ReviveState r, float seconds)
        {
            int steps = (int)System.Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) r.Step(Dt);
        }

        [Test]
        public void Waves_BurstOutOnScheduleAndOldFragmentsFade()
        {
            var r = NewRevive();
            Assert.That(r.Fragments, Is.Empty);
            Run(r, 0.6f);
            Assert.That(r.WavesSpawned, Is.EqualTo(1));
            Assert.That(r.Fragments.Count, Is.EqualTo(5));
            Assert.That(r.Fragments.Count(f => f.Motion == FragmentMotion.Zigzag), Is.EqualTo(2));
            Assert.That(r.Fragments.Count(f => f.Motion == FragmentMotion.Arc), Is.EqualTo(3));

            Run(r, 3.5f);
            Assert.That(r.WavesSpawned, Is.EqualTo(2));
            Assert.That(r.Fragments.Count, Is.EqualTo(10));

            // First wave (born at 0.5 s) is gone by 5.6 s, second still there.
            Run(r, 1.5f);
            Assert.That(r.Fragments.All(f => f.Wave >= 1), Is.True);
            Assert.That(r.Status, Is.EqualTo(ReviveStatus.Bleeding));
        }

        [Test]
        public void Fragments_FlyAroundTheBodyAndMove()
        {
            var r = NewRevive();
            Run(r, 1.5f);
            var s = _s.Revive;
            var before = r.Fragments.ToDictionary(f => f.Id, f => f.Position);
            r.Step(0.1f);
            foreach (var f in r.Fragments)
            {
                Vector3 off = f.Position - r.BodyCenter;
                float flat = new Vector2(off.X, off.Z).Length();
                Assert.That(flat, Is.LessThanOrEqualTo(s.MaxOrbitRadius + 1e-3f));
                Assert.That(off.Y, Is.InRange(s.MinHeight - s.ArcBob - 1e-3f, s.MaxHeight + s.ArcBob + 1e-3f));
                if (f.Motion == FragmentMotion.Arc)
                {
                    Assert.That(flat, Is.GreaterThanOrEqualTo(s.MinOrbitRadius - 1e-3f), "arcs have burst out to their orbit");
                    Assert.That(Vector3.Distance(before[f.Id], f.Position), Is.GreaterThan(0.1f), "arcs always move");
                }
            }
        }

        [Test]
        public void Fragments_ZigzagsPauseAndChangeDirection()
        {
            var r = NewRevive();
            Run(r, 0.6f);
            int id = r.Fragments.First(f => f.Motion == FragmentMotion.Zigzag).Id;
            var dirs = new List<Vector3>();
            int pausedSteps = 0;
            Vector3 last = Pos(r, id);
            for (int i = 0; i < 90; i++)
            {
                r.Step(Dt);
                if (!r.TryGetFragment(id, out var f)) break;
                Vector3 d = f.Position - last;
                if (d.Length() < 1e-5f) pausedSteps++;
                else dirs.Add(Vector3.Normalize(d));
                last = f.Position;
            }
            Assert.That(pausedSteps, Is.GreaterThan(0));
            bool turned = false;
            for (int i = 1; i < dirs.Count; i++)
            {
                if (Vector3.Dot(dirs[i], dirs[i - 1]) < 0.7f) turned = true;
            }
            Assert.That(turned, Is.True, "a zigzag changes direction sharply");
        }

        [Test]
        public void Fragments_AreDeterministicForASeed()
        {
            var a = NewRevive(7);
            var b = NewRevive(7);
            var c = NewRevive(8);
            for (int i = 0; i < 200; i++)
            {
                a.Step(Dt);
                b.Step(Dt);
                c.Step(Dt);
            }
            Assert.That(a.Fragments.Select(f => f.Position), Is.EqualTo(b.Fragments.Select(f => f.Position)));
            Assert.That(a.Fragments.Select(f => f.Position), Is.Not.EqualTo(c.Fragments.Select(f => f.Position)));
        }

        [Test]
        public void HitTargets_UseFragmentPositionsAndAnIdBase()
        {
            var r = NewRevive();
            Run(r, 1f);
            var targets = new List<HitTarget>();
            r.AppendHitTargets(targets, 10000);
            Assert.That(targets.Count, Is.EqualTo(r.Fragments.Count));
            var f = r.Fragments[2];
            var t = targets[2];
            Assert.That(t.Id, Is.EqualTo(10000 + f.Id));
            Assert.That(t.BodyRadius, Is.EqualTo(0.2f));

            Vector3 eye = f.Position + new Vector3(0, 0, -5);
            Assert.That(HitScan.Cast(eye, f.Position - eye, 20, targets, out var hit), Is.True);
            Assert.That(hit.TargetId - 10000, Is.EqualTo(f.Id));
        }

        [Test]
        public void Revive_EightHitsFromSeveralPlayersAddUp()
        {
            var r = NewRevive();
            Run(r, 4.2f);
            var ids = r.Fragments.Select(f => f.Id).ToList();
            for (int i = 0; i < 7; i++)
            {
                var hit = r.RegisterHit(ids[i], i % 2 == 0 ? 1 : 2, PayloadKind.Might);
                Assert.That(hit.Accepted, Is.True);
                Assert.That(hit.Progress, Is.EqualTo(i + 1));
                Assert.That(hit.Revived, Is.False);
            }
            Assert.That(r.Fragments.Any(f => f.Id == ids[0]), Is.False, "a hit fragment is destroyed");
            Assert.That(r.RegisterHit(ids[0], 1, PayloadKind.Might).Refused, Is.EqualTo(ReviveRefusal.NoSuchFragment));

            var last = r.RegisterHit(ids[7], 3, PayloadKind.Haste);
            Assert.That(last.Revived, Is.True);
            Assert.That(r.Status, Is.EqualTo(ReviveStatus.Revived));
            Assert.That(_downed.IsDown, Is.False);
            Assert.That(_downed.Health, Is.EqualTo(50));
            Assert.That(r.Contributions[1], Is.EqualTo(4));
            Assert.That(r.Contributions[2], Is.EqualTo(3));
            Assert.That(r.Contributions[3], Is.EqualTo(1));
            Assert.That(r.Fragments, Is.Empty);
            Assert.That(r.RegisterHit(ids[8], 1, PayloadKind.Might).Refused, Is.EqualTo(ReviveRefusal.Finished));
        }

        [Test]
        public void Revive_VigorCountsDouble()
        {
            var r = NewRevive();
            Run(r, 4.2f);
            var ids = r.Fragments.Select(f => f.Id).ToList();
            Assert.That(r.RegisterHit(ids[0], 1, PayloadKind.Vigor).Value, Is.EqualTo(2));
            for (int i = 1; i < 4; i++) r.RegisterHit(ids[i], 1, PayloadKind.Vigor);
            Assert.That(r.Status, Is.EqualTo(ReviveStatus.Revived));
        }

        [Test]
        public void Revive_RelayedVigorCountsDoubleThroughTheResolver()
        {
            var hits = new HitResolver(_s);
            var relayer = new PlayerVitals(1, _s) { RelayCarry = PayloadKind.Vigor };
            var shooter = new PlayerVitals(2, _s);
            hits.HitPlayer(relayer, shooter, PayloadKind.Relay);
            var r = NewRevive();
            Run(r, 1f);
            var hit = hits.HitFragment(shooter, r, r.Fragments[0].Id, PayloadKind.Might);
            Assert.That(hit.Value, Is.EqualTo(2));
            Assert.That(shooter.RelayHitsLeft, Is.EqualTo(4));
        }

        [Test]
        public void Revive_DownedPlayerCantHitOwnFragments()
        {
            var r = NewRevive();
            Run(r, 1f);
            var hit = r.RegisterHit(r.Fragments[0].Id, _downed.Id, PayloadKind.Vigor);
            Assert.That(hit.Accepted, Is.False);
            Assert.That(hit.Refused, Is.EqualTo(ReviveRefusal.SelfHit));
            Assert.That(r.Progress, Is.EqualTo(0));
            Assert.That(r.Fragments.Count, Is.EqualTo(5), "the fragment survives");
        }

        [Test]
        public void BleedOut_RespawnsAfterThirtySeconds()
        {
            var r = NewRevive();
            Run(r, 4.2f);
            r.RegisterHit(r.Fragments[0].Id, 1, PayloadKind.Might);
            Run(r, 25.5f);
            Assert.That(r.Status, Is.EqualTo(ReviveStatus.Bleeding));
            Assert.That(r.BleedOutLeft, Is.GreaterThan(0));
            Assert.That(_downed.IsDown, Is.True);
            Run(r, 0.5f);
            Assert.That(r.Status, Is.EqualTo(ReviveStatus.BledOut));
            Assert.That(r.Fragments, Is.Empty);
            Assert.That(_downed.IsDown, Is.False);
            Assert.That(_downed.Health, Is.EqualTo(_downed.MaxHealth));
            Assert.That(r.Settings.EssenceDropShare, Is.EqualTo(0.5f));
            Assert.That(r.Step(Dt), Is.EqualTo(ReviveStatus.BledOut));
        }

        [Test]
        public void Downed_PlayersRefuseBuffsUntilRevived()
        {
            var hits = new HitResolver(_s);
            var a = new PlayerVitals(1, _s);
            Assert.That(hits.HitPlayer(a, _downed, PayloadKind.Haste).Refused, Is.EqualTo(RefuseReason.TargetDown));
            var r = NewRevive();
            Run(r, 4.2f);
            foreach (var id in r.Fragments.Take(8).Select(f => f.Id).ToList()) r.RegisterHit(id, 1, PayloadKind.Might);
            Assert.That(hits.HitPlayer(a, _downed, PayloadKind.Haste).Refused, Is.EqualTo(RefuseReason.None));
        }

        private static Vector3 Pos(ReviveState r, int id)
        {
            Assert.That(r.TryGetFragment(id, out var f), Is.True);
            return f.Position;
        }
    }
}
