using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// Each layer of the world is its own biome, built on the same terrain, tiling, structures and walk checks:
    /// <list type="bullet">
    /// <item><b>Prismatica</b> (layer 1): the bright geometric dreamscape.</item>
    /// <item><b>Wonderwood</b> (layer 2): a forest of giant flora at dusk. Spiral-trunked trees crowned with huge
    /// petals, jellyfish caps trailing glowing tendrils, shelf-fungus spires, under a magenta and orange sunset.</item>
    /// <item><b>Glowdeep</b> (layer 3): a night of dark basalt and crystal lit by bioluminescent plants: crystal
    /// trees, glowing coral, anemones and lantern caps, with auroras and big moons.</item>
    /// </list>
    /// Layer 1 must build exactly as before: biome tweaks never draw from the random streams it uses.
    /// </summary>
    public enum PrismBiome
    {
        Prismatica,
        Wonderwood,
        Glowdeep,
    }

    internal static class PrismBiomes
    {
        public static PrismBiome For(ZoneRequest zone) => zone.Layer >= 3 ? PrismBiome.Glowdeep : zone.Layer == 2 ? PrismBiome.Wonderwood : PrismBiome.Prismatica;

        /// <summary>Terrain character: Wonderwood rolls softly with few basalt columns; Glowdeep is sharper and full of them.</summary>
        public static void Shape(PrismBiome biome, PrismTerrain.Shape s)
        {
            switch (biome)
            {
                case PrismBiome.Wonderwood:
                    s.Soft = Mathf.Min(0.55f, s.Soft * 1.5f);
                    s.HexFrac *= 0.45f;
                    s.PillarChance *= 0.5f;
                    break;
                case PrismBiome.Glowdeep:
                    s.Soft *= 0.7f;
                    s.HexFrac = Mathf.Min(0.5f, s.HexFrac * 1.35f);
                    s.PillarChance *= 1.6f;
                    break;
            }
        }

        /// <summary>Landmark weights (CrystalSpire, MonolithRing, RainbowArch, FloatingSpiral, GiantTree, Ziggurat) scaled per biome.</summary>
        public static void Landmarks(PrismBiome biome, float[] w)
        {
            switch (biome)
            {
                case PrismBiome.Wonderwood:
                    w[4] *= 4f; w[3] *= 1.5f; w[0] *= 0.5f; w[5] *= 0.5f;
                    break;
                case PrismBiome.Glowdeep:
                    w[0] *= 4f; w[1] *= 1.5f; w[2] *= 0.4f; w[4] *= 0.6f;
                    break;
            }
        }

        /// <summary>How much more (or less) tree and shrub cover the biome has than Prismatica.</summary>
        public static void Cover(PrismBiome biome, ref float trees, ref float shrubs)
        {
            switch (biome)
            {
                case PrismBiome.Wonderwood:
                    trees = Mathf.Min(0.9f, trees * 1.2f + 0.05f);
                    shrubs = Mathf.Min(1f, shrubs * 1.15f);
                    break;
                case PrismBiome.Glowdeep:
                    trees = Mathf.Min(1f, trees * 1.1f + 0.05f);
                    shrubs = Mathf.Min(1f, shrubs * 1.25f);
                    break;
            }
        }

        /// <summary>The sun (or moon): elevation range, colour mix toward white, and intensity.</summary>
        public static void Light(PrismBiome biome, PrismPalette pal, ref ZoneRandom look, out float elev, out Color color, out float intensity)
        {
            switch (biome)
            {
                case PrismBiome.Wonderwood:
                    // A low sun throwing long shadows through the giants.
                    elev = look.Range(9f, 18f);
                    color = Color.Lerp(pal.Sun, Color.white, 0.2f);
                    intensity = 1.2f;
                    break;
                case PrismBiome.Glowdeep:
                    // A cool moon: enough light to read the ground; the plants do the rest.
                    elev = look.Range(35f, 60f);
                    color = Color.Lerp(pal.Sun, new Color(0.7f, 0.85f, 1f), 0.4f);
                    intensity = 0.95f;
                    break;
                default:
                    elev = pal.Dusk ? look.Range(16f, 30f) : look.Range(30f, 52f);
                    color = pal.Dusk ? Color.Lerp(pal.Sun, Color.white, 0.35f) : pal.Sun;
                    intensity = pal.Dusk ? 1.15f : 1.35f;
                    break;
            }
        }
    }
}
