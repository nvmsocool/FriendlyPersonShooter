using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Avatars
{
    public enum BugAntennae : byte
    {
        None,
        /// <summary>Long thin whips.</summary>
        Whips,
        /// <summary>Short ones ending in a ball.</summary>
        Clubs,
        /// <summary>Bent at the tip, like a cockroach's or an ant's.</summary>
        Hooks,
        /// <summary>Feathery (moth-like): a stalk with little blades.</summary>
        Feathers,
    }

    public enum BugWings : byte
    {
        /// <summary>Rounded (moth, beetle).</summary>
        Round,
        /// <summary>Long and narrow (dragonfly, wasp).</summary>
        Long,
        /// <summary>Pointed, swept back.</summary>
        Swept,
    }

    /// <summary>
    /// A robotic insect's recipe: one per enemy, from its family and a seed, so no two enemies look exactly alike and
    /// everyone sees the same one. Sizes are multipliers around the family's usual shape; colours are a hue shift and a
    /// lightness on the family's essence colour (so the colour still says what it drops). The view turns it into a mesh.
    /// Each family has its own kind of insect: Skitters are beetles, Mites gnats, Wisps fireflies, Divers dragonflies or
    /// wasps, Sentinels mantises, roaming Bulwarks armoured pill bugs and Glimmers butterflies.
    /// </summary>
    public readonly struct BugLook
    {
        public readonly EnemyFamily Family;
        /// <summary>Pairs of walking legs (a mantis's raptorial arms are extra).</summary>
        public readonly int LegPairs;
        /// <summary>Leg length and thickness multipliers.</summary>
        public readonly float LegLength, LegThickness;
        /// <summary>Abdomen segments, and how long and wide the body is (multipliers).</summary>
        public readonly int Segments;
        public readonly float Length, Bulk, Hump;
        public readonly BugAntennae Antennae;
        public readonly float AntennaLength;
        /// <summary>Mandible size, 0 = none.</summary>
        public readonly float Mandibles;
        /// <summary>A horn on the head (or a stinger on a wasp), 0 = none.</summary>
        public readonly float Horn;
        /// <summary>Eye count (2, 4 or 6) and size.</summary>
        public readonly int Eyes;
        public readonly float EyeSize;
        /// <summary>Pairs of wings (0..2), their shape and length.</summary>
        public readonly int WingPairs;
        public readonly BugWings WingShape;
        public readonly float WingLength;
        /// <summary>Accent bands across the body.</summary>
        public readonly int Stripes;
        /// <summary>Hue shift (-0.015..0.015) and lightness (0.75..1.05) on the family colour.</summary>
        public readonly float HueShift, Lightness;
        /// <summary>Hue of the metal parts' tint, and how light they are.</summary>
        public readonly float MetalHue, MetalLight;
        /// <summary>Leg (or wing) beat speed multiplier and a phase, so a pack doesn't move in step.</summary>
        public readonly float GaitSpeed, Phase;

        public BugLook(EnemyFamily family, uint seed)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            Family = family;
            // Shared traits first, so every family varies the same way.
            HueShift = rng.Range(-0.015f, 0.015f);
            Lightness = rng.Range(0.75f, 1.05f);
            MetalHue = rng.NextFloat();
            MetalLight = rng.Range(0.12f, 0.32f);
            GaitSpeed = rng.Range(0.8f, 1.25f);
            Phase = rng.Range(0f, 6.2831853f);
            Stripes = rng.NextInt(4);
            LegThickness = rng.Range(0.8f, 1.25f);
            EyeSize = rng.Range(0.8f, 1.35f);
            Eyes = rng.Chance(0.6f) ? 2 : rng.Chance(0.6f) ? 4 : 6;

            switch (family)
            {
                case EnemyFamily.Skitter:
                    // A beetle or a tick: low and wide, six or eight legs, often mandibles, sometimes a horn.
                    LegPairs = rng.Chance(0.7f) ? 3 : 4;
                    LegLength = rng.Range(0.8f, 1.3f);
                    Segments = 1 + rng.NextInt(3);
                    Length = rng.Range(0.85f, 1.2f);
                    Bulk = rng.Range(0.85f, 1.2f);
                    Hump = rng.Range(0.7f, 1.25f);
                    Antennae = (BugAntennae)rng.NextInt(4);
                    AntennaLength = rng.Range(0.6f, 1.3f);
                    Mandibles = rng.Chance(0.8f) ? rng.Range(0.4f, 1f) : 0f;
                    Horn = rng.Chance(0.25f) ? rng.Range(0.4f, 1f) : 0f;
                    WingPairs = 0;
                    WingShape = BugWings.Round;
                    WingLength = 0f;
                    break;
                case EnemyFamily.Wisp:
                    // A firefly or a moth: a glowing lantern abdomen, one or two pairs of wings, dangling legs.
                    LegPairs = rng.NextInt(4);
                    LegLength = rng.Range(0.5f, 0.9f);
                    Segments = 2 + rng.NextInt(3);
                    Length = rng.Range(0.85f, 1.15f);
                    Bulk = rng.Range(0.85f, 1.15f);
                    Hump = rng.Range(0.85f, 1.15f);
                    Antennae = rng.Chance(0.5f) ? BugAntennae.Feathers : rng.Chance(0.5f) ? BugAntennae.Clubs : BugAntennae.Whips;
                    AntennaLength = rng.Range(0.7f, 1.3f);
                    Mandibles = 0f;
                    Horn = 0f;
                    WingPairs = 1 + rng.NextInt(2);
                    WingShape = rng.Chance(0.7f) ? BugWings.Round : BugWings.Swept;
                    WingLength = rng.Range(0.8f, 1.2f);
                    break;
                case EnemyFamily.Diver:
                    // A dragonfly (two pairs of long wings) or a wasp (one pair and a stinger): a long thin abdomen.
                    bool wasp = rng.Chance(0.4f);
                    LegPairs = 3;
                    LegLength = rng.Range(0.5f, 0.8f);
                    Segments = 4 + rng.NextInt(4);
                    Length = rng.Range(1f, 1.4f);
                    Bulk = rng.Range(0.8f, 1.1f);
                    Hump = rng.Range(0.85f, 1.1f);
                    Antennae = rng.Chance(0.6f) ? BugAntennae.Hooks : BugAntennae.Whips;
                    AntennaLength = rng.Range(0.4f, 0.9f);
                    Mandibles = rng.Chance(0.5f) ? rng.Range(0.3f, 0.7f) : 0f;
                    Horn = wasp ? rng.Range(0.5f, 1f) : 0f;
                    WingPairs = wasp ? 1 : 2;
                    WingShape = wasp ? BugWings.Swept : BugWings.Long;
                    WingLength = rng.Range(0.9f, 1.15f);
                    break;
                case EnemyFamily.Sentinel:
                    // A mantis: upright, two pairs of long walking legs and folded raptorial arms.
                    LegPairs = 2;
                    LegLength = rng.Range(0.9f, 1.2f);
                    Segments = 2 + rng.NextInt(3);
                    Length = rng.Range(0.85f, 1.2f);
                    Bulk = rng.Range(0.8f, 1.15f);
                    Hump = rng.Range(0.85f, 1.15f);
                    Antennae = rng.Chance(0.7f) ? BugAntennae.Whips : BugAntennae.Hooks;
                    AntennaLength = rng.Range(0.7f, 1.4f);
                    Mandibles = rng.Chance(0.4f) ? rng.Range(0.3f, 0.7f) : 0f;
                    Horn = rng.Chance(0.35f) ? rng.Range(0.4f, 1f) : 0f;
                    WingPairs = rng.Chance(0.5f) ? 1 : 0;
                    WingShape = BugWings.Long;
                    WingLength = rng.Range(0.7f, 1f);
                    break;
                case EnemyFamily.Bulwark:
                    // An armoured pill bug: a domed shell of overlapping plates over many short legs.
                    LegPairs = 5 + rng.NextInt(3);
                    LegLength = rng.Range(0.7f, 1.05f);
                    Segments = 4 + rng.NextInt(4);
                    Length = rng.Range(0.9f, 1.15f);
                    Bulk = rng.Range(0.9f, 1.1f);
                    Hump = rng.Range(0.8f, 1.15f);
                    Antennae = rng.Chance(0.5f) ? BugAntennae.Hooks : BugAntennae.Clubs;
                    AntennaLength = rng.Range(0.5f, 1f);
                    Mandibles = rng.Chance(0.4f) ? rng.Range(0.4f, 0.9f) : 0f;
                    Horn = rng.Chance(0.3f) ? rng.Range(0.5f, 1f) : 0f;
                    WingPairs = 0;
                    WingShape = BugWings.Round;
                    WingLength = 0f;
                    break;
                case EnemyFamily.Glimmer:
                    // A butterfly: a slim body under two pairs of big wings painted in the colours it may drop.
                    LegPairs = 3;
                    LegLength = rng.Range(0.5f, 0.8f);
                    Segments = 3 + rng.NextInt(3);
                    Length = rng.Range(0.85f, 1.15f);
                    Bulk = rng.Range(0.85f, 1.15f);
                    Hump = rng.Range(0.9f, 1.1f);
                    Antennae = rng.Chance(0.7f) ? BugAntennae.Clubs : BugAntennae.Feathers;
                    AntennaLength = rng.Range(0.8f, 1.3f);
                    Mandibles = 0f;
                    Horn = 0f;
                    WingPairs = 2;
                    WingShape = rng.Chance(0.7f) ? BugWings.Round : BugWings.Swept;
                    WingLength = rng.Range(0.85f, 1.1f);
                    break;
                default:
                    // Mites (and anything else small): a gnat or a flea, sometimes with a pair of wings.
                    LegPairs = 2 + rng.NextInt(2);
                    LegLength = rng.Range(0.8f, 1.3f);
                    Segments = 1 + rng.NextInt(2);
                    Length = rng.Range(0.85f, 1.2f);
                    Bulk = rng.Range(0.85f, 1.15f);
                    Hump = rng.Range(0.85f, 1.2f);
                    Antennae = rng.Chance(0.6f) ? BugAntennae.Whips : BugAntennae.None;
                    AntennaLength = rng.Range(0.7f, 1.3f);
                    Mandibles = rng.Chance(0.3f) ? rng.Range(0.3f, 0.8f) : 0f;
                    Horn = 0f;
                    WingPairs = rng.Chance(0.5f) ? 1 : 0;
                    WingShape = BugWings.Round;
                    WingLength = rng.Range(0.8f, 1.2f);
                    break;
            }
        }

        /// <summary>A nonzero, well-mixed seed from an enemy's id.</summary>
        public static uint SeedFrom(uint value) => RobotLook.SeedFrom(value ^ 0xB06B06u);
    }
}
