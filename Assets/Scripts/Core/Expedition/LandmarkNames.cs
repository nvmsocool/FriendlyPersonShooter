using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// Builds landmark names from word lists: an adjective plus a noun that suits the zone's tile.
    /// Singular nouns get "the" ("the Broken Tower"); plural ones don't ("Twin Falls"). Names are
    /// unique within a layer.
    /// </summary>
    internal static class LandmarkNames
    {
        private static readonly string[] Adjectives =
        {
            "Broken", "Twin", "Old", "Silent", "Leaning", "Sunken", "Burnt", "Crooked", "Whispering",
            "Lonely", "Iron", "Golden", "Mossy", "Weeping", "Shattered", "Black", "White", "High",
            "Fallen", "Singing", "Crystal", "Grey", "Hidden", "Frozen", "Red", "Long", "Bright", "Hanging",
        };

        private readonly struct Noun
        {
            public readonly string Word;
            public readonly bool Plural;

            public Noun(string word, bool plural = false)
            {
                Word = word;
                Plural = plural;
            }
        }

        // Indexed by (int)TileKind.
        private static readonly Noun[][] NounsByTile =
        {
            /* Meadow */ new[] { new Noun("Stones", true), new Noun("Oak"), new Noun("Well"), new Noun("Barrow"), new Noun("Fields", true) },
            /* Ruins  */ new[] { new Noun("Arch"), new Noun("Columns", true), new Noun("Hall"), new Noun("Steps", true), new Noun("Statue") },
            /* River  */ new[] { new Noun("Falls", true), new Noun("Ford"), new Noun("Bridge"), new Noun("Bend"), new Noun("Mill") },
            /* Tower  */ new[] { new Noun("Tower"), new Noun("Spire"), new Noun("Beacon"), new Noun("Watchtower"), new Noun("Belfry") },
            /* Canyon */ new[] { new Noun("Gorge"), new Noun("Ledge"), new Noun("Pass"), new Noun("Cliffs", true), new Noun("Gap") },
            /* Forest */ new[] { new Noun("Grove"), new Noun("Hollow"), new Noun("Stump"), new Noun("Thicket"), new Noun("Pines", true) },
            /* Lake   */ new[] { new Noun("Pool"), new Noun("Pier"), new Noun("Isle"), new Noun("Shallows", true), new Noun("Boathouse") },
            /* Crater */ new[] { new Noun("Pit"), new Noun("Rim"), new Noun("Shard"), new Noun("Crater"), new Noun("Vents", true) },
        };

        private const int TriesPerPool = 30;

        /// <summary>A new name not in <paramref name="used"/>; adds it to <paramref name="used"/>.</summary>
        public static string Make(TileKind tile, ref CombatRandom random, HashSet<string> used)
        {
            var tileNouns = NounsByTile[(int)tile % NounsByTile.Length];
            for (int i = 0; i < TriesPerPool; i++)
            {
                string name = Build(Adjectives[random.NextInt(Adjectives.Length)], tileNouns[random.NextInt(tileNouns.Length)]);
                if (used.Add(name)) return name;
            }
            // The tile's own words are used up: borrow from any tile.
            for (int i = 0; i < TriesPerPool; i++)
            {
                var nouns = NounsByTile[random.NextInt(NounsByTile.Length)];
                string name = Build(Adjectives[random.NextInt(Adjectives.Length)], nouns[random.NextInt(nouns.Length)]);
                if (used.Add(name)) return name;
            }
            // Very large layers: number a repeat ("the Old Mill II").
            string baseName = Build(Adjectives[random.NextInt(Adjectives.Length)], tileNouns[random.NextInt(tileNouns.Length)]);
            for (int n = 2; ; n++)
            {
                string name = baseName + " " + Numeral(n);
                if (used.Add(name)) return name;
            }
        }

        private static string Build(string adjective, Noun noun)
        {
            return noun.Plural ? $"{adjective} {noun.Word}" : $"the {adjective} {noun.Word}";
        }

        private static string Numeral(int n)
        {
            switch (n)
            {
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                case 5: return "V";
                default: return n.ToString();
            }
        }
    }
}
