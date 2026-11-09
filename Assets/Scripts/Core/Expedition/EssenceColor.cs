using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// The six essence colors, one per enemy family. Values are fixed so they can be sent over the
    /// network and used as array indexes.
    /// </summary>
    public enum EssenceColor : byte
    {
        /// <summary>Dropped by Wisps (flicking).</summary>
        Yellow = 0,
        /// <summary>Dropped by Skitters (tracking).</summary>
        Red = 1,
        /// <summary>Dropped by Mite swarms (target switching).</summary>
        Green = 2,
        /// <summary>Dropped by Sentinels (long-range precision).</summary>
        Blue = 3,
        /// <summary>Dropped by Bulwarks (timing).</summary>
        Purple = 4,
        /// <summary>Dropped by Divers (vertical tracking).</summary>
        Orange = 5,
    }

    /// <summary>Maps enemy families to the essence color they drop, and back.</summary>
    public static class EssenceColors
    {
        /// <summary>How many essence colors exist.</summary>
        public const int Count = 6;

        private static readonly EssenceColor[] AllColors =
        {
            EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green,
            EssenceColor.Blue, EssenceColor.Purple, EssenceColor.Orange,
        };

        /// <summary>Every color, in enum order.</summary>
        public static IReadOnlyList<EssenceColor> All => AllColors;

        /// <summary>
        /// The color a family drops: Wisp → Yellow, Skitter → Red, Mite → Green, Sentinel → Blue, Bulwark → Purple,
        /// Diver → Orange.
        /// </summary>
        public static EssenceColor Of(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return EssenceColor.Yellow;
                case EnemyFamily.Skitter: return EssenceColor.Red;
                case EnemyFamily.Glimmer: return EssenceColor.Yellow;
                case EnemyFamily.Bulwark: return EssenceColor.Purple;
                case EnemyFamily.Sentinel: return EssenceColor.Blue;
                case EnemyFamily.Diver: return EssenceColor.Orange;
                default: return EssenceColor.Green;
            }
        }

        /// <summary>The family that drops a color (every color has one).</summary>
        public static bool TryGetFamily(EssenceColor color, out EnemyFamily family)
        {
            switch (color)
            {
                case EssenceColor.Yellow: family = EnemyFamily.Wisp; return true;
                case EssenceColor.Red: family = EnemyFamily.Skitter; return true;
                case EssenceColor.Green: family = EnemyFamily.Mite; return true;
                case EssenceColor.Blue: family = EnemyFamily.Sentinel; return true;
                case EssenceColor.Purple: family = EnemyFamily.Bulwark; return true;
                case EssenceColor.Orange: family = EnemyFamily.Diver; return true;
                default: family = default; return false;
            }
        }

        /// <summary>A family's name in a sentence ("Mites", "Sentinels").</summary>
        public static string Plural(EnemyFamily family) => family == EnemyFamily.Mite ? "Mites" : family + "s";
    }
}
