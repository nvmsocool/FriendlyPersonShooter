using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;

namespace FriendlyPersonShooter.Core.Social
{
    /// <summary>How chatty and sociable a bot is. Chances are 0–1; times are seconds.</summary>
    public sealed class SocialBrainSettings
    {
        /// <summary>Calls out a bloom it sees.</summary>
        public float AnnounceBloomChance { get; set; } = 0.9f;
        /// <summary>Calls "Need a res!" on going down.</summary>
        public float CallForReviveChance { get; set; } = 1f;
        /// <summary>Says "Thanks!" after a revive.</summary>
        public float ThankReviveChance { get; set; } = 1f;
        /// <summary>Says "Thanks!" after being buffed by someone (at most once per <see cref="ThanksCooldown"/> per player).</summary>
        public float ThankBuffChance { get; set; } = 0.5f;
        /// <summary>Answers "Seen any {color}?" when it has a pin of that color.</summary>
        public float AnswerChance { get; set; } = 0.9f;
        /// <summary>Says "Haven't seen {color}" when asked and it has nothing.</summary>
        public float AnswerNotSeenChance { get; set; } = 0.2f;
        /// <summary>Asks "Seen any {color}?" when it needs a color it has no pin for.</summary>
        public float AskChance { get; set; } = 0.6f;
        /// <summary>Accepts a group invite.</summary>
        public float AcceptInviteChance { get; set; } = 0.5f;
        /// <summary>Follows a tip about a color it needs.</summary>
        public float FollowTipChance { get; set; } = 0.8f;

        /// <summary>Won't thank the same player for buffs more often than this.</summary>
        public double ThanksCooldown { get; set; } = 30.0;
        /// <summary>Won't ask about the same color more often than this.</summary>
        public double AskCooldown { get; set; } = 45.0;
        /// <summary>Won't announce the same bloom twice within this long.</summary>
        public double BloomCooldown { get; set; } = 120.0;
        /// <summary>Largest group (counting itself) it will join by accepting an invite.</summary>
        public int MaxGroupSize { get; set; } = 4;

        /// <summary>The usual stranger-friendly bot.</summary>
        public static SocialBrainSettings Friendly() => new SocialBrainSettings();

        /// <summary>A Grouper: always accepts invites.</summary>
        public static SocialBrainSettings Grouper() => new SocialBrainSettings { AcceptInviteChance = 1f, MaxGroupSize = 8 };

        /// <summary>Silent: never chats or groups (stands in for players who ignore strangers).</summary>
        public static SocialBrainSettings Silent() => new SocialBrainSettings
        {
            AnnounceBloomChance = 0, CallForReviveChance = 0, ThankReviveChance = 0, ThankBuffChance = 0,
            AnswerChance = 0, AnswerNotSeenChance = 0, AskChance = 0, AcceptInviteChance = 0, FollowTipChance = 0,
        };
    }

    /// <summary>
    /// Decides what a bot says and whether it groups, the way a social player would: calls out
    /// blooms, asks for a res when down, thanks people who helped, answers "Seen any X?" from its
    /// sightings, follows tips about colors it needs, and sometimes accepts invites. Every method
    /// returns its decision and changes nothing outside the brain; the caller sends the message.
    /// Deterministic for the same seed, player id and sequence of calls.
    /// </summary>
    public sealed class SocialBrain
    {
        private readonly SocialBrainSettings _settings;
        private CombatRandom _random;
        private readonly Dictionary<int, double> _lastThanked = new Dictionary<int, double>();
        private readonly Dictionary<EssenceColor, double> _lastAsked = new Dictionary<EssenceColor, double>();
        private readonly Dictionary<long, double> _lastBloom = new Dictionary<long, double>();

        public SocialBrain(int playerId, uint seed, SocialBrainSettings settings = null)
        {
            PlayerId = playerId;
            _settings = settings ?? new SocialBrainSettings();
            _random = new CombatRandom(seed ^ ((uint)playerId * 0x9E3779B1u) ^ 0x50C1A1u);
        }

        public int PlayerId { get; }
        public SocialBrainSettings Settings => _settings;

        /// <summary>Saw a bloom: "{color} bloom at {landmark}!" (or "bloom here!" if the layer has no landmarks).</summary>
        public QuickChatMessage? OnSawBloom(Bloom bloom, LayerMap map, double now)
        {
            if (bloom == null) throw new ArgumentNullException(nameof(bloom));
            if (map == null) throw new ArgumentNullException(nameof(map));
            long key = ((long)bloom.Zone.X << 40) ^ ((long)bloom.Zone.Y << 20) ^ bloom.Slot;
            if (_lastBloom.TryGetValue(key, out double last) && now - last < _settings.BloomCooldown) return null;
            if (!Roll(_settings.AnnounceBloomChance)) return null;
            _lastBloom[key] = now;
            var landmark = map.NearestLandmark(bloom.Point.World);
            return landmark != null
                ? QuickChatMessage.BloomAt(bloom.Color, landmark.Id)
                : QuickChatMessage.BloomHere(bloom.Color);
        }

        /// <summary>Went down: "Need a res!".</summary>
        public QuickChatMessage? OnDowned(double now) =>
            Roll(_settings.CallForReviveChance) ? QuickChatMessage.Simple(QuickChatId.NeedRevive) : (QuickChatMessage?)null;

        /// <summary>Was revived by someone: "Thanks!".</summary>
        public QuickChatMessage? OnRevived(int byPlayer, double now)
        {
            if (byPlayer == PlayerId || !Roll(_settings.ThankReviveChance)) return null;
            _lastThanked[byPlayer] = now;
            return QuickChatMessage.Simple(QuickChatId.Thanks);
        }

        /// <summary>Was buffed by someone: sometimes "Thanks!", at most once per cooldown per player.</summary>
        public QuickChatMessage? OnBuffed(int byPlayer, double now)
        {
            if (byPlayer == PlayerId) return null;
            if (_lastThanked.TryGetValue(byPlayer, out double last) && now - last < _settings.ThanksCooldown) return null;
            if (!Roll(_settings.ThankBuffChance)) return null;
            _lastThanked[byPlayer] = now;
            return QuickChatMessage.Simple(QuickChatId.Thanks);
        }

        /// <summary>
        /// Needs a color: asks "Seen any {color}?" unless its own book already has a pin for it.
        /// At most once per <see cref="SocialBrainSettings.AskCooldown"/> per color.
        /// </summary>
        public QuickChatMessage? OnNeedColor(EssenceColor color, SightingBook book, double now)
        {
            if (book != null && book.Latest(color, now) != null) return null;
            if (_lastAsked.TryGetValue(color, out double last) && now - last < _settings.AskCooldown) return null;
            if (!Roll(_settings.AskChance)) return null;
            _lastAsked[color] = now;
            return QuickChatMessage.SeenAny(color);
        }

        /// <summary>
        /// Heard a message. Answers "Seen any {color}?" with its freshest pin of that color
        /// ("{color} past {landmark}", or the bloom line), or now and then "Haven't seen {color}".
        /// Returns null for anything it doesn't answer, including its own messages.
        /// </summary>
        public QuickChatMessage? OnHeard(QuickChatDelivery heard, SightingBook book, double now)
        {
            if (heard.SenderId == PlayerId) return null;
            if (heard.Message.Id != QuickChatId.SeenAny) return null;
            var color = heard.Message.Color;
            var sighting = book?.Latest(color, now);
            if (sighting != null && sighting.LandmarkId >= 0)
                return Roll(_settings.AnswerChance) ? sighting.ToQuickChat() : (QuickChatMessage?)null;
            return Roll(_settings.AnswerNotSeenChance) ? QuickChatMessage.NotSeen(color) : (QuickChatMessage?)null;
        }

        /// <summary>
        /// Heard a tip: if it points at a color the bot needs ("{color} past {landmark}", a bloom),
        /// decides whether to head there and returns the spot. "Here" lines point at the sender.
        /// </summary>
        public bool TryFollowTip(QuickChatDelivery heard, EssenceColor needed, LayerMap map, out Vector2 target)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            target = default;
            if (heard.SenderId == PlayerId) return false;
            var m = heard.Message;
            switch (m.Id)
            {
                case QuickChatId.ColorPast:
                case QuickChatId.BloomAt:
                    if (m.Color != needed || m.LandmarkId < 0 || m.LandmarkId >= map.Landmarks.Count) return false;
                    if (!Roll(_settings.FollowTipChance)) return false;
                    target = map.Landmarks[m.LandmarkId].Point.World;
                    return true;
                case QuickChatId.BloomHere:
                    if (m.Color != needed || !Roll(_settings.FollowTipChance)) return false;
                    target = heard.SenderPosition;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether to accept a group invite. Never when the group would pass
        /// <see cref="SocialBrainSettings.MaxGroupSize"/> or the inviter is blocked.
        /// </summary>
        public bool ShouldAcceptInvite(int inviterId, int inviterGroupSize, bool inviterBlocked = false)
        {
            if (inviterId == PlayerId || inviterBlocked) return false;
            if (inviterGroupSize + 1 > _settings.MaxGroupSize) return false;
            return Roll(_settings.AcceptInviteChance);
        }

        private bool Roll(float chance)
        {
            if (chance <= 0f) return false;
            if (chance >= 1f) return true;
            return _random.Chance(chance);
        }
    }
}
