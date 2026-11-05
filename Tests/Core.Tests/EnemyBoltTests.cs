using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class EnemyBoltTests
    {
        private const float Dt = 1f / 60f;

        private static int Fly(ref EnemyBolt bolt, List<AttackCandidate> players, System.Action<float> move = null)
        {
            float t = 0f;
            while (!bolt.Done)
            {
                move?.Invoke(t);
                int hit = bolt.Step(Dt, players);
                t += Dt;
                if (hit >= 0) return hit;
            }
            return -1;
        }

        [Test]
        public void ABoltHitsSomeoneStandingStill()
        {
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 1, 12)) };
            var bolt = EnemyBolt.Fire(1, EnemyFamily.Wisp, new Vector3(0, 1, 0), players[0].Position, 16f, 0.3f, 6f, 20f, float.MaxValue);
            Assert.That(Fly(ref bolt, players), Is.EqualTo(0));
            Assert.That(bolt.Done);
        }

        [Test]
        public void MovingAsideDodgesIt_AndItFliesPast()
        {
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 1, 12)) };
            var bolt = EnemyBolt.Fire(1, EnemyFamily.Wisp, new Vector3(0, 1, 0), players[0].Position, 16f, 0.3f, 6f, 20f, float.MaxValue);
            // Strafing at walking speed (5 m/s) from the moment it fires.
            int hit = Fly(ref bolt, players, t => players[0] = new AttackCandidate(1, new Vector3(5f * t, 1, 12)));
            Assert.That(hit, Is.EqualTo(-1));
            Assert.That(bolt.Position.Z, Is.GreaterThan(25f), "it keeps going past where they stood");
        }

        [Test]
        public void CoverStopsIt()
        {
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 1, 12)) };
            var bolt = EnemyBolt.Fire(1, EnemyFamily.Skitter, new Vector3(0, 1, 0), players[0].Position, 18f, 0.35f, 8f, 15f, 6f);
            Assert.That(Fly(ref bolt, players), Is.EqualTo(-1));
            Assert.That(bolt.Position.Z, Is.EqualTo(6f).Within(0.01f));
        }

        [Test]
        public void EnemiesOnlyPickTargetsTheyCanSee()
        {
            var s = EnemyAttackSettings.ForWisp();
            var attack = EnemyAttack.Create(3, s);
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 0, 5)), new AttackCandidate(2, new Vector3(0, 0, 9)) };
            // Player 1 is behind a wall.
            System.Func<Vector3, Vector3, bool> canSee = (a, b) => b.Z > 8f;
            AttackEvent e = default;
            for (int i = 0; i < 600 && e.Kind != AttackEventKind.TelegraphStarted; i++) e = attack.Step(Dt, Vector3.Zero, players, s, canSee);
            Assert.That(e.Kind, Is.EqualTo(AttackEventKind.TelegraphStarted));
            Assert.That(e.TargetId, Is.EqualTo(2));

            var hidden = EnemyAttack.Create(3, s);
            for (int i = 0; i < 600; i++)
                Assert.That(hidden.Step(Dt, Vector3.Zero, players, s, (a, b) => false).Kind, Is.EqualTo(AttackEventKind.None));
        }

        [Test]
        public void MitesStillBiteInMelee()
        {
            var s = EnemyAttackSettings.ForMite();
            Assert.That(s.Ranged, Is.False);
            var attack = EnemyAttack.Create(5, s);
            var players = new List<AttackCandidate> { new AttackCandidate(1, new Vector3(0, 0, 1)) };
            AttackEvent e = default;
            for (int i = 0; i < 1000 && e.Kind != AttackEventKind.Hit; i++) e = attack.Step(Dt, Vector3.Zero, players, s);
            Assert.That(e.Kind, Is.EqualTo(AttackEventKind.Hit));
        }

        [Test]
        public void Colossus_HoversWithVolleys_ThenSlams_ThenSitsExposed()
        {
            var s = new ColossusSettings();
            var c = ColossusBehavior.Create(Vector3.Zero, 0f, s);
            int volleys = 0, slams = 0;
            float t = 0f, slamAt = -1f;
            bool warned = false;
            Assert.That(c.Exposed, Is.False);
            Assert.That(c.DamageMultiplier(s), Is.EqualTo(s.ArmorMultiplier));
            while (t < 30f && slams == 0)
            {
                var ev = c.Step(Dt, 1f, new Vector3(10, 0, 10), s);
                t += Dt;
                if ((ev & ColossusEvent.Volley) != 0) volleys++;
                if (c.Warning) warned = true;
                if ((ev & ColossusEvent.Slammed) != 0)
                {
                    slams++;
                    slamAt = t;
                }
            }
            Assert.That(volleys, Is.InRange(3, 4));
            Assert.That(warned, "it rises first, as a warning");
            Assert.That(slamAt, Is.EqualTo(s.HoverTime + s.RiseTime + s.SlamTime).Within(0.1f));
            Assert.That(c.Exposed);
            Assert.That(c.DamageMultiplier(s), Is.EqualTo(1f));
            Assert.That(c.Position.Y, Is.LessThan(s.HoverHeight));
            for (float u = 0; u < s.StunTime + 0.1f; u += Dt) c.Step(Dt, 1f, null, s);
            Assert.That(c.Exposed, Is.False, "the stun ends");
        }

        [Test]
        public void Colossus_VolleySpreadsAcrossTheLine()
        {
            var s = new ColossusSettings { VolleyBolts = 3, VolleySpread = 2f };
            var aims = ColossusBehavior.VolleyAims(Vector3.Zero, new Vector3(0, 0, 20), s);
            Assert.That(aims.Count, Is.EqualTo(3));
            Assert.That(aims[1], Is.EqualTo(new Vector3(0, 0, 20)));
            Assert.That(System.Math.Abs(aims[0].X - aims[2].X), Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void Shockwave_HitsThoseOnTheGround_AndJumpingClearsIt()
        {
            var wave = new Shockwave(1, Vector3.Zero, 13f, 36f, 1.4f, 22f);
            var standing = new WaveTarget(1, new Vector3(10, 0, 0), grounded: true);
            var caught = new List<int>();
            var players = new List<WaveTarget> { standing, new WaveTarget(2, new Vector3(0, 0, -10), grounded: false), new WaveTarget(3, new Vector3(20, 9, 0), true) };
            while (!wave.Done) wave.Step(Dt, players, caught);
            Assert.That(caught, Is.EqualTo(new[] { 1 }), "the jumper and the one on a high ledge are clear; each is hit once");
        }
    }
}
