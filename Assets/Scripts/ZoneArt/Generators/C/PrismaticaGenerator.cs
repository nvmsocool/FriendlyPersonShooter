using System.Collections;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// "Prismatica": a geometric dreamscape. Terraced plateaus with candy-striped strata, basalt hex-column
    /// fields that step up in walkable increments, towers wrapped in floating spiral stairs, bridges,
    /// landmark set pieces (crystal spires, monolith rings, rainbow arches, floating spirals, giant trees,
    /// hex ziggurats), a dense varied flora of invented species, and a painterly sky with planets and moons.
    ///
    /// Builds incrementally: all shaping, connectivity, walk checks, placement and mesh data run on worker threads
    /// (<see cref="PrismBuild"/>), and the main thread only turns finished data into meshes, colliders and objects
    /// in short steps. Nothing is shared between builds, so zones may be built side by side.
    /// </summary>
    public sealed class PrismaticaGenerator : IIncrementalZoneGenerator
    {
        public string Name => "prismatica";

        public ZoneResult Build(ZoneRequest zone, Transform parent)
        {
            var result = new ZoneResult();
            ZoneBuild.RunToEnd(BuildSteps(zone, parent, result));
            return result;
        }

        public IEnumerator BuildSteps(ZoneRequest request, Transform parent, ZoneResult result) => new PrismBuild(request, parent, result).Run();

        internal static LandmarkKind PickLandmark(TileKind tile, HashSet<LandmarkKind> used, ref ZoneRandom rng, PrismBiome biome = PrismBiome.Prismatica)
        {
            // CrystalSpire, MonolithRing, RainbowArch, FloatingSpiral, GiantTree, Ziggurat
            float[] w = tile switch
            {
                TileKind.Ruins => new float[] { 1, 4, 2, 1, 0.5f, 3 },
                TileKind.Crater => new float[] { 5, 1, 1, 1.5f, 0.5f, 1.5f },
                TileKind.Forest => new float[] { 1, 1.5f, 1, 1, 5, 1 },
                TileKind.Tower => new float[] { 1.5f, 1, 1.5f, 4, 0.5f, 2 },
                TileKind.Canyon => new float[] { 2, 1, 4, 1.5f, 0.5f, 1.5f },
                TileKind.Lake => new float[] { 2.5f, 1, 2, 3, 1.5f, 1 },
                TileKind.River => new float[] { 1.5f, 1, 4, 1.5f, 2, 1 },
                _ => new float[] { 1.5f, 2.5f, 2, 1.5f, 3, 2 },
            };
            PrismBiomes.Landmarks(biome, w);
            if (used.Count < w.Length) foreach (var k in used) w[(int)k] = 0f;
            return (LandmarkKind)rng.Weighted(w);
        }

        internal static float HexThreshold(ZoneRequest zone, float frac)
        {
            var oMask = ZoneNoise.Offset(zone.Seed, 21);
            var vals = new List<float>();
            // Sampled over the zone itself (away from its edges), so the share of hex ground matches the zone.
            float step = zone.Size / 50f;
            for (int z = 0; z < 50; z++)
            {
                for (int x = 0; x < 50; x++)
                {
                    float wx = zone.Origin.x + (x + 0.5f) * step, wz = zone.Origin.z + (z + 0.5f) * step;
                    if (!zone.Contains(wx, wz, 15f)) continue;
                    vals.Add(ZoneNoise.Fbm(wx + oMask.x, wz + oMask.y, 1f / 65f, 3));
                }
            }
            if (vals.Count == 0) return 1f;
            vals.Sort();
            return vals[Mathf.Clamp((int)((1f - frac) * vals.Count), 0, vals.Count - 1)];
        }

        internal static Material Lit(string name, PrismPalette pal, float wrap, float toon, float breakup, float rim)
        {
            var m = ZoneLook.Material("ZoneArtC/PrismLit", name);
            m.SetFloat("_Wrap", wrap);
            m.SetFloat("_Toon", toon);
            m.SetFloat("_Breakup", breakup);
            m.SetFloat("_Rim", rim);
            m.SetColor("_RimColor", pal.Rim);
            m.SetColor("_ShadowColor", pal.ShadowTint);
            m.SetFloat("_Glow", 4f);
            return m;
        }

        internal static void SetupSky(PrismPalette pal, Vector3 heroDir, ref ZoneRandom rng)
        {
            var sky = ZoneLook.Material("ZoneArtC/PrismSky", "Prismatica sky");
            sky.SetColor("_Zenith", pal.SkyZenith);
            sky.SetColor("_Mid", pal.SkyMid);
            sky.SetColor("_Horizon", pal.SkyHorizon);
            sky.SetColor("_Below", pal.Fog);
            sky.SetFloat("_MidHeight", rng.Range(0.18f, 0.4f));
            sky.SetColor("_SunColor", pal.Sun);
            sky.SetFloat("_SunSize", rng.Range(0.002f, 0.006f));
            sky.SetFloat("_SunHalo", rng.Range(0.6f, 1.3f));
            sky.SetFloat("_SunRays", rng.Range(0.1f, 0.5f));
            sky.SetColor("_CloudLit", pal.CloudLit);
            sky.SetColor("_CloudShade", pal.CloudShade);
            sky.SetColor("_CloudRim", pal.CloudRim);
            sky.SetFloat("_CloudCover", rng.Range(0.5f, 0.62f));
            sky.SetFloat("_CloudScale", rng.Range(0.7f, 1.2f));
            sky.SetVector("_CloudSeed", new Vector4(rng.Range(-50f, 50f), rng.Range(-50f, 50f), rng.Range(0f, 6f), 0));
            sky.SetVector("_Swirl1", new Vector4(rng.Range(-1.5f, 1.5f), rng.Range(-1.5f, 1.5f), rng.Range(0.6f, 1.2f), rng.Sign() * rng.Range(0.9f, 1.8f)));
            sky.SetVector("_Swirl2", new Vector4(rng.Range(-2f, 2f), rng.Range(-2f, 2f), rng.Range(0.5f, 1.0f), rng.Sign() * rng.Range(0.7f, 1.5f)));
            sky.SetFloat("_Streak", rng.Range(0.2f, 0.7f));
            sky.SetColor("_BankBack", pal.BankBack);
            sky.SetColor("_BankMid", pal.BankMid);
            sky.SetColor("_BankFront", pal.BankFront);
            sky.SetFloat("_BankHeight", rng.Range(0.06f, 0.15f));
            sky.SetFloat("_BankSeed", rng.Range(0f, 100f));
            if (pal.Biome == PrismBiome.Glowdeep)
            {
                // Night: a full star field and nearly always an aurora.
                sky.SetFloat("_Stars", rng.Range(1.5f, 2.2f));
                sky.SetFloat("_Aurora", rng.Chance(0.9f) ? rng.Range(0.9f, 1.5f) : 0.4f);
            }
            else
            {
                sky.SetFloat("_Stars", pal.Dusk ? rng.Range(0.8f, 1.4f) : rng.Range(0.25f, 0.7f));
                sky.SetFloat("_Aurora", pal.Dusk ? (rng.Chance(0.7f) ? rng.Range(0.5f, 1.2f) : 0f) : (rng.Chance(0.15f) ? rng.Range(0.3f, 0.6f) : 0f));
            }
            sky.SetColor("_AuroraA", pal.AuroraA);
            sky.SetColor("_AuroraB", pal.AuroraB);

            Vector4 Dir(float azDeg, float elevDeg, float radius)
            {
                float a = azDeg * Mathf.Deg2Rad, e = elevDeg * Mathf.Deg2Rad;
                return new Vector4(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a), radius);
            }
            float heroAz = Mathf.Atan2(heroDir.z, heroDir.x) * Mathf.Rad2Deg;
            int skyStyle = rng.Weighted(new[] { 0.5f, 0.25f, 0.25f }); // planet, sky ring, big moons
            if (skyStyle == 1)
            {
                // Ring plane normal tilted 45-65 degrees from vertical, leaning away from the hero view, so the band arcs over the target.
                float tilt = rng.Range(45f, 65f) * Mathf.Deg2Rad;
                float naz = (heroAz + 180f + rng.Range(-30f, 30f)) * Mathf.Deg2Rad;
                var rn = new Vector3(Mathf.Cos(naz) * Mathf.Sin(tilt), Mathf.Cos(tilt), Mathf.Sin(naz) * Mathf.Sin(tilt));
                sky.SetVector("_SkyRing", new Vector4(rn.x, rn.y, rn.z, rng.Range(0.1f, 0.17f)));
                sky.SetColor("_SkyRingCol", Color.Lerp(pal.Ring, Color.white, 0.25f));
            }
            else sky.SetVector("_SkyRing", Vector4.zero);
            if (skyStyle == 0 || (skyStyle == 1 && rng.Chance(0.35f)))
            {
                bool arc = rng.Chance(0.35f);
                float pAz = heroAz + rng.Range(-25f, 25f);
                if (arc) sky.SetVector("_Planet", Dir(pAz, rng.Range(-4f, 6f), rng.Range(0.35f, 0.55f)));
                else sky.SetVector("_Planet", Dir(pAz, rng.Range(14f, 32f), rng.Range(0.1f, 0.22f)));
                sky.SetVector("_PlanetParams", new Vector4(rng.Range(5f, 13f), rng.Range(-0.5f, 0.5f), rng.Range(0.2f, 0.35f), rng.Chance(0.6f) ? 1f : 0f));
            }
            else sky.SetVector("_Planet", Vector4.zero);
            sky.SetColor("_PlanetA", pal.PlanetA);
            sky.SetColor("_PlanetB", pal.PlanetB);
            sky.SetColor("_PlanetC", pal.PlanetC);
            sky.SetColor("_RingColor", pal.Ring);
            int moons = skyStyle == 2 ? 3 : rng.Range(1, 3);
            var names = new[] { "_Moon1", "_Moon2", "_Moon3" };
            for (int i = 0; i < 3; i++)
            {
                if (i < moons)
                {
                    float az = heroAz + 60f + i * rng.Range(70f, 120f);
                    if (skyStyle == 2 && i == 0) az = heroAz + rng.Range(-20f, 20f);
                    float mr = skyStyle == 2 ? (i == 0 ? rng.Range(0.09f, 0.15f) : rng.Range(0.03f, 0.07f)) : rng.Range(0.015f, 0.05f);
                    sky.SetVector(names[i], Dir(az, rng.Range(14f, 45f), mr));
                    sky.SetColor(names[i] + "Col", pal.Moons[i]);
                }
                else sky.SetVector(names[i], Vector4.zero);
            }
            RenderSettings.skybox = sky;
        }
    }
}
