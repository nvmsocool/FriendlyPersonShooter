using FriendlyPersonShooter.Core.Expedition;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>
    /// A zone's colors. Built from hand-tuned moods, hue-rotated and remixed per seed, nudged by tile
    /// kind and the zone's essence lean, so nine zones read as nine places.
    /// </summary>
    public sealed class PrismPalette
    {
        public string Mood;
        /// <summary>The layer's biome (see <see cref="PrismBiome"/>).</summary>
        public PrismBiome Biome;
        public Color[] Grass;      // ground tones on flats (patchwork)
        public Color[] Strata;     // cliff bands
        public Color[] HexTop;     // basalt column tops
        public Color Path;
        public Color Plaza;
        public Color LakeBed;
        public Color Water;
        public Color WaterDeep;
        public Color[] Foliage;    // tree and shrub bodies
        public Color[] Flowers;    // bright accents
        public Color[] Bark;
        public Color[] Crystal;
        public Color[] Stone;      // monoliths, slabs, structures
        public Color Glow;         // rune / accent glow
        public Color SkyZenith, SkyMid, SkyHorizon, Fog;
        public Color Sun, CloudLit, CloudShade, CloudRim;
        public Color BankBack, BankMid, BankFront;
        public Color ShadowTint, Rim;
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public Color PlanetA, PlanetB, PlanetC, Ring;
        public Color[] Moons;
        public Color AuroraA, AuroraB;
        public bool Dusk;

        private sealed class MoodDef
        {
            public string Name;
            public string[] Grass, Strata, HexTop, Foliage, Flowers, Bark, Crystal, Stone;
            public string Path, Water, Zenith, Mid, Horizon, Sun, CloudLit, CloudShade, Shadow;
            public bool Dusk;
        }

        private static readonly MoodDef[] Moods =
        {
            new MoodDef
            {
                Name = "candy", Grass = new[] { "#7ED957", "#A8E05F", "#5CCB7A", "#C4E86B" },
                Strata = new[] { "#F2856D", "#F7B267", "#C77DFF", "#FFD6A5", "#F4A6C1", "#9D7BF0" },
                HexTop = new[] { "#F4A6C1", "#FFC9A8", "#C9A7FF" }, Foliage = new[] { "#2FBF71", "#FF7AA2", "#FFC145", "#4FD1C5", "#B388FF", "#8BE04E" },
                Flowers = new[] { "#FF3D7F", "#FFE14D", "#FFFFFF", "#5CC8FF", "#FF8A3D", "#D25CFF" }, Bark = new[] { "#8A4F7D", "#6B4E9B", "#B5655A" },
                Crystal = new[] { "#7AF0FF", "#D08CFF" }, Stone = new[] { "#F8E9D8", "#E7C6FF", "#FFD1DC" }, Path = "#FFE3B8", Water = "#49D8E8",
                Zenith = "#2E62D9", Mid = "#79AFFF", Horizon = "#FFD3E8", Sun = "#FFF1D0", CloudLit = "#FFFFFF", CloudShade = "#F0A9D6", Shadow = "#5B3FA8",
            },
            new MoodDef
            {
                Name = "sunset", Grass = new[] { "#62C99A", "#8FD36A", "#F2A65A", "#F5D25A" },
                Strata = new[] { "#E8505B", "#F5A65B", "#7B4B94", "#FCD5A6", "#F9844A", "#B5446E" },
                HexTop = new[] { "#F9C784", "#E8735A", "#C76B98" }, Foliage = new[] { "#43AA8B", "#FF6F59", "#F9C74F", "#2A9D8F", "#F3722C", "#90BE6D" },
                Flowers = new[] { "#FF2E63", "#FFD23F", "#FFFFFF", "#3BCEAC", "#EE4266", "#9B5DE5" }, Bark = new[] { "#5E2B4F", "#7A3B3B", "#4B3869" },
                Crystal = new[] { "#FF9BD2", "#FFD36E" }, Stone = new[] { "#FCE1C0", "#F2B8A2", "#D9A5E0" }, Path = "#FBD9A7", Water = "#3FC1C9",
                Zenith = "#3B2A7B", Mid = "#C9579A", Horizon = "#FFB36B", Sun = "#FFD9A0", CloudLit = "#FFD7A8", CloudShade = "#A8558F", Shadow = "#6A2C70", Dusk = true,
            },
            new MoodDef
            {
                Name = "lagoon", Grass = new[] { "#3DDC97", "#7EF29D", "#22C1A3", "#A6F07A" },
                Strata = new[] { "#2EC4B6", "#7FD6C8", "#FF9F1C", "#FFBF69", "#3A86FF", "#F7EDE2" },
                HexTop = new[] { "#CBF3F0", "#FFBF69", "#8EE3EF" }, Foliage = new[] { "#00A68C", "#FF5D8F", "#FFD60A", "#7CDF64", "#0FB5D6", "#F15BB5" },
                Flowers = new[] { "#FF4D6D", "#FFE66D", "#FFFFFF", "#9B5DE5", "#FF9F1C", "#00F5D4" }, Bark = new[] { "#355070", "#6D597A", "#2B6777" },
                Crystal = new[] { "#9BFFF7", "#FF9DE2" }, Stone = new[] { "#F7EDE2", "#D8F3DC", "#BDE0FE" }, Path = "#FFF1C9", Water = "#1EC8D8",
                Zenith = "#0B6E99", Mid = "#4CC9F0", Horizon = "#E9FFF7", Sun = "#FFFBE6", CloudLit = "#FFFFFF", CloudShade = "#8FD3E8", Shadow = "#1D4E89",
            },
            new MoodDef
            {
                Name = "lavender", Grass = new[] { "#B79CED", "#9EE493", "#D3B5F7", "#7FD8BE" },
                Strata = new[] { "#6D5BD0", "#F78FB3", "#F8C291", "#E0E7FF", "#8E7DEE", "#FFB4A2" },
                HexTop = new[] { "#E0C3FC", "#FFC8DD", "#BDE0FE" }, Foliage = new[] { "#FF9CEE", "#7DE2D1", "#FFCA3A", "#8AC926", "#6A4C93", "#FF595E" },
                Flowers = new[] { "#FFFFFF", "#FFD6FF", "#FFEE93", "#FF7096", "#70D6FF", "#E9FF70" }, Bark = new[] { "#4A3F75", "#7B5E7B", "#3D3B8E" },
                Crystal = new[] { "#B8F2FF", "#FFB8F5" }, Stone = new[] { "#F1E4FF", "#FFE5EC", "#E2ECE9" }, Path = "#FFEFD8", Water = "#7FD6F5",
                Zenith = "#5E60CE", Mid = "#9D8DF1", Horizon = "#FFC8DD", Sun = "#FFF0F5", CloudLit = "#FFF4FA", CloudShade = "#B8A1E3", Shadow = "#4B3B9A",
            },
            new MoodDef
            {
                Name = "citrus", Grass = new[] { "#7CCB4A", "#4FC779", "#D6E33F", "#2EC4B6" },
                Strata = new[] { "#FF6B35", "#F7C59F", "#F4D35E", "#2EC4B6", "#FFB627", "#E84855" },
                HexTop = new[] { "#FFE19C", "#FF9F68", "#9FE2BF" }, Foliage = new[] { "#1AAE9F", "#FF006E", "#FFBE0B", "#3A86FF", "#8338EC", "#06D6A0" },
                Flowers = new[] { "#FF006E", "#FFBE0B", "#FFFFFF", "#3A86FF", "#FB5607", "#8338EC" }, Bark = new[] { "#5C3D2E", "#3D405B", "#7F4F24" },
                Crystal = new[] { "#FFF07C", "#7CF5FF" }, Stone = new[] { "#FFF8E7", "#FFE0B5", "#D4F1F4" }, Path = "#FFE8B0", Water = "#2EC8F0",
                Zenith = "#1572D3", Mid = "#7FC8F8", Horizon = "#FFF6C2", Sun = "#FFFFFF", CloudLit = "#FFFFFF", CloudShade = "#A9D6F5", Shadow = "#2D3A8C",
            },
            new MoodDef
            {
                Name = "neon", Grass = new[] { "#2FD6C2", "#E05AB8", "#4FC3F7", "#7B6CF6" },
                Strata = new[] { "#3A0CA3", "#7209B7", "#4CC9F0", "#F72585", "#560BAD", "#4895EF" },
                HexTop = new[] { "#4CC9F0", "#B5179E", "#7B2CBF" }, Foliage = new[] { "#4CC9F0", "#B5179E", "#80FFDB", "#FEE440", "#00BBF9", "#9B5DE5" },
                Flowers = new[] { "#FEE440", "#00F5D4", "#FFFFFF", "#F15BB5", "#00BBF9", "#FF9E00" }, Bark = new[] { "#240046", "#3C096C", "#10002B" },
                Crystal = new[] { "#80FFDB", "#FF66E0" }, Stone = new[] { "#CDB4DB", "#A2D2FF", "#FFAFCC" }, Path = "#FFC6FF", Water = "#5DE2FF",
                Zenith = "#14003D", Mid = "#4A148C", Horizon = "#FF7AC6", Sun = "#FFD1F0", CloudLit = "#FFB3E6", CloudShade = "#6A1B9A", Shadow = "#3A0CA3", Dusk = true,
            },
            new MoodDef
            {
                Name = "autumn", Grass = new[] { "#8FD694", "#C5E17A", "#62C370", "#F2D16B" },
                Strata = new[] { "#D96C06", "#F2A541", "#5B3758", "#F4E9CD", "#C8553D", "#7EA16B" },
                HexTop = new[] { "#F4E9CD", "#F2A541", "#E07A5F" }, Foliage = new[] { "#E2711D", "#CC2936", "#FFB703", "#2A9D8F", "#F4845F", "#8AB17D" },
                Flowers = new[] { "#FFFFFF", "#FFD000", "#FF4365", "#3DCCC7", "#F15152", "#C77DFF" }, Bark = new[] { "#4A2C2A", "#5B3758", "#3B2F2F" },
                Crystal = new[] { "#FFE29A", "#9BF6FF" }, Stone = new[] { "#F4E9CD", "#E9D8A6", "#F6BD60" }, Path = "#F6E7C1", Water = "#38B2AC",
                Zenith = "#1F5F8B", Mid = "#56B4D3", Horizon = "#FBE7C6", Sun = "#FFE8B8", CloudLit = "#FFF9F0", CloudShade = "#E3B5A4", Shadow = "#4A3B78",
            },
            new MoodDef
            {
                Name = "aurora", Grass = new[] { "#5EF2D3", "#B8F7A0", "#43D9E8", "#9AF0C6" },
                Strata = new[] { "#577590", "#A0C4FF", "#BDB2FF", "#FFFFFC", "#4D908E", "#FFADAD" },
                HexTop = new[] { "#A0C4FF", "#BDB2FF", "#CAFFBF" }, Foliage = new[] { "#FFADAD", "#FDFFB6", "#43AA8B", "#9BF6FF", "#FFC6FF", "#52B788" },
                Flowers = new[] { "#FFFFFF", "#FDFFB6", "#FF8FAB", "#9BF6FF", "#FFD670", "#C77DFF" }, Bark = new[] { "#2B2D42", "#3D405B", "#22333B" },
                Crystal = new[] { "#9BF6FF", "#FFC6FF" }, Stone = new[] { "#E9F5FF", "#DDE5FF", "#F3E8FF" }, Path = "#EAF4FF", Water = "#64DFDF",
                Zenith = "#0B1D51", Mid = "#2E4A9E", Horizon = "#8FE3CF", Sun = "#E8FFF8", CloudLit = "#D9F7FF", CloudShade = "#3E5C9A", Shadow = "#2C3E8F", Dusk = true,
            },
        };

        /// <summary>Wonderwood (layer 2): dusk over a giant forest. Olive and amber moss, plum bark, magenta and orange skies.</summary>
        private static readonly MoodDef[] WonderMoods =
        {
            new MoodDef
            {
                Name = "ember", Grass = new[] { "#7A9A3A", "#A88A34", "#5E8A42", "#C99A48" },
                Strata = new[] { "#7A3466", "#B44E74", "#E07A4F", "#F4B860", "#5A3670", "#C9637E" },
                HexTop = new[] { "#E8A36B", "#B5527A", "#7E4C8C" }, Foliage = new[] { "#FF8A3D", "#E8457A", "#3FB8AF", "#FFC145", "#B455D6", "#5FC46B" },
                Flowers = new[] { "#FFD23F", "#FF4F8B", "#5CE1E6", "#FFFFFF", "#FF7A30", "#C77DFF" }, Bark = new[] { "#4A2A4E", "#5A2E3E", "#3A2C55" },
                Crystal = new[] { "#FFB0E0", "#7CF5FF" }, Stone = new[] { "#E9C9B1", "#D9A1B8", "#B9A3D9" }, Path = "#D9A66B", Water = "#2FB5C9",
                Zenith = "#2A1650", Mid = "#B8358A", Horizon = "#FF9A4D", Sun = "#FFD08A", CloudLit = "#FFC38F", CloudShade = "#8A3F86", Shadow = "#4A1F5C", Dusk = true,
            },
            new MoodDef
            {
                Name = "rosewood", Grass = new[] { "#6FA34A", "#A3B347", "#4F8F5A", "#D9A441" },
                Strata = new[] { "#7D2E68", "#D35D8C", "#F59E7A", "#FFD3A5", "#5A3D7A", "#E86A6A" },
                HexTop = new[] { "#F2B6A0", "#D35D8C", "#8E6BBF" }, Foliage = new[] { "#FF6FA8", "#FFB347", "#2EC4B6", "#9B5DE5", "#FF5E5B", "#7BD389" },
                Flowers = new[] { "#FFE66D", "#FF3D7F", "#7DF9FF", "#FFFFFF", "#FF9F1C", "#E0AAFF" }, Bark = new[] { "#4E2A4C", "#62304A", "#38345A" },
                Crystal = new[] { "#FFC4E8", "#A0FFF0" }, Stone = new[] { "#F3D5C0", "#E6B3C8", "#C7B8EA" }, Path = "#E4B07A", Water = "#3BC4D0",
                Zenith = "#3A1C6B", Mid = "#D1478F", Horizon = "#FFB070", Sun = "#FFE0A8", CloudLit = "#FFD0B0", CloudShade = "#9A4A92", Shadow = "#5A2470", Dusk = true,
            },
            new MoodDef
            {
                Name = "violet hour", Grass = new[] { "#4E8C6A", "#7A9E4E", "#3E7A7A", "#B07ACF" },
                Strata = new[] { "#4A3478", "#6E4AA8", "#C266B8", "#F2A0C0", "#38548A", "#FF9E80" },
                HexTop = new[] { "#C9A0E8", "#F2A0C0", "#7FB8E0" }, Foliage = new[] { "#C266FF", "#3FD0C9", "#FF8FC8", "#FFD166", "#6EE7A0", "#FF7B54" },
                Flowers = new[] { "#FFEE88", "#FF5FA2", "#88F7FF", "#FFFFFF", "#FFB347", "#B98CFF" }, Bark = new[] { "#3A2A56", "#4A2E62", "#2A3656" },
                Crystal = new[] { "#D6B4FF", "#8CF5FF" }, Stone = new[] { "#E8D8F5", "#D8C0E8", "#B8D8F0" }, Path = "#C8A8D8", Water = "#4CB8E0",
                Zenith = "#1E1450", Mid = "#7A3FB0", Horizon = "#FF8FB0", Sun = "#FFD6E8", CloudLit = "#FFC0E0", CloudShade = "#5E3A9A", Shadow = "#2E1A60", Dusk = true,
            },
            new MoodDef
            {
                Name = "amber", Grass = new[] { "#8FA03A", "#B8923A", "#6E8E3E", "#E0B04A" },
                Strata = new[] { "#8A3B2E", "#D9683A", "#F2B15A", "#FFE0A8", "#7A345A", "#B8504A" },
                HexTop = new[] { "#F2C47A", "#D9683A", "#A0506E" }, Foliage = new[] { "#FF9F1C", "#E84A5F", "#2BB3A3", "#FFD23F", "#8E5BE0", "#9BD35A" },
                Flowers = new[] { "#FFE45E", "#FF3366", "#66E0FF", "#FFFFFF", "#FF8C42", "#D4A5FF" }, Bark = new[] { "#4E2C2C", "#5E3232", "#40304E" },
                Crystal = new[] { "#FFD08A", "#9CF0FF" }, Stone = new[] { "#F5DEC0", "#E8C0A0", "#D8B8C8" }, Path = "#E8B87A", Water = "#36B0BF",
                Zenith = "#3A1A55", Mid = "#C8456E", Horizon = "#FFB45A", Sun = "#FFE2A0", CloudLit = "#FFD4A0", CloudShade = "#A0466E", Shadow = "#5A2A4A", Dusk = true,
            },
        };

        /// <summary>Glowdeep (layer 3): night. Dark basalt and moss, neon plants and crystals, deep blue skies.</summary>
        private static readonly MoodDef[] GlowMoods =
        {
            new MoodDef
            {
                Name = "abyss", Grass = new[] { "#24506A", "#2C5C70", "#38407A", "#1C6A5E" },
                Strata = new[] { "#20264A", "#33306A", "#46347A", "#24466E", "#56286A", "#184A5A" },
                HexTop = new[] { "#34487E", "#46387E", "#285E6E" }, Foliage = new[] { "#3CF2D2", "#FF4FD8", "#6A8CFF", "#B8FF4F", "#FF9B3D", "#9B5CFF" },
                Flowers = new[] { "#7DFFEA", "#FF7AF0", "#FFF27A", "#FFFFFF", "#7AB8FF", "#C6FF7A" }, Bark = new[] { "#1C2440", "#281C40", "#142E36" },
                Crystal = new[] { "#5CFFF0", "#FF5CF2" }, Stone = new[] { "#46528A", "#584690", "#365E74" }, Path = "#4A5C90", Water = "#1FA0C8",
                Zenith = "#03051A", Mid = "#0E1A4A", Horizon = "#2A4F8A", Sun = "#BFE8FF", CloudLit = "#4A6AA8", CloudShade = "#141E44", Shadow = "#0A0E30", Dusk = true,
            },
            new MoodDef
            {
                Name = "ember deep", Grass = new[] { "#3E3460", "#4E3050", "#2A4460", "#5A3A44" },
                Strata = new[] { "#2C1A3A", "#4A2046", "#66244A", "#34285A", "#7A3432", "#20344A" },
                HexTop = new[] { "#5A2C48", "#463670", "#6A3A3A" }, Foliage = new[] { "#FF6A3D", "#FFD03D", "#FF3D8B", "#3DFFB8", "#FF9F5A", "#C45CFF" },
                Flowers = new[] { "#FFE07A", "#FF7AA8", "#7AFFD8", "#FFFFFF", "#FFB07A", "#E07AFF" }, Bark = new[] { "#24162C", "#301824", "#1A2430" },
                Crystal = new[] { "#FF8A5C", "#FFD45C" }, Stone = new[] { "#5A3C64", "#6A4650", "#46466E" }, Path = "#6A4658", Water = "#3AA0B0",
                Zenith = "#0A0412", Mid = "#2A0E2E", Horizon = "#7A2A3A", Sun = "#FFC8A0", CloudLit = "#7A3A5A", CloudShade = "#200E28", Shadow = "#14081E", Dusk = true,
            },
            new MoodDef
            {
                Name = "mycelium", Grass = new[] { "#245A4C", "#30684E", "#2C406A", "#46623A" },
                Strata = new[] { "#1E3434", "#2A4458", "#363254", "#225650", "#46462E", "#2C2838" },
                HexTop = new[] { "#30585A", "#44446A", "#306A56" }, Foliage = new[] { "#9CFF6A", "#4FE8FF", "#FFE45C", "#FF6AD8", "#6AFFC4", "#A07AFF" },
                Flowers = new[] { "#E8FF7A", "#7AFFF0", "#FF9AEA", "#FFFFFF", "#FFD27A", "#B8A0FF" }, Bark = new[] { "#162626", "#20203A", "#12241C" },
                Crystal = new[] { "#9CFF6A", "#4FE8FF" }, Stone = new[] { "#466262", "#4C4C6C", "#3A5A4E" }, Path = "#466C60", Water = "#2AC8B0",
                Zenith = "#020A0E", Mid = "#0A2230", Horizon = "#1E5A5A", Sun = "#D8FFE8", CloudLit = "#3A6A6A", CloudShade = "#0E2028", Shadow = "#061418", Dusk = true,
            },
        };

        public static PrismPalette Create(ZoneRequest zone)
        {
            var rng = ZoneRandom.For(zone.Seed, "palette");
            var biome = PrismBiomes.For(zone);
            var moods = biome == PrismBiome.Wonderwood ? WonderMoods : biome == PrismBiome.Glowdeep ? GlowMoods : Moods;
            // Tile kinds lean toward some moods but any mood can appear.
            var w = new float[moods.Length];
            for (int i = 0; i < w.Length; i++) w[i] = 1f;
            void Lean(string name, float add)
            {
                for (int i = 0; i < moods.Length; i++) if (moods[i].Name == name) w[i] += add;
            }
            switch (zone.Tile)
            {
                case TileKind.Meadow: Lean("candy", 2f); Lean("lavender", 1.5f); Lean("citrus", 1f); break;
                case TileKind.Forest: Lean("autumn", 2f); Lean("lagoon", 1f); Lean("citrus", 1f); break;
                case TileKind.Canyon: Lean("sunset", 2.5f); Lean("autumn", 1f); break;
                case TileKind.Lake: Lean("lagoon", 2f); Lean("aurora", 1.5f); break;
                case TileKind.River: Lean("lagoon", 1.5f); Lean("candy", 1f); Lean("citrus", 1f); break;
                case TileKind.Crater: Lean("neon", 2.5f); Lean("aurora", 1f); break;
                case TileKind.Ruins: Lean("lavender", 1.5f); Lean("sunset", 1f); Lean("autumn", 1f); break;
                case TileKind.Tower: Lean("citrus", 1f); Lean("neon", 1f); Lean("sunset", 1f); break;
            }
            var m = moods[rng.Weighted(w)];
            // A second mood donates some foliage and flowers, so the same mood never repeats exactly.
            var donor = moods[rng.Range(0, moods.Length)];
            float hue = rng.Range(-0.045f, 0.045f);

            Color H(string hex) => Shift(ZoneLook.Hex(hex), hue);
            Color[] HA(string[] hexes) => System.Array.ConvertAll(hexes, H);

            var p = new PrismPalette { Mood = m.Name + "+" + donor.Name, Dusk = m.Dusk, Biome = biome };
            var grass = HA(m.Grass);
            // The first two entries of each mood are ground tones, the last two are accents.
            if (rng.Chance(0.4f)) (grass[0], grass[1]) = (grass[1], grass[0]);
            if (rng.Chance(0.5f)) (grass[2], grass[3]) = (grass[3], grass[2]);
            // A dominant ground tone, a close sibling for soft patches, then accents.
            p.Grass = new[] { grass[0], Shift(grass[0], rng.Range(-0.05f, 0.05f), rng.Range(0.95f, 1.1f), rng.Range(1.04f, 1.14f)), grass[1], grass[2] };
            p.Strata = HA(m.Strata);
            Shuffle(p.Strata, ref rng);
            for (int i = 0; i < p.Strata.Length; i++)
            {
                Color.RGBToHSV(p.Strata[i], out float sh, out float ss, out float sv);
                // Glowdeep keeps its rock dark (the plants and crystals bring the light); the others stay bright.
                p.Strata[i] = biome == PrismBiome.Glowdeep
                    ? Color.HSVToRGB(sh, Mathf.Min(ss, 0.7f), Mathf.Clamp(sv, 0.26f, 0.52f))
                    : Color.HSVToRGB(sh, Mathf.Min(ss, i == 0 ? 0.85f : 0.68f), Mathf.Max(sv, i == 0 ? 0.55f : 0.72f));
            }
            p.HexTop = HA(m.HexTop);
            p.Foliage = Mix(HA(m.Foliage), HA(donor.Foliage), 2, ref rng);
            p.Flowers = Mix(HA(m.Flowers), HA(donor.Flowers), 2, ref rng);
            p.Bark = HA(m.Bark);
            p.Crystal = HA(m.Crystal);
            p.Stone = HA(m.Stone);
            p.Path = H(m.Path);
            p.Plaza = Color.Lerp(H(m.Path), p.Stone[0], 0.5f);
            p.Water = H(m.Water);
            p.WaterDeep = Shift(H(m.Water), 0.03f, 1.25f, 0.72f);
            p.LakeBed = Color.Lerp(H(m.Path), H(m.Water), 0.45f);
            p.SkyZenith = H(m.Zenith);
            p.SkyMid = H(m.Mid);
            p.SkyHorizon = H(m.Horizon);
            p.Sun = H(m.Sun);
            p.CloudLit = H(m.CloudLit);
            p.CloudShade = H(m.CloudShade);
            p.CloudRim = Color.Lerp(p.Sun, Color.white, 0.3f);
            p.ShadowTint = H(m.Shadow) * 0.55f;
            p.Rim = Color.Lerp(p.SkyHorizon, Color.white, 0.3f);
            p.Fog = Color.Lerp(Color.Lerp(p.SkyHorizon, p.SkyMid, 0.2f), Color.white, 0.25f);
            p.BankBack = Color.Lerp(p.SkyHorizon, p.CloudLit, 0.55f);
            p.BankMid = Color.Lerp(p.CloudShade, p.SkyHorizon, 0.45f);
            p.BankFront = Color.Lerp(p.CloudShade, p.SkyMid, 0.35f);
            p.AmbientSky = Color.Lerp(p.SkyMid, Color.white, m.Dusk ? 0.45f : 0.25f) * (m.Dusk ? 0.95f : 0.75f);
            p.AmbientEquator = Color.Lerp(p.SkyHorizon, Color.white, 0.1f) * 0.7f;
            p.AmbientGround = Color.Lerp(p.Grass[0], p.Strata[0], 0.5f) * 0.45f;

            // Essence lean colors crystals and the glow accent.
            Color lean = LeanColor(zone.Lean);
            p.Crystal[0] = Color.Lerp(p.Crystal[0], lean, 0.55f);
            p.Glow = Color.Lerp(lean, Color.white, 0.25f);

            var sky = ZoneRandom.For(zone.Seed, "planet");
            p.PlanetA = Shift(p.Strata[0], sky.Range(-0.1f, 0.1f));
            p.PlanetB = Shift(p.Strata[1], sky.Range(-0.1f, 0.1f));
            p.PlanetC = Color.Lerp(p.Stone[0], Color.white, 0.4f);
            p.Ring = Color.Lerp(p.CloudLit, p.Strata[2], 0.35f);
            p.Moons = new[] { Color.Lerp(p.Stone[0], Color.white, 0.5f), Color.Lerp(p.Crystal[1], Color.white, 0.4f), Color.Lerp(p.Flowers[1], Color.white, 0.5f) };
            p.AuroraA = Color.Lerp(p.Crystal[0], new Color(0.3f, 1f, 0.7f), 0.5f);
            p.AuroraB = Color.Lerp(p.Crystal[1], new Color(1f, 0.3f, 0.9f), 0.5f);
            if (biome == PrismBiome.Glowdeep)
            {
                // Night: no washed-out haze. Deep blue fog, cool ambient light bright enough to read the ground by,
                // and neon rims from the crystals.
                p.Fog = Color.Lerp(p.SkyHorizon, p.SkyMid, 0.35f);
                p.AmbientSky = Color.Lerp(p.SkyHorizon, Color.white, 0.3f) * 0.95f;
                p.AmbientEquator = Color.Lerp(p.SkyHorizon, p.Crystal[0], 0.3f) * 0.75f;
                p.AmbientGround = Color.Lerp(p.Grass[0], p.Foliage[0], 0.3f) * 0.6f;
                p.Rim = Color.Lerp(p.Crystal[0], p.SkyHorizon, 0.4f);
                p.BankBack = Color.Lerp(p.SkyHorizon, p.CloudLit, 0.5f);
            }
            else if (biome == PrismBiome.Wonderwood)
            {
                // A warm sunset haze between the giants.
                p.Fog = Color.Lerp(Color.Lerp(p.SkyHorizon, p.SkyMid, 0.3f), Color.white, 0.12f);
            }
            return p;
        }

        public static Color LeanColor(EssenceColor c) => c switch
        {
            EssenceColor.Yellow => new Color(1f, 0.85f, 0.25f),
            EssenceColor.Red => new Color(1f, 0.3f, 0.35f),
            EssenceColor.Green => new Color(0.35f, 1f, 0.5f),
            EssenceColor.Blue => new Color(0.35f, 0.6f, 1f),
            EssenceColor.Purple => new Color(0.75f, 0.4f, 1f),
            _ => new Color(1f, 0.6f, 0.25f),
        };

        public static Color Shift(Color c, float hue, float sat = 1f, float val = 1f)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var o = Color.HSVToRGB(Mathf.Repeat(h + hue, 1f), Mathf.Clamp01(s * sat), Mathf.Clamp01(v * val));
            o.a = c.a;
            return o;
        }

        private static Color[] Mix(Color[] own, Color[] donor, int take, ref ZoneRandom rng)
        {
            var list = new System.Collections.Generic.List<Color>(own);
            for (int i = 0; i < take; i++) list[rng.Range(0, list.Count)] = donor[rng.Range(0, donor.Length)];
            var arr = list.ToArray();
            Shuffle(arr, ref rng);
            return arr;
        }

        private static void Shuffle(Color[] a, ref ZoneRandom rng)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
    }
}
