using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;

namespace FriendlyPersonShooter.Core.Social
{
    /// <summary>Tunables for quick-chat delivery. Metres and seconds.</summary>
    public sealed class QuickChatSettings
    {
        /// <summary>
        /// Messages reach every player within this distance of the sender, grouped or not
        /// ("everyone nearby"). 60 m is about a fight's width: anyone close enough to see you
        /// shooting hears you.
        /// </summary>
        public float Radius { get; set; } = 60f;

        /// <summary>Messages a player can send back to back before the rate limit kicks in.</summary>
        public int Burst { get; set; } = 3;

        /// <summary>One more message is allowed every this many seconds, up to <see cref="Burst"/>.</summary>
        public double RefillSeconds { get; set; } = 2.0;

        /// <summary>The very same message (line and slots) can't be repeated sooner than this.</summary>
        public double RepeatCooldown { get; set; } = 5.0;

        /// <summary>Messages each player's HUD keeps, newest last.</summary>
        public int HistorySize { get; set; } = 6;
    }

    /// <summary>Someone who might hear a message: a player id and where they stand.</summary>
    public readonly struct ChatListener
    {
        public readonly int PlayerId;
        /// <summary>World ground position (x, z).</summary>
        public readonly Vector2 Position;

        public ChatListener(int playerId, Vector2 position)
        {
            PlayerId = playerId;
            Position = position;
        }
    }

    /// <summary>A message as it was heard.</summary>
    public readonly struct QuickChatDelivery
    {
        public readonly int SenderId;
        /// <summary>Where the sender stood; "here" lines (bloom here, cache here) point at it.</summary>
        public readonly Vector2 SenderPosition;
        public readonly QuickChatMessage Message;
        public readonly double Time;

        public QuickChatDelivery(int senderId, Vector2 senderPosition, QuickChatMessage message, double time)
        {
            SenderId = senderId;
            SenderPosition = senderPosition;
            Message = message;
            Time = time;
        }
    }

    public enum QuickChatSendOutcome : byte
    {
        Delivered = 0,
        /// <summary>Unknown line or a slot with a bad value; nothing sent.</summary>
        Invalid = 1,
        /// <summary>Sent too many messages too fast; nothing sent.</summary>
        RateLimited = 2,
        /// <summary>Same message again too soon; nothing sent.</summary>
        Repeated = 3,
    }

    /// <summary>What happened to one send.</summary>
    public readonly struct QuickChatSendResult
    {
        public readonly QuickChatSendOutcome Outcome;
        public readonly QuickChatDelivery Delivery;
        /// <summary>Players who heard it, not counting the sender. Empty unless delivered.</summary>
        public readonly IReadOnlyList<int> Recipients;

        public QuickChatSendResult(QuickChatSendOutcome outcome, QuickChatDelivery delivery, IReadOnlyList<int> recipients)
        {
            Outcome = outcome;
            Delivery = delivery;
            Recipients = recipients;
        }

        public bool Delivered => Outcome == QuickChatSendOutcome.Delivered;
    }

    /// <summary>
    /// Delivers quick-chat to everyone within <see cref="QuickChatSettings.Radius"/> of the sender,
    /// measured the short way around the wrapping layer. The server owns one per lobby.
    /// <list type="bullet">
    /// <item>Anti-spam: each player has a small token bucket (<see cref="QuickChatSettings.Burst"/>
    /// messages, one more every <see cref="QuickChatSettings.RefillSeconds"/>) and can't repeat the
    /// exact same message within <see cref="QuickChatSettings.RepeatCooldown"/>. Refused messages
    /// cost nothing.</item>
    /// <item>Each player keeps a short history of what they heard (and said) for the HUD.</item>
    /// </list>
    /// </summary>
    public sealed class QuickChatChannel
    {
        private sealed class SenderState
        {
            public double Tokens;
            public double LastRefill;
            public readonly List<QuickChatDelivery> Recent = new List<QuickChatDelivery>();
        }

        private readonly LayerMap _map;
        private readonly QuickChatSettings _settings;
        private readonly Dictionary<int, SenderState> _senders = new Dictionary<int, SenderState>();
        private readonly Dictionary<int, List<QuickChatDelivery>> _history = new Dictionary<int, List<QuickChatDelivery>>();

        public QuickChatChannel(LayerMap map, QuickChatSettings settings = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _settings = settings ?? new QuickChatSettings();
        }

        public QuickChatSettings Settings => _settings;
        public LayerMap Map => _map;

        /// <summary>
        /// Sends a message from <paramref name="senderId"/> standing at <paramref name="senderPosition"/>
        /// to every listener in range. <paramref name="listeners"/> is everyone in the lobby (the sender
        /// may be among them and is skipped). Times are seconds on one clock and must not go backward.
        /// </summary>
        public QuickChatSendResult Send(int senderId, Vector2 senderPosition, QuickChatMessage message,
            IEnumerable<ChatListener> listeners, double now)
        {
            if (listeners == null) throw new ArgumentNullException(nameof(listeners));
            var delivery = new QuickChatDelivery(senderId, senderPosition, message, now);
            if (!QuickChatCatalog.IsValid(message, _map))
                return new QuickChatSendResult(QuickChatSendOutcome.Invalid, delivery, Array.Empty<int>());

            var state = StateOf(senderId, now);
            Refill(state, now);
            for (int i = 0; i < state.Recent.Count; i++)
            {
                var past = state.Recent[i];
                if (now - past.Time < _settings.RepeatCooldown && past.Message.Equals(message))
                    return new QuickChatSendResult(QuickChatSendOutcome.Repeated, delivery, Array.Empty<int>());
            }
            if (state.Tokens < 1.0)
                return new QuickChatSendResult(QuickChatSendOutcome.RateLimited, delivery, Array.Empty<int>());

            state.Tokens -= 1.0;
            state.Recent.Add(delivery);
            state.Recent.RemoveAll(d => now - d.Time >= _settings.RepeatCooldown);

            var recipients = new List<int>();
            foreach (var listener in listeners)
            {
                if (listener.PlayerId == senderId) continue;
                if (_map.Distance(senderPosition, listener.Position) > _settings.Radius) continue;
                recipients.Add(listener.PlayerId);
                Remember(listener.PlayerId, delivery);
            }
            Remember(senderId, delivery);
            return new QuickChatSendResult(QuickChatSendOutcome.Delivered, delivery, recipients);
        }

        /// <summary>True if the player could send a new message right now (rate limit only).</summary>
        public bool CanSend(int senderId, double now)
        {
            if (!_senders.TryGetValue(senderId, out var state)) return _settings.Burst >= 1;
            return TokensAt(state, now) >= 1.0;
        }

        /// <summary>What a player heard and said, oldest first, at most <see cref="QuickChatSettings.HistorySize"/>.</summary>
        public IReadOnlyList<QuickChatDelivery> HistoryOf(int playerId) =>
            _history.TryGetValue(playerId, out var list) ? list : (IReadOnlyList<QuickChatDelivery>)Array.Empty<QuickChatDelivery>();

        /// <summary>The HUD line for a delivery: the message text, rendered on this channel's layer.</summary>
        public string Render(QuickChatDelivery delivery, Func<int, string> playerName = null) =>
            QuickChatCatalog.Render(delivery.Message, _map, playerName);

        /// <summary>Forgets a player who left (their rate limit and history).</summary>
        public void Forget(int playerId)
        {
            _senders.Remove(playerId);
            _history.Remove(playerId);
        }

        private SenderState StateOf(int senderId, double now)
        {
            if (!_senders.TryGetValue(senderId, out var state))
            {
                state = new SenderState { Tokens = _settings.Burst, LastRefill = now };
                _senders.Add(senderId, state);
            }
            return state;
        }

        private double TokensAt(SenderState state, double now)
        {
            if (_settings.RefillSeconds <= 0) return _settings.Burst;
            double gained = Math.Max(0, now - state.LastRefill) / _settings.RefillSeconds;
            return Math.Min(_settings.Burst, state.Tokens + gained);
        }

        private void Refill(SenderState state, double now)
        {
            state.Tokens = TokensAt(state, now);
            state.LastRefill = Math.Max(state.LastRefill, now);
        }

        private void Remember(int playerId, QuickChatDelivery delivery)
        {
            if (_settings.HistorySize <= 0) return;
            if (!_history.TryGetValue(playerId, out var list))
            {
                list = new List<QuickChatDelivery>();
                _history.Add(playerId, list);
            }
            list.Add(delivery);
            if (list.Count > _settings.HistorySize) list.RemoveRange(0, list.Count - _settings.HistorySize);
        }
    }
}
