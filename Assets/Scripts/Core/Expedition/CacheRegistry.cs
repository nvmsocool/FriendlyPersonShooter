using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for dropped essence caches. Seconds.</summary>
    public sealed class CacheSettings
    {
        /// <summary>
        /// How long a cache lies on the ground before it fades and its essence is lost. 10 minutes:
        /// long enough to walk back from the shrine you respawned at, or for a stranger to find it.
        /// </summary>
        public double Lifetime { get; set; } = 600.0;
        /// <summary>Kudos a player earns for returning someone else's cache.</summary>
        public int ReturnKudos { get; set; } = 1;
    }

    /// <summary>Essence dropped where a player bled out.</summary>
    public sealed class EssenceCache
    {
        internal EssenceCache(int id, int ownerId, Vector2 position, EssenceAmounts contents, double droppedAt, double expiresAt)
        {
            Id = id;
            OwnerId = ownerId;
            Position = position;
            Contents = contents;
            DroppedAt = droppedAt;
            ExpiresAt = expiresAt;
        }

        public int Id { get; }
        public int OwnerId { get; }
        /// <summary>World ground position (x, z).</summary>
        public Vector2 Position { get; }
        public EssenceAmounts Contents { get; }
        public double DroppedAt { get; }
        public double ExpiresAt { get; }

        public bool IsExpired(double now) => now >= ExpiresAt;
    }

    public enum CachePickupOutcome : byte
    {
        /// <summary>No such cache (already picked up, or never existed).</summary>
        NotFound = 0,
        /// <summary>The cache had faded; its essence is gone.</summary>
        Expired = 1,
        /// <summary>The owner picked up their own cache: it goes back to their carried essence.</summary>
        Recovered = 2,
        /// <summary>Someone else picked it up: it is sent to the owner's banked essence and the finder earns kudos.</summary>
        Returned = 3,
    }

    /// <summary>What happened when a player touched a cache.</summary>
    public readonly struct CachePickup
    {
        public readonly CachePickupOutcome Outcome;
        public readonly int CacheId;
        public readonly int OwnerId;
        public readonly int PickerId;
        /// <summary>The essence going back to the owner (null for NotFound or Expired).</summary>
        public readonly EssenceAmounts Contents;
        public readonly int KudosEarned;

        public CachePickup(CachePickupOutcome outcome, int cacheId, int ownerId, int pickerId, EssenceAmounts contents, int kudosEarned)
        {
            Outcome = outcome;
            CacheId = cacheId;
            OwnerId = ownerId;
            PickerId = pickerId;
            Contents = contents;
            KudosEarned = kudosEarned;
        }

        /// <summary>True when essence goes back to the owner.</summary>
        public bool Delivers => Outcome == CachePickupOutcome.Recovered || Outcome == CachePickupOutcome.Returned;

        /// <summary>Returned caches land banked; a recovered one lands carried.</summary>
        public bool ToBanked => Outcome == CachePickupOutcome.Returned;

        /// <summary>Puts the contents into the owner's wallet (banked or carried, see <see cref="ToBanked"/>).</summary>
        public void DeliverTo(EssenceWallet owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!Delivers) return;
            if (owner.PlayerId != OwnerId) throw new ArgumentException("That wallet doesn't belong to the cache's owner.");
            owner.Receive(Contents, ToBanked);
        }
    }

    /// <summary>
    /// The lobby's dropped caches. A cache always goes back to the player who dropped it:
    /// <list type="bullet">
    /// <item>Anyone else who picks it up sends it to the owner's <em>banked</em> essence (it travels
    /// through the shrines, so it can't be lost again and the owner needn't be nearby) and earns
    /// <see cref="CacheSettings.ReturnKudos"/>. A returned cache is worth more than one you recover
    /// yourself, which is the point: strangers are good news.</item>
    /// <item>The owner picking up their own cache gets it back as <em>carried</em>, like any pickup.</item>
    /// <item>Unclaimed caches fade after <see cref="CacheSettings.Lifetime"/> and the essence is lost.</item>
    /// </list>
    /// </summary>
    public sealed class CacheRegistry
    {
        private readonly CacheSettings _settings;
        private readonly Dictionary<int, EssenceCache> _caches = new Dictionary<int, EssenceCache>();
        private readonly Dictionary<int, int> _kudos = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _returns = new Dictionary<int, int>();
        private int _nextId = 1;

        public CacheRegistry(CacheSettings settings = null)
        {
            _settings = settings ?? new CacheSettings();
        }

        /// <summary>Caches on the ground, oldest first (some may have expired until <see cref="ExpireOld"/> runs).</summary>
        public IEnumerable<EssenceCache> All
        {
            get
            {
                var list = new List<EssenceCache>(_caches.Values);
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                return list;
            }
        }

        public int Count => _caches.Count;

        public EssenceCache Find(int cacheId) => _caches.TryGetValue(cacheId, out var c) ? c : null;

        /// <summary>Drops a cache. Returns null (and drops nothing) if the contents are empty.</summary>
        public EssenceCache Drop(int ownerId, Vector2 position, EssenceAmounts contents, double now)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            if (contents.IsEmpty) return null;
            var cache = new EssenceCache(_nextId++, ownerId, position, contents.Clone(), now, now + _settings.Lifetime);
            _caches.Add(cache.Id, cache);
            return cache;
        }

        /// <summary>
        /// A player bled out: takes <paramref name="share"/> of their carried essence
        /// (<see cref="Combat.ReviveSettings.EssenceDropShare"/>) and drops it here. Null if nothing dropped.
        /// </summary>
        public EssenceCache DropFrom(EssenceWallet wallet, Vector2 position, float share, double now)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            return Drop(wallet.PlayerId, position, wallet.DropCache(share), now);
        }

        /// <summary>
        /// A player touched a cache. Removes it and says where the essence goes; call
        /// <see cref="CachePickup.DeliverTo"/> with the owner's wallet to pay it out.
        /// </summary>
        public CachePickup PickUp(int cacheId, int pickerId, double now)
        {
            if (!_caches.TryGetValue(cacheId, out var cache))
                return new CachePickup(CachePickupOutcome.NotFound, cacheId, -1, pickerId, null, 0);

            _caches.Remove(cacheId);
            if (cache.IsExpired(now))
                return new CachePickup(CachePickupOutcome.Expired, cacheId, cache.OwnerId, pickerId, null, 0);

            if (pickerId == cache.OwnerId)
                return new CachePickup(CachePickupOutcome.Recovered, cacheId, cache.OwnerId, pickerId, cache.Contents, 0);

            int kudos = _settings.ReturnKudos;
            _kudos.TryGetValue(pickerId, out int k);
            _kudos[pickerId] = k + kudos;
            _returns.TryGetValue(pickerId, out int r);
            _returns[pickerId] = r + 1;
            return new CachePickup(CachePickupOutcome.Returned, cacheId, cache.OwnerId, pickerId, cache.Contents, kudos);
        }

        /// <summary>Removes caches that have faded by <paramref name="now"/> and returns them.</summary>
        public IReadOnlyList<EssenceCache> ExpireOld(double now)
        {
            var expired = new List<EssenceCache>();
            foreach (var cache in All)
            {
                if (cache.IsExpired(now)) expired.Add(cache);
            }
            foreach (var cache in expired) _caches.Remove(cache.Id);
            return expired;
        }

        /// <summary>Kudos a player has earned by returning caches.</summary>
        public int KudosOf(int playerId) => _kudos.TryGetValue(playerId, out int k) ? k : 0;

        /// <summary>How many caches a player has returned to others.</summary>
        public int ReturnsBy(int playerId) => _returns.TryGetValue(playerId, out int r) ? r : 0;
    }
}
