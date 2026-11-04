using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class ArsenalTests
    {
        private static Mod M(ModKind k, ModTier t = ModTier.Common) => new Mod(k, t);

        // ---- Packing ----

        [Test]
        public void Gun_PacksAndUnpacks()
        {
            var gun = new Gun(FrameKind.Marksman, M(ModKind.Scope, ModTier.Epic), M(ModKind.Split, ModTier.Rare), M(ModKind.Reload),
                M(ModKind.Vigor, ModTier.Rare), M(ModKind.Haste, ModTier.Epic));
            Assert.That(Gun.Unpack(gun.Pack()), Is.EqualTo(gun));
            Assert.That(Gun.Unpack(0).IsEmpty, Is.True);
        }

        [Test]
        public void LootItem_PacksGunsAndModsApart()
        {
            var gun = LootItem.Of(new Gun(FrameKind.Pistol, payload0: M(ModKind.Might)));
            var mod = LootItem.Of(M(ModKind.Conductor, ModTier.Epic));
            var g2 = LootItem.Unpack(gun.Pack());
            var m2 = LootItem.Unpack(mod.Pack());
            Assert.That(g2.IsGun, Is.True);
            Assert.That(g2.Gun, Is.EqualTo(gun.Gun));
            Assert.That(m2.IsGun, Is.False);
            Assert.That(m2.Mod.Kind, Is.EqualTo(ModKind.Conductor));
            Assert.That(m2.Mod.Tier, Is.EqualTo(ModTier.Epic));
        }

        [Test]
        public void Loadout_PacksAndUnpacks()
        {
            var l = new Loadout(new Gun(FrameKind.Smg), new Gun(FrameKind.Beam, payload0: M(ModKind.Vigor)), 1);
            var (a, b, active) = l.Pack();
            var l2 = Loadout.Unpack(a, b, active);
            Assert.That(l2.GunA, Is.EqualTo(l.GunA));
            Assert.That(l2.GunB, Is.EqualTo(l.GunB));
            Assert.That(l2.Active, Is.EqualTo(1));
        }

        // ---- Stats ----

        [Test]
        public void Stats_EveryFrameHasSaneNumbers()
        {
            foreach (var f in GunFrames.All)
            {
                var s = GunStats.For(new Gun(f));
                Assert.That(s.Damage, Is.GreaterThan(0), f.ToString());
                Assert.That(s.Interval, Is.GreaterThan(0), f.ToString());
                Assert.That(s.Magazine, Is.GreaterThan(0), f.ToString());
                Assert.That(s.Range, Is.GreaterThan(0), f.ToString());
                Assert.That(s.Payload, Is.EqualTo(PayloadKind.None), f.ToString());
            }
            Assert.That(GunStats.For(Gun.Empty), Is.Null);
        }

        [Test]
        public void Stats_EpicModIs30PercentBetterThanCommon()
        {
            var bare = GunStats.For(new Gun(FrameKind.Rifle));
            var common = GunStats.For(new Gun(FrameKind.Rifle, M(ModKind.Damage)));
            var epic = GunStats.For(new Gun(FrameKind.Rifle, M(ModKind.Damage, ModTier.Epic)));
            float c = common.Damage / bare.Damage - 1f, e = epic.Damage / bare.Damage - 1f;
            Assert.That(e / c, Is.EqualTo(1.3f).Within(0.001f));
        }

        [Test]
        public void Stats_ModsChangeTheirNumbers()
        {
            var bare = GunStats.For(new Gun(FrameKind.Smg));
            var modded = GunStats.For(new Gun(FrameKind.Smg, M(ModKind.FireRate), M(ModKind.Stability), M(ModKind.Reload)));
            Assert.That(modded.Interval, Is.LessThan(bare.Interval));
            Assert.That(modded.Spread, Is.LessThan(bare.Spread));
            Assert.That(modded.Reload, Is.LessThan(bare.Reload));

            var scoped = GunStats.For(new Gun(FrameKind.Smg, M(ModKind.Scope)));
            Assert.That(scoped.Zoom, Is.GreaterThan(bare.Zoom));
            Assert.That(scoped.ZoomedSpread, Is.LessThan(bare.ZoomedSpread));

            var ranged = GunStats.For(new Gun(FrameKind.Smg, M(ModKind.Range)));
            Assert.That(ranged.Range, Is.GreaterThan(bare.Range));
            Assert.That(ranged.FalloffAt(30f), Is.GreaterThan(bare.FalloffAt(30f)));
        }

        [Test]
        public void Stats_MagazineModAlwaysAddsAtLeastOneRound()
        {
            var bare = GunStats.For(new Gun(FrameKind.Launcher));
            var mag = GunStats.For(new Gun(FrameKind.Launcher, M(ModKind.Magazine)));
            Assert.That(mag.Magazine, Is.GreaterThanOrEqualTo(bare.Magazine + 1));
        }

        [Test]
        public void Stats_SmgFallsOffWithDistance()
        {
            var s = GunStats.For(new Gun(FrameKind.Smg));
            Assert.That(s.FalloffAt(5f), Is.EqualTo(1f));
            Assert.That(s.FalloffAt(100f), Is.EqualTo(s.FalloffMin));
            Assert.That(s.FalloffAt((s.FalloffStart + s.FalloffEnd) / 2), Is.InRange(s.FalloffMin, 1f));
        }

        [Test]
        public void Stats_MarksmanIsAccurateOnlyWhenZoomed()
        {
            var s = GunStats.For(new Gun(FrameKind.Marksman));
            Assert.That(s.SpreadFor(false), Is.GreaterThan(0.5f));
            Assert.That(s.SpreadFor(true), Is.EqualTo(0f));
        }

        [Test]
        public void Stats_SplitWeakensBothPayloads()
        {
            var one = GunStats.For(new Gun(FrameKind.Rifle, payload0: M(ModKind.Might)));
            var split = GunStats.For(new Gun(FrameKind.Rifle, M(ModKind.Split), payload0: M(ModKind.Might), payload1: M(ModKind.Haste)));
            Assert.That(split.SecondPayload, Is.EqualTo(PayloadKind.Haste));
            Assert.That(split.PayloadStrength, Is.LessThan(one.PayloadStrength));
            Assert.That(split.SecondStrength, Is.EqualTo(split.PayloadStrength));

            // Without Split the second slot does nothing.
            var noSplit = new Gun(FrameKind.Rifle, payload0: M(ModKind.Might), payload1: M(ModKind.Haste));
            Assert.That(GunStats.For(noSplit).SecondPayload, Is.EqualTo(PayloadKind.None));
        }

        [Test]
        public void Stats_SpecialModsSetTheirFlags()
        {
            var s = GunStats.For(new Gun(FrameKind.Pistol, M(ModKind.Ricochet), M(ModKind.Conductor), M(ModKind.SelfShare)));
            Assert.That(s.Ricochet && s.Conductor && s.SelfShare, Is.True);
        }

        // ---- Equipping ----

        [Test]
        public void Equip_GunFillsEmptySlotThenReplacesGunInHand()
        {
            var l = new Loadout(new Gun(FrameKind.Pistol));
            var r1 = l.Equip(LootItem.Of(new Gun(FrameKind.Smg)));
            Assert.That(r1.GunSlot, Is.EqualTo(1));
            Assert.That(r1.Dropped.IsNone, Is.True);
            Assert.That(l.Active, Is.EqualTo(1));

            var r2 = l.Equip(LootItem.Of(new Gun(FrameKind.Launcher)));
            Assert.That(r2.GunSlot, Is.EqualTo(1));
            Assert.That(r2.Dropped.Gun.Frame, Is.EqualTo(FrameKind.Smg));
            Assert.That(l.GunA.Frame, Is.EqualTo(FrameKind.Pistol));
            Assert.That(l.GunB.Frame, Is.EqualTo(FrameKind.Launcher));
        }

        [Test]
        public void Equip_ModNeedsAGunInHand()
        {
            var l = new Loadout();
            var r = l.Equip(LootItem.Of(M(ModKind.Damage)));
            Assert.That(r.Equipped, Is.False);
            Assert.That(r.Refusal, Is.Not.Null);
        }

        [Test]
        public void Equip_ModFillsEmptySlotsThenReplacesSameKindThenWeakest()
        {
            var l = new Loadout(new Gun(FrameKind.Rifle));
            l.Equip(LootItem.Of(M(ModKind.Damage, ModTier.Rare)));
            l.Equip(LootItem.Of(M(ModKind.Range)));
            l.Equip(LootItem.Of(M(ModKind.Reload, ModTier.Epic)));
            Assert.That(l.InHand.Mod2.Kind, Is.EqualTo(ModKind.Reload));

            // Same kind: replaces the Damage mod.
            var r = l.Equip(LootItem.Of(M(ModKind.Damage, ModTier.Epic)));
            Assert.That(r.Dropped.Mod.Kind, Is.EqualTo(ModKind.Damage));
            Assert.That(r.Dropped.Mod.Tier, Is.EqualTo(ModTier.Rare));
            Assert.That(l.InHand.Mod0.Tier, Is.EqualTo(ModTier.Epic));

            // New kind: replaces the weakest (the common Range).
            r = l.Equip(LootItem.Of(M(ModKind.Scope)));
            Assert.That(r.Dropped.Mod.Kind, Is.EqualTo(ModKind.Range));
            Assert.That(l.InHand.Has(ModKind.Scope), Is.True);
        }

        [Test]
        public void Equip_PayloadGoesInPayloadSlotAndSwapsTheOldOne()
        {
            var l = new Loadout(new Gun(FrameKind.Pistol));
            var r = l.Equip(LootItem.Of(M(ModKind.Vigor)));
            Assert.That(r.Dropped.IsNone, Is.True);
            Assert.That(l.InHand.Payload, Is.EqualTo(PayloadKind.Vigor));

            r = l.Equip(LootItem.Of(M(ModKind.Might)));
            Assert.That(r.Dropped.Mod.Kind, Is.EqualTo(ModKind.Vigor));
            Assert.That(l.InHand.Payload, Is.EqualTo(PayloadKind.Might));
            Assert.That(l.InHand.Mod0.IsEmpty, Is.True, "payload mods never take a stat slot");
        }

        [Test]
        public void Equip_SplitOpensSecondPayloadSlotAndLosingItDropsThatPayload()
        {
            var l = new Loadout(new Gun(FrameKind.Rifle, M(ModKind.Split), M(ModKind.Damage), M(ModKind.Range), M(ModKind.Might)));
            l.Equip(LootItem.Of(M(ModKind.Haste)));
            Assert.That(l.InHand.SecondPayload, Is.EqualTo(PayloadKind.Haste));

            // An epic Split replacement keeps both payloads; a different mod replacing Split drops the second.
            l = new Loadout(new Gun(FrameKind.Rifle, M(ModKind.Split), M(ModKind.Damage, ModTier.Epic), M(ModKind.Range, ModTier.Epic),
                M(ModKind.Might), M(ModKind.Haste)));
            var r = l.Equip(LootItem.Of(M(ModKind.Scope, ModTier.Epic)));
            Assert.That(r.Dropped.Mod.Kind, Is.EqualTo(ModKind.Split));
            Assert.That(r.Dropped2.Mod.Kind, Is.EqualTo(ModKind.Haste));
            Assert.That(l.InHand.Payload1.IsEmpty, Is.True);
        }

        [Test]
        public void Swap_OnlyWhenTheOtherSlotHasAGun()
        {
            var l = new Loadout(new Gun(FrameKind.Pistol));
            Assert.That(l.Swap(), Is.False);
            l.GunB = new Gun(FrameKind.Smg);
            Assert.That(l.Swap(), Is.True);
            Assert.That(l.InHand.Frame, Is.EqualTo(FrameKind.Smg));
        }

        // ---- Firing ----

        [Test]
        public void Fire_HoldingFiresAtTheGunsRate()
        {
            var s = GunStats.For(new Gun(FrameKind.Smg));
            var st = GunState.Full(s);
            int shots = 0;
            for (double t = 0; t < 1.0 - 1e-6; t += 0.01) shots += FireControl.Step(ref st, s, t, true, false);
            Assert.That(shots, Is.InRange(12, 13)); // 0.08 s interval
            Assert.That(st.Ammo, Is.EqualTo(s.Magazine - shots));
        }

        [Test]
        public void Fire_BurstFiresAllItsRoundsFromOneTap()
        {
            var s = GunStats.For(new Gun(FrameKind.Rifle));
            var st = GunState.Full(s);
            int shots = FireControl.Step(ref st, s, 0, true, false);
            for (double t = 0.01; t < 0.3; t += 0.01) shots += FireControl.Step(ref st, s, t, false, false);
            Assert.That(shots, Is.EqualTo(s.BurstCount));
        }

        [Test]
        public void Fire_EmptyMagazineReloadsByItself()
        {
            var s = GunStats.For(new Gun(FrameKind.Launcher));
            var st = GunState.Full(s);
            double t = 0;
            int shots = 0;
            for (; t < 10 && shots < s.Magazine; t += 0.05) shots += FireControl.Step(ref st, s, t, true, false);
            Assert.That(st.Ammo, Is.EqualTo(0));
            Assert.That(st.Reloading, Is.True);
            Assert.That(FireControl.Step(ref st, s, t + 0.1, true, false), Is.EqualTo(0));
            FireControl.Step(ref st, s, t + s.Reload + 0.01, false, false);
            Assert.That(st.Ammo, Is.EqualTo(s.Magazine));
        }

        [Test]
        public void Fire_ReloadKeyOnlyWhenNotFull()
        {
            var s = GunStats.For(new Gun(FrameKind.Pistol));
            var st = GunState.Full(s);
            FireControl.Step(ref st, s, 0, false, true);
            Assert.That(st.Reloading, Is.False);
            FireControl.Step(ref st, s, 0.1, true, false);
            FireControl.Step(ref st, s, 0.5, false, true);
            Assert.That(st.Reloading, Is.True);
        }

        [Test]
        public void Fire_SwappingInDelaysTheFirstShot()
        {
            var s = GunStats.For(new Gun(FrameKind.Pistol));
            var st = GunState.Full(s);
            FireControl.SwappedIn(ref st, 1.0);
            Assert.That(FireControl.Step(ref st, s, 1.1, true, false), Is.EqualTo(0));
            Assert.That(FireControl.Step(ref st, s, 1.0 + FireControl.SwapTime + 0.01, true, false), Is.EqualTo(1));
        }

        [Test]
        public void Fire_BeamBuildsUpOnOneTarget()
        {
            var s = GunStats.For(new Gun(FrameKind.Beam));
            var st = GunState.Full(s);
            float first = FireControl.BeamMultiplier(ref st, s, 5, 0);
            float later = 0;
            for (double t = 0.1; t <= 2.0; t += 0.1) later = FireControl.BeamMultiplier(ref st, s, 5, t);
            Assert.That(first, Is.EqualTo(s.Frame.BeamMin).Within(1e-4));
            Assert.That(later, Is.EqualTo(s.Frame.BeamMax).Within(1e-4));
            Assert.That(FireControl.BeamMultiplier(ref st, s, 6, 2.1), Is.EqualTo(s.Frame.BeamMin).Within(1e-4), "new target restarts");
        }

        [Test]
        public void Spread_StaysInsideTheCone()
        {
            var rng = new CombatRandom(7);
            var aim = new Vector3(0, 0, 1);
            for (int i = 0; i < 200; i++)
            {
                var d = FireControl.Spread(aim, 3f, ref rng);
                float angle = (float)(System.Math.Acos(System.Math.Min(1f, Vector3.Dot(d, aim))) * 180 / System.Math.PI);
                Assert.That(angle, Is.LessThanOrEqualTo(3.01f));
            }
            Assert.That(FireControl.Spread(aim, 0f, ref rng), Is.EqualTo(aim));
        }

        // ---- Loot ----

        [Test]
        public void Loot_SameSeedSameItem()
        {
            for (uint seed = 1; seed < 50; seed++)
            {
                Assert.That(LootRolls.Roll(LootSource.Box, seed).Pack(), Is.EqualTo(LootRolls.Roll(LootSource.Box, seed).Pack()));
            }
        }

        [Test]
        public void Loot_BossDropsAreRareOrBetter()
        {
            for (uint seed = 1; seed < 300; seed++)
            {
                var item = LootRolls.Roll(LootSource.Boss, seed);
                Assert.That(item.IsNone, Is.False);
                Assert.That(item.Tier, Is.GreaterThanOrEqualTo(ModTier.Rare));
            }
        }

        [Test]
        public void Loot_BoxesMostlyCommonAndMixGunsAndMods()
        {
            int guns = 0, common = 0, n = 2000;
            for (uint seed = 1; seed <= n; seed++)
            {
                var item = LootRolls.Roll(LootSource.Box, seed * 2654435761u);
                if (item.IsGun) guns++;
                else if (item.Mod.Tier == ModTier.Common) common++;
            }
            Assert.That(guns / (float)n, Is.InRange(0.25f, 0.35f));
            Assert.That(common / (float)(n - guns), Is.InRange(0.58f, 0.72f));
        }

        [Test]
        public void Loot_StartingGunHasAPayloadAndNoStatMods()
        {
            var frames = new System.Collections.Generic.HashSet<FrameKind>();
            for (uint seed = 1; seed < 200; seed++)
            {
                var g = LootRolls.StartingGun(seed);
                frames.Add(g.Frame);
                Assert.That(g.Payload, Is.Not.EqualTo(PayloadKind.None));
                Assert.That(g.Mod0.IsEmpty && g.Mod1.IsEmpty && g.Mod2.IsEmpty, Is.True);
            }
            Assert.That(frames.Count, Is.EqualTo(GunFrames.All.Length));
        }

        [Test]
        public void Loot_BotGunCarriesItsRolesPayload()
        {
            Assert.That(LootRolls.BotGun(PayloadKind.Vigor, 3).Payload, Is.EqualTo(PayloadKind.Vigor));
            Assert.That(GunFrames.For(LootRolls.BotGun(PayloadKind.Might, 9).Frame).Mode, Is.Not.EqualTo(FireMode.Projectile));
        }

        [Test]
        public void LootBoxes_ClearOfLandmarksAndDeterministic()
        {
            var map = LayerGenerator.Generate(2026, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
            var boxes = LootBoxSites.For(map);
            var again = LootBoxSites.For(map);
            Assert.That(boxes.Count, Is.GreaterThan(map.Zones.Count));
            Assert.That(boxes.Select(b => b.World), Is.EqualTo(again.Select(b => b.World)));
            Assert.That(boxes.Select(b => b.Id).Distinct().Count(), Is.EqualTo(boxes.Count));
            foreach (var b in boxes)
            {
                var zone = map.Zones.First(z => z.Coord.Equals(b.Zone));
                foreach (var l in zone.Landmarks)
                {
                    Assert.That(Vector2.Distance(l.Point.World, b.World), Is.GreaterThanOrEqualTo(LootBoxSites.Clearance));
                }
            }
        }

        [Test]
        public void LootBoxLedger_EachPlayerOpensEachBoxOnce()
        {
            var ledger = new LootBoxLedger();
            Assert.That(ledger.TryOpen(3, 1), Is.True);
            Assert.That(ledger.TryOpen(3, 1), Is.False);
            Assert.That(ledger.TryOpen(3, 2), Is.True, "every player gets their own roll");
            Assert.That(ledger.OpenedBy(1), Is.EqualTo(1));
            Assert.That(LootBoxLedger.RollSeed(1, 3, 1), Is.Not.EqualTo(LootBoxLedger.RollSeed(1, 3, 2)));
        }
    }

    public class ArsenalCombatTests
    {
        private CombatSettings _s;
        private LobbyCombat _combat;

        [SetUp]
        public void SetUp()
        {
            _s = new CombatSettings();
            _combat = new LobbyCombat(_s);
        }

        private PlayerVitals Hurt(int id)
        {
            var p = new PlayerVitals(id, _s);
            p.TakeEnemyDamage(p.MaxHealth * 0.5f);
            return p;
        }

        [Test]
        public void NoPayload_HealsATinyBitAndStartsNoChain()
        {
            var a = new PlayerVitals(1, _s);
            var b = Hurt(2);
            float before = b.Health;
            var fx = ShotEffects.Of(PayloadKind.None);
            fx.PlainHeal = 1.5f;
            var r = _combat.HitPlayer(a, b, fx, 0);
            Assert.That(b.Health - before, Is.EqualTo(1.5f).Within(1e-4));
            Assert.That(r.Effect, Is.EqualTo(EffectKind.None));
            Assert.That(_combat.Chains.TryGetHeld(2, 0, out _), Is.False);
        }

        [Test]
        public void NoPayload_EnemyTakesDamageButNoDebuff()
        {
            var a = new PlayerVitals(1, _s);
            var e = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var r = _combat.HitEnemy(a, e, ShotEffects.Of(PayloadKind.None), 10f, false, 0);
            Assert.That(r.DamageDealt, Is.GreaterThan(0));
            Assert.That(e.Debuffs.Count, Is.EqualTo(0));
        }

        [Test]
        public void HealScale_ScalesVigor()
        {
            var a = new PlayerVitals(1, _s);
            var b = Hurt(2);
            var c = Hurt(3);
            var plain = ShotEffects.Of(PayloadKind.Vigor);
            var big = ShotEffects.Of(PayloadKind.Vigor);
            big.HealScale = 2f;
            var r1 = _combat.HitPlayer(a, b, plain, 0);
            var r2 = _combat.HitPlayer(a, c, big, 0);
            Assert.That(r2.Healed, Is.EqualTo(r1.Healed * 2f).Within(1e-3));
        }

        [Test]
        public void Split_AppliesBothPayloads()
        {
            var a = new PlayerVitals(1, _s);
            var e = new EnemyVitals(100, EnemyFamily.Skitter, _s);
            var fx = ShotEffects.Of(PayloadKind.Might);
            fx.Second = PayloadKind.Haste;
            fx.Strength = fx.SecondStrength = 0.6f;
            var r = _combat.HitEnemy(a, e, fx, 10f, false, 0);
            Assert.That(r.SecondPayload, Is.EqualTo(PayloadKind.Haste));
            Assert.That(e.Debuffs.Has(EffectKind.Expose), Is.True);
            Assert.That(e.Debuffs.Has(EffectKind.Slow), Is.True);
        }

        [Test]
        public void Conductor_GivesTheChainAnExtraLink()
        {
            var a = new PlayerVitals(1, _s);
            var b = new PlayerVitals(2, _s);
            _combat.HitPlayer(a, b, ShotEffects.Of(PayloadKind.Might), 0, null, conductor: true);
            Assert.That(_combat.Chains.TryGetHeld(2, 0, out var info), Is.True);
            Assert.That(info.Links, Is.EqualTo(2));
        }

        [Test]
        public void SelfShare_GivesTheShooterSomeOfTheBuff()
        {
            var a = Hurt(1);
            var result = new HitResult();
            float before = a.Health;
            _combat.Hits.GiveSelf(a, PayloadKind.Vigor, GunMods.SelfShare, 1f, ref result);
            Assert.That(a.Health, Is.GreaterThan(before));
            Assert.That(_combat.Hits.GiveSelf(a, PayloadKind.Relay, GunMods.SelfShare, 1f, ref result), Is.EqualTo(EffectOutcome.None));
        }
    }
}
