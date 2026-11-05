using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class EnemyBehaviorTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void Wisp_IsOnlyHittableShortlyAfterEachBlink()
        {
            var s = new WispSettings();
            var w = WispBehavior.Create(new Vector3(5, 0, 5), 42, s);
            int blinks = 0;
            float sinceBlink = 0;
            for (int i = 0; i < 60 * 30; i++)
            {
                Vector3 before = w.Position;
                bool blinked = w.Step(Dt, s);
                if (blinked)
                {
                    blinks++;
                    sinceBlink = 0;
                    Assert.That(Vector3.Distance(before, w.Position), Is.GreaterThanOrEqualTo(s.MinBlinkDistance - 1e-4f));
                }
                else
                {
                    sinceBlink += Dt;
                    Assert.That(w.Position, Is.EqualTo(before), "it only moves by blinking");
                }
                if (blinks > 0)
                {
                    if (sinceBlink < s.HittableWindow - Dt) Assert.That(w.IsHittable, Is.True);
                    if (sinceBlink > s.HittableWindow + Dt) Assert.That(w.IsHittable, Is.False);
                    Assert.That(w.ToHitTarget(1, s).Hittable, Is.EqualTo(w.IsHittable));
                }

                float height = w.Position.Y - w.Anchor.Y;
                Assert.That(height, Is.InRange(s.MinHeight, s.MaxHeight));
                float flat = new Vector2(w.Position.X - w.Anchor.X, w.Position.Z - w.Anchor.Z).Length();
                Assert.That(flat, Is.LessThanOrEqualTo(s.BlinkRadius + 1e-4f));
            }
            // 30 s at one blink per 1.2-2.0 s.
            Assert.That(blinks, Is.InRange(14, 26));
        }

        [Test]
        public void Wisp_HittableWindowLastsAboutPointSixSeconds()
        {
            var s = new WispSettings();
            var w = WispBehavior.Create(Vector3.Zero, 3, s);
            while (!w.Step(Dt, s)) { }
            float hittable = 0;
            while (w.IsHittable)
            {
                hittable += Dt;
                w.Step(Dt, s);
            }
            Assert.That(hittable, Is.EqualTo(s.HittableWindow).Within(2 * Dt));
        }

        [Test]
        public void Skitter_StaysOnLeashAndMovesFast()
        {
            var s = new SkitterSettings();
            var home = new Vector3(0, 0.6f, 0);
            var k = SkitterBehavior.Create(home, 7, s);
            int turns = 0;
            float travelled = 0;
            for (int i = 0; i < 60 * 60; i++)
            {
                Vector3 before = k.Position;
                if (k.Step(Dt, s)) turns++;
                travelled += Vector3.Distance(before, k.Position);
                float flat = new Vector2(k.Position.X - home.X, k.Position.Z - home.Z).Length();
                Assert.That(flat, Is.LessThanOrEqualTo(s.LeashRadius + 1e-3f));
                Assert.That(k.Position.Y, Is.EqualTo(home.Y));
            }
            // A direction change every 0.2-0.6 s over 60 s.
            Assert.That(turns, Is.InRange(100, 320));
            Assert.That(travelled / 60f, Is.GreaterThan(s.Speed * 0.8f));
        }

        [Test]
        public void Skitter_CoreIsAWeakPoint()
        {
            var s = new SkitterSettings();
            var k = SkitterBehavior.Create(new Vector3(0, 0.6f, 10), 1, s);
            var t = k.ToHitTarget(9, s);
            Assert.That(t.HasWeakPoint, Is.True);
            Assert.That(t.WeakPointRadius, Is.EqualTo(0.2f));
            Assert.That(HitScan.Cast(new Vector3(0, 0.6f, 0), Vector3.UnitZ, 100, new[] { t }, out var hit), Is.True);
            Assert.That(hit.WeakPoint, Is.True);
        }

        [Test]
        public void Behaviors_AreDeterministicForASeed()
        {
            var ws = new WispSettings();
            var ks = new SkitterSettings();
            var w1 = WispBehavior.Create(Vector3.Zero, 99, ws);
            var w2 = WispBehavior.Create(Vector3.Zero, 99, ws);
            var w3 = WispBehavior.Create(Vector3.Zero, 100, ws);
            var k1 = SkitterBehavior.Create(Vector3.Zero, 99, ks);
            var k2 = SkitterBehavior.Create(Vector3.Zero, 99, ks);
            bool differs = false;
            for (int i = 0; i < 600; i++)
            {
                w1.Step(Dt, ws);
                w2.Step(Dt, ws);
                w3.Step(Dt, ws);
                k1.Step(Dt, ks);
                k2.Step(Dt, ks);
                Assert.That(w1.Position, Is.EqualTo(w2.Position));
                Assert.That(k1.Position, Is.EqualTo(k2.Position));
                differs |= w1.Position != w3.Position;
            }
            Assert.That(differs, Is.True);
        }

        [Test]
        public void MiteSwarm_HasEightToTwentySeparateMitesThatDriftSlowly()
        {
            var s = new MiteSwarmSettings();
            for (uint seed = 1; seed <= 20; seed++)
            {
                var swarm = new MiteSwarm(new Vector3(0, 0, 0), seed, s);
                Assert.That(swarm.Mites.Length, Is.InRange(8, 20));
                for (int step = 0; step < 600; step++)
                {
                    Vector3 before = swarm.Centre.Position;
                    swarm.Step(Dt);
                    float moved = Vector3.Distance(before, swarm.Centre.Position);
                    Assert.That(moved, Is.LessThanOrEqualTo(s.DriftSpeed * Dt + 1e-4f));
                    float flat = new Vector2(swarm.Centre.Position.X, swarm.Centre.Position.Z).Length();
                    Assert.That(flat, Is.LessThanOrEqualTo(s.LeashRadius + 1e-3f));
                }
                for (int i = 0; i < swarm.Mites.Length; i++)
                {
                    for (int j = i + 1; j < swarm.Mites.Length; j++)
                    {
                        float d = Vector3.Distance(swarm.Mites[i].Position, swarm.Mites[j].Position);
                        Assert.That(d, Is.GreaterThan(2 * s.MiteRadius), "mites overlap, seed " + seed);
                    }
                    Assert.That(Vector3.Distance(swarm.Mites[i].Position, swarm.Centre.Position),
                        Is.LessThanOrEqualTo(s.MaxSpread + s.BobHeight + 1e-3f));
                }
            }
        }

        [Test]
        public void MiteSwarm_EachMiteIsOneHit()
        {
            var cs = new CombatSettings();
            var hits = new HitResolver(cs);
            var shooter = new PlayerVitals(1, cs);
            var swarm = new MiteSwarm(Vector3.Zero, 5, new MiteSwarmSettings(), count: 10);
            for (int i = 0; i < swarm.Mites.Length; i++)
            {
                var mite = new EnemyVitals(100 + i, EnemyFamily.Mite, cs);
                Assert.That(hits.HitEnemy(shooter, mite, PayloadKind.Might, 1, false).Killed, Is.True);
                swarm.Alive[i] = false;
            }
            Assert.That(swarm.AliveCount, Is.EqualTo(0));
        }

        [Test]
        public void Attack_TelegraphsThenFiresAtNearestPlayerInRange()
        {
            var s = EnemyAttackSettings.ForSkitter();
            var attack = EnemyAttack.Create(11, s);
            var players = new List<AttackCandidate>
            {
                new AttackCandidate(1, new Vector3(0, 0, 12)),
                new AttackCandidate(2, new Vector3(0, 0, 6)),
                new AttackCandidate(3, new Vector3(0, 0, 3), canBeAttacked: false),
            };

            float t = 0, telegraphAt = -1, hitAt = -1;
            AttackEvent hit = default;
            while (t < 10 && hitAt < 0)
            {
                var e = attack.Step(Dt, Vector3.Zero, players, s);
                t += Dt;
                if (e.Kind == AttackEventKind.TelegraphStarted)
                {
                    telegraphAt = t;
                    Assert.That(e.TargetId, Is.EqualTo(2));
                }
                if (e.Kind == AttackEventKind.Fired)
                {
                    hitAt = t;
                    hit = e;
                }
            }
            Assert.That(hit.Aim, Is.EqualTo(new Vector3(0, 0, 6)), "a Skitter fires a bolt where the target stood");
            Assert.That(telegraphAt, Is.InRange(s.MinCooldown, s.MaxCooldown + Dt));
            Assert.That(hitAt - telegraphAt, Is.EqualTo(s.TelegraphTime).Within(Dt * 1.5));
            Assert.That(hit.TargetId, Is.EqualTo(2));
            Assert.That(hit.Damage, Is.EqualTo(8));
            Assert.That(attack.Phase, Is.EqualTo(AttackPhase.Cooldown));
        }

        [Test]
        public void Attack_MissesIfTargetLeavesRangeDuringTelegraph()
        {
            var s = EnemyAttackSettings.ForMite();
            var attack = EnemyAttack.Create(2, s);
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 0, 1)) };
            AttackEvent e = default;
            while (e.Kind != AttackEventKind.TelegraphStarted) e = attack.Step(Dt, Vector3.Zero, players, s);
            players[0] = new AttackCandidate(1, new Vector3(0, 0, 5));
            while (e.Kind != AttackEventKind.Missed && e.Kind != AttackEventKind.Hit) e = attack.Step(Dt, Vector3.Zero, players, s);
            Assert.That(e.Kind, Is.EqualTo(AttackEventKind.Missed));
        }

        [Test]
        public void Attack_WaitsWhileNoOneIsInRange()
        {
            var s = EnemyAttackSettings.ForWisp();
            var attack = EnemyAttack.Create(2, s);
            var far = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 0, 50)) };
            for (int i = 0; i < 600; i++)
                Assert.That(attack.Step(Dt, Vector3.Zero, far, s).Kind, Is.EqualTo(AttackEventKind.None));
            var near = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 0, 5)) };
            Assert.That(attack.Step(Dt, Vector3.Zero, near, s).Kind, Is.EqualTo(AttackEventKind.TelegraphStarted),
                "a waiting enemy telegraphs as soon as someone comes in range");
        }
    }
}
