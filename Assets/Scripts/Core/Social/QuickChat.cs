using System;
using System.Collections.Generic;
using System.Text;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Social
{
    /// <summary>
    /// Every quick-chat message there is. There is no free text: a fixed list can't carry abuse,
    /// can be shown in the reader's own language, and is cheap to send (an id plus a few slots).
    /// Values are fixed so they can go over the network.
    /// </summary>
    public enum QuickChatId : byte
    {
        /// <summary>"Hello!"</summary>
        Hello = 0,
        /// <summary>"Thanks!"</summary>
        Thanks = 1,
        /// <summary>"Need a res!" (the sender is downed).</summary>
        NeedRevive = 2,
        /// <summary>"Need buffs"</summary>
        NeedBuffs = 3,
        /// <summary>"Seen any {color}?"</summary>
        SeenAny = 4,
        /// <summary>"{color} past {landmark}"</summary>
        ColorPast = 5,
        /// <summary>"Haven't seen {color}"</summary>
        NotSeen = 6,
        /// <summary>"{color} bloom here!" (at the sender's position).</summary>
        BloomHere = 7,
        /// <summary>"{color} bloom at {landmark}!"</summary>
        BloomAt = 8,
        /// <summary>"Gate at {landmark}"</summary>
        GateAt = 9,
        /// <summary>"Cache here" (a dropped cache at the sender's position).</summary>
        CacheHere = 10,
        /// <summary>"Follow me"</summary>
        FollowMe = 11,
        /// <summary>"Group up?"</summary>
        GroupUp = 12,
        /// <summary>"On my way"</summary>
        OnMyWay = 13,
        /// <summary>"{player} needs a res!"</summary>
        PlayerNeedsRevive = 14,
        /// <summary>"Heading to zone {zone}"</summary>
        HeadingTo = 15,
    }

    /// <summary>The typed blanks a message fills in.</summary>
    [Flags]
    public enum QuickChatSlots : byte
    {
        None = 0,
        Color = 1,
        Landmark = 2,
        Player = 4,
        Zone = 8,
    }

    /// <summary>
    /// One quick-chat message: which line, and the values for its slots. Slots the line doesn't use
    /// are ignored. Build with the static helpers (<see cref="ColorPast"/> and so on) or the constructor.
    /// </summary>
    public readonly struct QuickChatMessage : IEquatable<QuickChatMessage>
    {
        public readonly QuickChatId Id;
        public readonly EssenceColor Color;
        /// <summary>A <see cref="Landmark.Id"/> on the current layer, or -1.</summary>
        public readonly int LandmarkId;
        /// <summary>A player id, or -1.</summary>
        public readonly int PlayerId;
        public readonly ZoneCoord Zone;

        public QuickChatMessage(QuickChatId id, EssenceColor color = EssenceColor.Yellow, int landmarkId = -1,
            int playerId = -1, ZoneCoord zone = default)
        {
            Id = id;
            Color = color;
            LandmarkId = landmarkId;
            PlayerId = playerId;
            Zone = zone;
        }

        public static QuickChatMessage Simple(QuickChatId id) => new QuickChatMessage(id);
        public static QuickChatMessage SeenAny(EssenceColor color) => new QuickChatMessage(QuickChatId.SeenAny, color);
        public static QuickChatMessage NotSeen(EssenceColor color) => new QuickChatMessage(QuickChatId.NotSeen, color);
        public static QuickChatMessage ColorPast(EssenceColor color, int landmarkId) => new QuickChatMessage(QuickChatId.ColorPast, color, landmarkId);
        public static QuickChatMessage BloomHere(EssenceColor color) => new QuickChatMessage(QuickChatId.BloomHere, color);
        public static QuickChatMessage BloomAt(EssenceColor color, int landmarkId) => new QuickChatMessage(QuickChatId.BloomAt, color, landmarkId);
        public static QuickChatMessage GateAt(int landmarkId) => new QuickChatMessage(QuickChatId.GateAt, landmarkId: landmarkId);
        public static QuickChatMessage PlayerNeedsRevive(int playerId) => new QuickChatMessage(QuickChatId.PlayerNeedsRevive, playerId: playerId);
        public static QuickChatMessage HeadingTo(ZoneCoord zone) => new QuickChatMessage(QuickChatId.HeadingTo, zone: zone);

        /// <summary>
        /// Same line with the same values in the slots that line uses (unused slots don't count).
        /// Used by the repeat filter.
        /// </summary>
        public bool Equals(QuickChatMessage other)
        {
            if (Id != other.Id) return false;
            var slots = QuickChatCatalog.SlotsOf(Id);
            if ((slots & QuickChatSlots.Color) != 0 && Color != other.Color) return false;
            if ((slots & QuickChatSlots.Landmark) != 0 && LandmarkId != other.LandmarkId) return false;
            if ((slots & QuickChatSlots.Player) != 0 && PlayerId != other.PlayerId) return false;
            if ((slots & QuickChatSlots.Zone) != 0 && Zone != other.Zone) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is QuickChatMessage other && Equals(other);
        public override int GetHashCode() => (int)Id;
        public override string ToString() => $"{Id}";
    }

    /// <summary>One line of the catalog: its slots, its English template, and what it counts as.</summary>
    public sealed class QuickChatEntry
    {
        internal QuickChatEntry(QuickChatId id, QuickChatSlots slots, string template, bool isInfo)
        {
            Id = id;
            Slots = slots;
            Template = template;
            IsInfo = isInfo;
        }

        public QuickChatId Id { get; }
        /// <summary>Slots the message must fill.</summary>
        public QuickChatSlots Slots { get; }
        /// <summary>
        /// English text with {color}, {landmark}, {player} and {zone} blanks. The id is the
        /// translation key, so other languages swap the template and keep the slots.
        /// </summary>
        public string Template { get; }
        /// <summary>
        /// True when the line tells the reader something about the world (where a color, bloom,
        /// gate or cache is, or that a color isn't around). Receiving one from a stranger counts as
        /// an "info" exchange for step 5's measure.
        /// </summary>
        public bool IsInfo { get; }
    }

    /// <summary>The fixed quick-chat wheel, and turning a message into display text.</summary>
    public static class QuickChatCatalog
    {
        private static readonly QuickChatEntry[] Entries =
        {
            new QuickChatEntry(QuickChatId.Hello, QuickChatSlots.None, "Hello!", false),
            new QuickChatEntry(QuickChatId.Thanks, QuickChatSlots.None, "Thanks!", false),
            new QuickChatEntry(QuickChatId.NeedRevive, QuickChatSlots.None, "Need a res!", false),
            new QuickChatEntry(QuickChatId.NeedBuffs, QuickChatSlots.None, "Need buffs", false),
            new QuickChatEntry(QuickChatId.SeenAny, QuickChatSlots.Color, "Seen any {color}?", false),
            new QuickChatEntry(QuickChatId.ColorPast, QuickChatSlots.Color | QuickChatSlots.Landmark, "{color} past {landmark}", true),
            new QuickChatEntry(QuickChatId.NotSeen, QuickChatSlots.Color, "Haven't seen {color}", true),
            new QuickChatEntry(QuickChatId.BloomHere, QuickChatSlots.Color, "{color} bloom here!", true),
            new QuickChatEntry(QuickChatId.BloomAt, QuickChatSlots.Color | QuickChatSlots.Landmark, "{color} bloom at {landmark}!", true),
            new QuickChatEntry(QuickChatId.GateAt, QuickChatSlots.Landmark, "Gate at {landmark}", true),
            new QuickChatEntry(QuickChatId.CacheHere, QuickChatSlots.None, "Cache here", true),
            new QuickChatEntry(QuickChatId.FollowMe, QuickChatSlots.None, "Follow me", false),
            new QuickChatEntry(QuickChatId.GroupUp, QuickChatSlots.None, "Group up?", false),
            new QuickChatEntry(QuickChatId.OnMyWay, QuickChatSlots.None, "On my way", false),
            new QuickChatEntry(QuickChatId.PlayerNeedsRevive, QuickChatSlots.Player, "{player} needs a res!", false),
            new QuickChatEntry(QuickChatId.HeadingTo, QuickChatSlots.Zone, "Heading to zone {zone}", false),
        };

        /// <summary>Every line, in id order.</summary>
        public static IReadOnlyList<QuickChatEntry> All => Entries;

        public static QuickChatEntry Get(QuickChatId id)
        {
            int i = (int)id;
            if (i < 0 || i >= Entries.Length) throw new ArgumentOutOfRangeException(nameof(id));
            return Entries[i];
        }

        public static bool Exists(QuickChatId id) => (int)id < Entries.Length;

        public static QuickChatSlots SlotsOf(QuickChatId id) => Exists(id) ? Entries[(int)id].Slots : QuickChatSlots.None;

        public static bool IsInfo(QuickChatId id) => Exists(id) && Entries[(int)id].IsInfo;

        /// <summary>
        /// True when the message names a real line and fills every slot it needs with a valid value:
        /// a defined color, a landmark on this layer, a player id of 0 or more, a zone on the grid.
        /// The server checks this before relaying, so a client can't send a made-up landmark.
        /// </summary>
        public static bool IsValid(QuickChatMessage message, LayerMap map)
        {
            if (!Exists(message.Id)) return false;
            var slots = SlotsOf(message.Id);
            if ((slots & QuickChatSlots.Color) != 0 && (int)message.Color >= EssenceColors.Count) return false;
            if ((slots & QuickChatSlots.Landmark) != 0 &&
                (map == null || message.LandmarkId < 0 || message.LandmarkId >= map.Landmarks.Count)) return false;
            if ((slots & QuickChatSlots.Player) != 0 && message.PlayerId < 0) return false;
            if ((slots & QuickChatSlots.Zone) != 0 && map != null &&
                !map.Tiling.InRange(message.Zone)) return false;
            return true;
        }

        /// <summary>
        /// The English text for a message, for example "Yellow past the Broken Tower".
        /// <paramref name="playerName"/> names players for the {player} slot (defaults to "Player 12").
        /// Invalid slots render as "?".
        /// </summary>
        public static string Render(QuickChatMessage message, LayerMap map, Func<int, string> playerName = null)
        {
            if (!Exists(message.Id)) return "?";
            string template = Entries[(int)message.Id].Template;
            var sb = new StringBuilder(template.Length + 24);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c != '{')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                int end = template.IndexOf('}', i);
                string slot = template.Substring(i + 1, end - i - 1);
                sb.Append(SlotText(slot, message, map, playerName));
                i = end + 1;
            }
            // A line that starts with a landmark name ("the ...") still reads as a sentence.
            if (sb.Length > 0 && char.IsLower(sb[0])) sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }

        private static string SlotText(string slot, QuickChatMessage message, LayerMap map, Func<int, string> playerName)
        {
            switch (slot)
            {
                case "color":
                    return (int)message.Color < EssenceColors.Count ? message.Color.ToString() : "?";
                case "landmark":
                    if (map == null || message.LandmarkId < 0 || message.LandmarkId >= map.Landmarks.Count) return "?";
                    return map.Landmarks[message.LandmarkId].Name;
                case "player":
                    if (message.PlayerId < 0) return "?";
                    return playerName != null ? playerName(message.PlayerId) : $"Player {message.PlayerId}";
                case "zone":
                    return message.Zone.ToString();
                default:
                    return "?";
            }
        }
    }
}
