using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for banking.</summary>
    public sealed class BankingSettings
    {
        /// <summary>After "Reset position", no banking for this long (seconds), so a reset is never a shortcut home.</summary>
        public double ResetLockoutSeconds { get; set; } = 60.0;
    }

    /// <summary>
    /// Where and when carried essence goes into the party bank. Banking happens at the layer's doors (the
    /// raid door and the gates), not at shrines: shrines are where you respawn and where "Reset position"
    /// takes you, so banking there would make a reset a free trip home. A reset also locks a player out of
    /// banking for <see cref="BankingSettings.ResetLockoutSeconds"/>, since every door has a shrine nearby.
    /// </summary>
    public sealed class Banking
    {
        private readonly struct Site
        {
            public readonly Vector2 Point;
            public readonly float Radius;

            public Site(Vector2 point, float radius)
            {
                Point = point;
                Radius = radius;
            }
        }

        private readonly LayerMap _map;
        private readonly List<Site> _sites = new List<Site>();
        private readonly Dictionary<int, double> _lockedUntil = new Dictionary<int, double>();

        public Banking(LayerMap map, BankingSettings settings = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            Settings = settings ?? new BankingSettings();
        }

        public BankingSettings Settings { get; }

        /// <summary>The number of places that bank.</summary>
        public int SiteCount => _sites.Count;

        /// <summary>Makes standing within <paramref name="radius"/> metres of <paramref name="point"/> bank.</summary>
        public void AddSite(Vector2 point, float radius)
        {
            if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");
            _sites.Add(new Site(point, radius));
        }

        /// <summary>True when the position is at one of the bank sites.</summary>
        public bool IsAtBank(Vector2 position)
        {
            foreach (var s in _sites)
            {
                if (_map.Distance(s.Point, position) <= s.Radius) return true;
            }
            return false;
        }

        /// <summary>The player used "Reset position": no banking until the lockout runs out.</summary>
        public void PositionReset(int playerId, double now) => _lockedUntil[playerId] = now + Settings.ResetLockoutSeconds;

        /// <summary>Seconds until the player may bank again (0 if they can now).</summary>
        public double LockedFor(int playerId, double now) =>
            _lockedUntil.TryGetValue(playerId, out double until) ? Math.Max(0.0, until - now) : 0.0;

        public bool CanBank(int playerId, double now) => LockedFor(playerId, now) <= 0.0;

        /// <summary>The HUD line while locked out, for example "Can't bank your essence for 42s (you reset)".</summary>
        public static string LockoutText(double seconds) => $"Can't bank your essence for {(int)Math.Ceiling(seconds)}s (you reset)";

        /// <summary>
        /// Banks everything the wallet carries if its owner stands at a bank site and isn't locked out.
        /// Returns what was banked (empty if nothing was).
        /// </summary>
        public EssenceAmounts TryBank(EssenceWallet wallet, Vector2 position, double now)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (!CanBank(wallet.PlayerId, now) || !IsAtBank(position) || wallet.CarriedAll().IsEmpty) return new EssenceAmounts();
            return wallet.BankAll();
        }

        /// <summary>The player left: drop their lockout.</summary>
        public void Forget(int playerId) => _lockedUntil.Remove(playerId);
    }
}
