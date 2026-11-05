using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Avatars
{
    public enum RobotHead : byte
    {
        /// <summary>A big faceted ball.</summary>
        Round,
        /// <summary>A chunky box, like an old TV.</summary>
        Boxy,
        /// <summary>A tall tin can.</summary>
        Can,
        /// <summary>A wide, squashed bean.</summary>
        Bean,
    }

    public enum RobotEars : byte
    {
        /// <summary>Pointy cat ears, like the reference robot.</summary>
        Cat,
        /// <summary>A springy antenna with a ball on top.</summary>
        Antenna,
        /// <summary>A beanie propeller that spins.</summary>
        Propeller,
        /// <summary>Two long floppy ears, one bent over.</summary>
        Bunny,
        /// <summary>Round headphone cups.</summary>
        Headphones,
        /// <summary>Two antennae of different heights.</summary>
        TwinAntennae,
    }

    public enum RobotEyes : byte
    {
        /// <summary>Two eyes on a visor, rarely the same size.</summary>
        Pair,
        /// <summary>One huge eye.</summary>
        Cyclops,
        /// <summary>Three eyes in a row.</summary>
        Triple,
        /// <summary>Big white googly eyes with pupils that look different ways.</summary>
        Googly,
    }

    public enum RobotBody : byte
    {
        /// <summary>An egg, wide at the bottom.</summary>
        Egg,
        /// <summary>A barrel.</summary>
        Barrel,
        /// <summary>An upside-down cone, wide at the shoulders.</summary>
        Cone,
        /// <summary>A small round ball (a big head on a tiny body).</summary>
        Ball,
    }

    public enum RobotMouth : byte
    {
        None,
        Grin,
        /// <summary>A small round "o".</summary>
        Oh,
        /// <summary>A row of square teeth.</summary>
        Teeth,
    }

    /// <summary>
    /// A floating robot avatar's recipe: shapes, sizes and colours from a seed, so every player
    /// gets a different goofy robot and everyone sees the same one. Colours are hues and
    /// lightness values (0..1); the view turns them into a mesh. The body's main accent colour
    /// is the player's team colour, so it isn't part of the recipe.
    /// </summary>
    public readonly struct RobotLook
    {
        public readonly RobotHead Head;
        public readonly RobotEars Ears;
        public readonly RobotEyes Eyes;
        public readonly RobotBody Body;
        public readonly RobotMouth Mouth;

        /// <summary>Head size multipliers (width, height, depth), about 0.8..1.5. Goofy robots have big heads.</summary>
        public readonly float HeadWidth, HeadHeight, HeadDepth;
        /// <summary>Body size multipliers, about 0.75..1.25.</summary>
        public readonly float BodyWidth, BodyHeight;
        /// <summary>Gap between body and head (a springy neck when long), metres.</summary>
        public readonly float Neck;
        /// <summary>Resting head tilt in degrees (a confused lean).</summary>
        public readonly float HeadTilt;
        /// <summary>Eye sizes, left and right (Pair and Googly); 1 = normal.</summary>
        public readonly float LeftEye, RightEye;
        /// <summary>Mitt size multiplier, 0.9..1.5: big hands are funny.</summary>
        public readonly float HandSize;
        /// <summary>True for three chunky fingers (like the reference); false for a round mitt.</summary>
        public readonly bool Fingers;
        /// <summary>Two little thrusters under the body instead of one.</summary>
        public readonly bool TwinJets;
        /// <summary>Shell lightness 0.8..1 (mostly white, like the reference) and a slight hue for it (pearl, cream, mint...).</summary>
        public readonly float ShellLight, ShellHue, ShellTint;
        /// <summary>Hue of the glowing eyes and thrusters.</summary>
        public readonly float GlowHue;
        /// <summary>Bob speed multiplier and a phase, so robots don't bob in sync.</summary>
        public readonly float BobSpeed, BobPhase;

        public RobotLook(uint seed)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            Head = (RobotHead)rng.NextInt(4);
            Ears = (RobotEars)rng.NextInt(6);
            Eyes = (RobotEyes)rng.NextInt(4);
            Body = (RobotBody)rng.NextInt(4);
            Mouth = (RobotMouth)rng.NextInt(4);

            HeadWidth = rng.Range(0.9f, 1.45f);
            HeadHeight = rng.Range(0.8f, 1.35f);
            HeadDepth = rng.Range(0.85f, 1.2f);
            BodyWidth = rng.Range(0.75f, 1.25f);
            BodyHeight = rng.Range(0.8f, 1.2f);
            Neck = rng.Chance(0.3f) ? rng.Range(0.12f, 0.24f) : rng.Range(0.02f, 0.07f);
            HeadTilt = rng.Range(-14f, 14f);

            // Mismatched eyes are the default; a matched pair is the rare case.
            LeftEye = rng.Range(0.7f, 1.4f);
            RightEye = rng.Chance(0.25f) ? LeftEye : rng.Range(0.7f, 1.4f);

            HandSize = rng.Range(0.9f, 1.5f);
            Fingers = rng.Chance(0.5f);
            TwinJets = rng.Chance(0.4f);

            ShellLight = rng.Range(0.8f, 1f);
            ShellHue = rng.NextFloat();
            ShellTint = rng.Range(0f, 0.18f);
            GlowHue = rng.Chance(0.5f) ? rng.Range(0.48f, 0.58f) : rng.NextFloat();
            BobSpeed = rng.Range(0.8f, 1.3f);
            BobPhase = rng.Range(0f, 6.2831853f);
        }

        /// <summary>A nonzero seed from any number (0 means "not assigned yet" on the wire).</summary>
        public static uint SeedFrom(uint value)
        {
            uint x = value * 0x9E3779B9u + 0x7F4A7C15u;
            x ^= x >> 16;
            x *= 0x85EBCA6Bu;
            x ^= x >> 13;
            return x == 0 ? 1u : x;
        }
    }
}
