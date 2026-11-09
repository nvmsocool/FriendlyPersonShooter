using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Frenzy, Ward and Focus, and the five combos they make.</summary>
    public class NewPayloadsTests
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
        public void Payloads_MapToTheirEffects()
        {
            Assert.That(Payloads.AllyEffect(PayloadKind.Frenzy), Is.EqualTo(EffectKind.Frenzied));
            Assert.That(Payloads.AllyEffect(PayloadKind.Ward), Is.EqualTo(EffectKind.Warded));
            Assert.That(Payloads.AllyEffect(PayloadKind.Focus), Is.EqualTo(EffectKind.Focused));
            Assert.That(Payloads.EnemyEffect(PayloadKind.Frenzy), Is.EqualTo(EffectKind.Jam));
            Assert.That(Payloads.EnemyEffect(PayloadKind.Ward), Is.EqualTo(EffectKind.Brittle));
            Assert.That(Payloads.EnemyEffect(PayloadKind.Focus), Is.EqualTo(EffectKind.Mark));
            foreach (var p in new[] { PayloadKind.Frenzy, PayloadKind.Ward, PayloadKind.Focus })
            {
                var mod = GunMods.ForPayload(p);
                Assert.That(GunMods.IsPayload(mod), Is.True, p.ToString());
                Assert.That(GunMods.Payload(mod), Is.EqualTo(p));
                Assert.That(GunMods.PayloadKinds, Does.Contain(mod));
                Assert.That(GunMods.Describe(new Mod(mod)), Is.Not.Empty);
            }
        }

        [Test]
        public void Frenzy_SpeedsAllyFireAndReload_JamsEnemyAttacks()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            _lobby.HitPlayer(a, b, PayloadKind.Frenzy, 0);
            Assert.That(b.FireRateMultiplier, Is.EqualTo(1f + _s.FrenzyBonus).Within(1e-5));
            Assert.That(b.ReloadSpeedMultiplier, Is.EqualTo(1f + _s.FrenzyBonus).Within(1e-5));

            var e = new EnemyVitals(100, EnemyFamily.Sentinel, _s);
            Assert.That(e.AttackTimeScale, Is.EqualTo(1f));
            _lobby.HitEnemy(a, e, PayloadKind.Frenzy, 1, false, 0);
            Assert.That(e.AttackTimeScale, Is.EqualTo(1f / (1f + _s.JamAmount)).Within(1e-5));
        }

        [Test]
        public void Frenzy_ShortensReloads()
        {
            var s = GunStats.For(new Gun(FrameKind.Launcher));
            var st = GunState.Full(s);
            FireControl.StartReload(ref st, s, 0, 2f);
            Assert.That(st.ReloadTime, Is.EqualTo(s.Reload / 2f).Within(1e-5));
        }

        [Test]
        public void Ward_ShieldsAndAbsorbsAfterVigorShield()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            var r = _lobby.HitPlayer(a, b, PayloadKind.Ward, 0);
            Assert.That(r.ShieldGained, Is.EqualTo(_s.WardShield).Within(1e-4));
            Assert.That(b.Ward, Is.EqualTo(_s.WardShield).Within(1e-4));

            b.TakeEnemyDamage(_s.WardShield + 5f);
            Assert.That(b.Health, Is.EqualTo(b.MaxHealth - 5f).Within(1e-4), "the Ward soaks its points first");
            Assert.That(b.Buffs.Has(EffectKind.Warded), Is.True, "an emptied Ward stays until it runs out");
            Assert.That(b.Ward, Is.EqualTo(0f));

            // The same giver tops it back up.
            r = _lobby.HitPlayer(a, b, PayloadKind.Ward, 0.5);
            Assert.That(b.Ward, Is.EqualTo(_s.WardShield).Within(1e-4));
            Assert.That(r.ShieldGained, Is.EqualTo(_s.WardShield).Within(1e-4));
        }

        [Test]
        public void Ward_MakesArmorBrittle()
        {
            var a = new PlayerVitals(1, _s);
            var e = new EnemyVitals(100, EnemyFamily.Sentinel, _s);
            Assert.That(e.Armor(0.3f), Is.EqualTo(0.3f));
            _lobby.HitEnemy(a, e, PayloadKind.Ward, 1, false, 0);
            Assert.That(e.Armor(0.3f), Is.EqualTo(0.3f + 0.7f * _s.BrittleAmount).Within(1e-5));
            Assert.That(e.Armor(1f), Is.EqualTo(1f));
        }

        [Test]
        public void Focus_GrowsWeakPointsForTheAlly_MarkGrowsThemForEveryone()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            _lobby.HitPlayer(a, b, PayloadKind.Focus, 0);
            Assert.That(b.WeakPointBonus, Is.EqualTo(_s.FocusWeakPointBonus).Within(1e-5));
            Assert.That(a.WeakPointBonus, Is.EqualTo(0f), "no self-buffing");

            var e = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            _lobby.HitEnemy(a, e, PayloadKind.Focus, 1, false, 0);
            Assert.That(e.WeakPointScale, Is.EqualTo(1f + _s.MarkWeakPointBonus).Within(1e-5));
        }

        [Test]
        public void Overdrive_HasteAndFrenzy_StopsReloads()
        {
            var a = new PlayerVitals(1, _s);
            var c = new PlayerVitals(3, _s);
            var b = new PlayerVitals(2, _s);
            var events = new List<ComboEvent>();
            _lobby.HitPlayer(a, b, PayloadKind.Haste, 0);
            Assert.That(b.NoReload, Is.False);
            _lobby.HitPlayer(c, b, PayloadKind.Frenzy, 0.1, events);
            Assert.That(b.NoReload, Is.True);
            Assert.That(events.Exists(x => x.Kind == ComboKind.Overdrive), Is.True);

            var s = GunStats.For(new Gun(FrameKind.Launcher));
            var st = GunState.Full(s);
            int shots = 0;
            for (double t = 0; t < 20; t += 0.05) shots += FireControl.Step(ref st, s, t, true, false, 1f, 1f, noReload: true);
            Assert.That(shots, Is.GreaterThan(s.Magazine * 2));
            Assert.That(st.Ammo, Is.EqualTo(s.Magazine));
            Assert.That(st.Reloading, Is.False);
        }

        [Test]
        public void Deadeye_MightAndFocus_TriplesWeakPointHits()
        {
            var a = new PlayerVitals(1, _s);
            var c = new PlayerVitals(3, _s);
            var b = new PlayerVitals(2, _s);
            _lobby.HitPlayer(a, b, PayloadKind.Might, 0);
            _lobby.HitPlayer(c, b, PayloadKind.Focus, 0.1);
            Assert.That(b.IsDeadeye, Is.True);

            var e = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            float weak = _lobby.Hits.EnemyDamage(b, e, 10f, true);
            float body = _lobby.Hits.EnemyDamage(b, e, 10f, false);
            Assert.That(weak / body, Is.EqualTo(_s.DeadeyeWeakPointMultiplier).Within(1e-4));
        }

        [Test]
        public void Bastion_VigorShieldAndWard_RegeneratesTheWard()
        {
            var a = new PlayerVitals(1, _s);
            var c = new PlayerVitals(3, _s);
            var b = new PlayerVitals(2, _s);
            _lobby.HitPlayer(a, b, PayloadKind.Ward, 0);
            b.TakeEnemyDamage(_s.WardShield);
            Assert.That(b.Ward, Is.EqualTo(0f));
            b.Tick(1f);
            Assert.That(b.Ward, Is.EqualTo(0f), "no regeneration without Vigor's shield");

            // Full health, so Vigor overflows into a shield: Bastion forms.
            b.SetHealth(b.MaxHealth);
            _lobby.HitPlayer(c, b, PayloadKind.Vigor, 1);
            Assert.That(b.IsBastion, Is.True);
            b.Tick(1f);
            Assert.That(b.Ward, Is.EqualTo(_s.BastionRegen).Within(1e-4));
            for (int i = 0; i < 2; i++) b.Tick(1f);
            Assert.That(b.Ward, Is.EqualTo(_s.WardShield).Within(1e-4), "never above one Ward's worth");
        }

        [Test]
        public void Pinned_SlowAndMark_FreezesOnce()
        {
            var a = new PlayerVitals(1, _s);
            var e = new EnemyVitals(100, EnemyFamily.Diver, _s);
            _lobby.HitEnemy(a, e, PayloadKind.Haste, 1, false, 0);
            Assert.That(e.IsFrozen, Is.False);
            _lobby.HitEnemy(a, e, PayloadKind.Focus, 1, false, 0);
            Assert.That(e.IsPinned, Is.True);
            Assert.That(e.IsFrozen, Is.True);
            Assert.That(e.MoveMultiplier, Is.EqualTo(0f));
            Assert.That(e.AttackTimeScale, Is.EqualTo(0f));

            e.Tick(_s.PinnedFreeze + 0.01f);
            Assert.That(e.IsFrozen, Is.False);
            _lobby.HitEnemy(a, e, PayloadKind.Focus, 1, false, 0);
            Assert.That(e.IsFrozen, Is.False, "it freezes as the combo forms, not on every hit");
        }

        [Test]
        public void Shatter_ExposeAndBrittle_BreaksArmor()
        {
            var a = new PlayerVitals(1, _s);
            var e = new EnemyVitals(100, EnemyFamily.Bulwark, _s);
            _lobby.HitEnemy(a, e, PayloadKind.Ward, 1, false, 0);
            Assert.That(e.Armor(0.2f), Is.LessThan(1f));
            _lobby.HitEnemy(a, e, PayloadKind.Might, 1, false, 0);
            Assert.That(e.IsShattered, Is.True);
            Assert.That(e.Armor(0.2f), Is.EqualTo(1f));
        }

        [Test]
        public void ComboTracker_KnowsEveryCombo()
        {
            Assert.That(ComboTracker.AllKinds.Length, Is.EqualTo(7));
            var all = ComboKind.None;
            foreach (var k in ComboTracker.AllKinds) all |= k;
            Assert.That(all, Is.EqualTo(ComboKind.Plague | ComboKind.Blitz | ComboKind.Overdrive | ComboKind.Deadeye |
                                        ComboKind.Bastion | ComboKind.Pinned | ComboKind.Shatter));
        }
    }
}
