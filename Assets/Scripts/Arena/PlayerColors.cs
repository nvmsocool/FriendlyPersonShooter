using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Each player's color (by network id), used for their body and their name on the HUD.</summary>
    public static class PlayerColors
    {
        public static readonly Color[] Palette =
        {
            new Color(0.25f, 0.6f, 1f),   // blue
            new Color(1f, 0.45f, 0.3f),   // coral
            new Color(0.45f, 0.9f, 0.3f), // lime
            new Color(1f, 0.8f, 0.2f),    // gold
            new Color(0.7f, 0.4f, 1f),    // violet
            new Color(0.2f, 0.85f, 0.8f), // teal
            new Color(1f, 0.55f, 0.85f),  // pink
            new Color(0.95f, 0.95f, 0.95f), // white
        };

        public static readonly string[] Names = { "Blue", "Coral", "Lime", "Gold", "Violet", "Teal", "Pink", "White" };

        public static int Index(int networkId) => ((networkId - 1) % Palette.Length + Palette.Length) % Palette.Length;
        public static Color For(int networkId) => Palette[Index(networkId)];

        /// <summary>The player's display name, e.g. "Coral".</summary>
        public static string Name(int networkId) => Names[Index(networkId)];

        /// <summary>The name in its color, for rich text.</summary>
        public static string Tag(int networkId) => $"<color=#{ColorUtility.ToHtmlStringRGB(For(networkId))}>{Name(networkId)}</color>";
    }
}
