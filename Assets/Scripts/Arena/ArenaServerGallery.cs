using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Gallery;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Ticks to seconds for galleries (the default 60 Hz simulation, as on every client).</summary>
    public static class GalleryClock
    {
        public const int TicksPerSecond = 60;
        public static float Seconds(int ticks) => ticks / (float)TicksPerSecond;
    }

    /// <summary>
    /// The shooting gallery half of the server. Big actions open a gallery for one player: their world goes grey,
    /// targets drift around where they were looking, and their shots go at the targets instead of the world (one
    /// shot per click). The targets follow from the gallery's seed and start tick (Core.Gallery), so the server
    /// judges each shot at the tick the player saw, like enemy hits. A player is fully protected while a gallery is
    /// open. Shooting things in the world (loot boxes, items, reward chests) is also here.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>A gallery's opening aim is kept this far from straight up or down, so targets stay on screen.</summary>
        private const float GalleryPitchLimit = 60f;
        /// <summary>A reward chest that ends up farther than this from its player (they travelled) moves back beside them.</summary>
        private const float ChestFollowRange = 40f;
        private const float BoxHitRadius = 0.8f, ItemHitRadius = 0.55f, ChestHitRadius = 0.9f, CacheHitRadius = 0.6f;
        private readonly List<int> _missedTargets = new List<int>();

        private sealed class GalleryState
        {
            public GalleryRound Round;
            public GalleryKind Kind;
            public byte Serial;
            public int Layer = 1;
            public int StartTick, EndTick;
            public float Yaw0, Pitch0;
            /// <summary>Opened outside a shot (the starting gun): its aim and start are taken from the next input.</summary>
            public bool AimPending;
            public LootItem[] Items;
            public uint ColorSeed;
            public int Subject = -1;
            /// <summary>Where the player stood when it opened (an item that can't be equipped drops here).</summary>
            public float3 At;
            /// <summary>A bank gallery's essence: what each target holds, and what was banked and lost.</summary>
            public BankRun Bank;
            public bool Open => Round != null && !Round.Over;
        }

        private enum ShotObjectKind : byte
        {
            None,
            Box,
            Item,
            Chest,
            Capsule,
            /// <summary>A friend's flying capsule: it refills theirs and the shooter's.</summary>
            FriendCapsule,
            Orb,
            Cache,
            BankOrb,
            Gate,
            Portal,
            Door,
        }

        private struct ShotObject
        {
            public ShotObjectKind Kind;
            public int Box;
            public Entity Item;
            public int Id;
            public EssenceColor Color;
            public float At;
            /// <summary>Whose capsule a FriendCapsule is.</summary>
            public PlayerState Owner;
        }

        private static int GalleryLayer() => ExpeditionWorld.Active ? ExpeditionWorld.Layer : 1;

        /// <summary>Opens a gallery for this player (any open one is replaced).</summary>
        private void OpenGallery(PlayerState s, GalleryKind kind, float yaw, float pitch, bool aimPending, LootItem[] items = null,
            uint colorSeed = 0, int subject = -1, int targets = 0)
        {
            int layer = GalleryLayer();
            var g = s.Gallery;
            g.Kind = kind;
            g.Layer = layer;
            g.Round = new GalleryRound(GalleryDifficulty.For(kind, layer, targets), NextSeed() | 1u);
            g.Serial = ++s.GallerySerial;
            g.Yaw0 = yaw;
            g.Pitch0 = math.clamp(pitch, -GalleryPitchLimit, GalleryPitchLimit);
            g.AimPending = aimPending;
            g.StartTick = _tick;
            g.EndTick = 0;
            g.Items = items;
            g.ColorSeed = colorSeed;
            g.Subject = subject;
            g.At = new float3(s.Position.X, s.Position.Y, s.Position.Z);
            g.Bank = null;
            // A short pause, so the click that opened it can't also be a gallery shot.
            s.NextGalleryShot = _time + 0.2;
        }

        private static float GalleryTime(GalleryState g, int tick) => math.max(0f, GalleryClock.Seconds(tick - g.StartTick));

        /// <summary>
        /// A player's input this tick while their gallery is open: one shot per click, judged against the targets
        /// where the player saw them (<paramref name="seenTick"/>).
        /// </summary>
        private void GalleryInput(PlayerState s, ArenaInput input, bool fire, int seenTick, EntityCommandBuffer ecb)
        {
            var g = s.Gallery;
            if (g.AimPending)
            {
                g.AimPending = false;
                g.Yaw0 = input.Yaw;
                g.Pitch0 = math.clamp(input.Pitch, -GalleryPitchLimit, GalleryPitchLimit);
                g.StartTick = _tick;
            }
            bool click = fire && !s.FireHeld;
            s.FireHeld = fire;
            if (!click || _time < s.NextGalleryShot) return;
            s.NextGalleryShot = _time + GalleryDifficulty.ShotGap;
            float t = GalleryTime(g, seenTick);
            StepRound(s, t);
            if (!g.Round.Over)
            {
                float yaw = AimMath.DeltaAngle(g.Yaw0, input.Yaw);
                float pitch = input.Pitch - g.Pitch0;
                var shot = g.Round.Shoot(yaw, pitch, input.Pitch, t);
                if (shot.Counted && g.Bank != null)
                {
                    var wallet = WalletOf(s.Vitals.Id);
                    int n = wallet == null ? 0 : g.Bank.Hit(shot.Target, wallet);
                    if (n > 0) Banked(s.Vitals.Id, n);
                }
            }
            if (g.Round.Over) SettleGallery(s, ecb);
        }

        /// <summary>Moves a gallery on to time <paramref name="t"/>: bank targets that ran out lose their essence.</summary>
        private void StepRound(PlayerState s, float t)
        {
            var g = s.Gallery;
            _missedTargets.Clear();
            g.Round.Step(t, _missedTargets);
            if (g.Bank == null || _missedTargets.Count == 0) return;
            var wallet = WalletOf(s.Vitals.Id);
            if (wallet == null) return;
            foreach (int i in _missedTargets) g.Bank.Miss(i, wallet);
        }

        /// <summary>Time-outs and cancels, boss reward chests, and each player's gallery ghost.</summary>
        private void StepGalleries(EntityCommandBuffer ecb)
        {
            foreach (var kv in _players)
            {
                var s = kv.Value;
                var g = s.Gallery;
                if (g.Open && !g.AimPending)
                {
                    // A new layer ends it (not the starting gun, which waits for its pick).
                    if (g.Kind != GalleryKind.Starter && g.Layer != GalleryLayer()) g.Round.Cancel(GalleryTime(g, _tick));
                    else StepRound(s, GalleryTime(g, _tick - s.AimDelay));
                    if (g.Round.Over) SettleGallery(s, ecb);
                }
                PlaceChest(s);
                StepBotCapsule(s, ecb);
            }

            foreach (var (gallery, entity) in SystemAPI.Query<RefRW<ArenaGallery>>().WithAll<ArenaPlayer>().WithEntityAccess())
            {
                if (!_players.TryGetValue(entity, out var s)) continue;
                gallery.ValueRW = GalleryGhost(s);
            }
        }

        private ArenaGallery GalleryGhost(PlayerState s)
        {
            var g = s.Gallery;
            var ghost = new ArenaGallery { LootSeed = _lootSeed };
            if (g.Round != null && !g.AimPending)
            {
                var r = g.Round;
                ghost.Kind = (byte)g.Kind;
                ghost.Serial = g.Serial;
                ghost.Layer = (byte)g.Layer;
                ghost.Outcome = (byte)r.Outcome;
                ghost.Targets = (short)r.Count;
                ghost.Chosen = (short)r.Chosen;
                ghost.Seed = r.Seed;
                ghost.ColorSeed = g.ColorSeed;
                ghost.StartTick = g.StartTick;
                ghost.EndTick = g.EndTick;
                ghost.NextTick = g.StartTick + (int)math.round(r.NextSince * GalleryClock.TicksPerSecond);
                ghost.Yaw0 = g.Yaw0;
                ghost.Pitch0 = g.Pitch0;
                ghost.Hits = r.Hits;
                ghost.HitCount = (short)r.HitCount;
                ghost.Next = (short)r.Next;
                ghost.Subject = g.Subject;
                if (g.Items != null)
                {
                    ghost.Choice0 = g.Items.Length > 0 ? g.Items[0].Pack() : 0;
                    ghost.Choice1 = g.Items.Length > 1 ? g.Items[1].Pack() : 0;
                    ghost.Choice2 = g.Items.Length > 2 ? g.Items[2].Pack() : 0;
                }
            }
            if (s.ChestOut && s.ChestItems != null)
            {
                ghost.ChestOut = 1;
                ghost.ChestPos = s.ChestPos;
                ghost.ChestSeed = s.ChestSeed;
                ghost.Chest0 = s.ChestItems.Length > 0 ? s.ChestItems[0].Pack() : 0;
                ghost.Chest1 = s.ChestItems.Length > 1 ? s.ChestItems[1].Pack() : 0;
                ghost.Chest2 = s.ChestItems.Length > 2 ? s.ChestItems[2].Pack() : 0;
            }
            return ghost;
        }

        /// <summary>A gallery just ended: do what it was for.</summary>
        private void SettleGallery(PlayerState s, EntityCommandBuffer ecb)
        {
            var g = s.Gallery;
            g.EndTick = _tick;
            s.NextObjectShot = _time + 0.6;
            bool won = g.Round.Outcome == GalleryOutcome.Won;
            int id = s.Vitals.Id;
            switch (g.Kind)
            {
                case GalleryKind.Starter:
                    if (!won || g.Items == null || g.Round.Chosen < 0) break;
                    var gun = g.Items[g.Round.Chosen].Gun;
                    s.Loadout = new Loadout(gun);
                    Refit(s);
                    UnityEngine.Debug.Log($"[Gallery] player {id} picked {gun.Frame} with {gun.Payload}");
                    break;
                case GalleryKind.Box:
                case GalleryKind.Reward:
                    if (!won || g.Items == null || g.Round.Chosen < 0)
                    {
                        UnityEngine.Debug.Log($"[Gallery] player {id} missed a {(g.Kind == GalleryKind.Box ? "loot box" : "boss reward")}: its items are gone");
                        break;
                    }
                    var item = g.Items[g.Round.Chosen];
                    if (g.Kind == GalleryKind.Box && !s.BotKit) _usage.BoxOpened();
                    if (!Take(s, item, g.At, ecb)) SpawnItem(item, id, ExpeditionWorld.Wrap(g.At + new float3(0f, 1.1f, 0f)), PersonalItemLife);
                    UnityEngine.Debug.Log($"[Gallery] player {id} took {item} from a {(g.Kind == GalleryKind.Box ? "loot box" : "boss reward")}");
                    break;
                case GalleryKind.Cache:
                    if (won) PickUpCache(g.Subject, id);
                    break;
                case GalleryKind.Bank:
                    if (g.Bank != null)
                    {
                        _bankLost += g.Bank.Lost;
                        UnityEngine.Debug.Log($"[Gallery] player {id} banked {g.Bank.Banked} {g.Bank.Color}, lost {g.Bank.Lost}");
                    }
                    break;
                case GalleryKind.Gate:
                    if (won) UnlockGate(id);
                    break;
                case GalleryKind.Travel:
                    if (!won) break;
                    if (g.Subject == 1) TryGoUp(id);
                    else PassGate(id);
                    break;
                case GalleryKind.RaidStart:
                    if (won && _raid != null && _raid.Ready && _raid.Begin(_time)) AnnounceGather(id);
                    break;
            }
            _galleries++;
            if (won) _galleriesWon++;
        }

        private int _galleries, _galleriesWon, _bankLost;

        /// <summary>A waiting boss reward lands as a chest beside its player (not inside the raid), one at a time.</summary>
        private void PlaceChest(PlayerState s)
        {
            if (s.BotKit || s.Loadout == null || InsideRaid(s.Vitals.Id)) return;
            var feet = new float3(s.Position.X, s.Position.Y, s.Position.Z);
            if (s.ChestOut)
            {
                var near = ExpeditionWorld.Near(s.ChestPos, feet);
                if (math.distancesq(near, feet) <= ChestFollowRange * ChestFollowRange) return;
            }
            else
            {
                if (s.Rewards.Count == 0 || s.Gallery.Open) return;
                s.ChestItems = s.Rewards.Dequeue();
                s.ChestSeed = NextSeed() | 1u;
                s.ChestOut = true;
            }
            float yaw = math.radians(s.LastYaw);
            var p = feet + new float3(math.sin(yaw), 0f, math.cos(yaw)) * 3f;
            if (ExpeditionWorld.Active)
            {
                p = ExpeditionWorld.Wrap(p);
                p.y = ExpeditionTerrain.GroundY(p.x, p.z);
            }
            s.ChestPos = p;
        }

        /// <summary>
        /// The nearest world object a human's shot meets before <paramref name="range"/>: their flying payload capsule
        /// (where it was at <paramref name="seenTime"/>, when they saw it), an unopened loot box, an item they may take,
        /// or their reward chest. Not while a gallery is open (shots go to the targets then).
        /// </summary>
        private ShotObject WorldObjectOnShot(PlayerState s, float3 eye, float3 dir, float range, double seenTime)
        {
            var best = new ShotObject { At = range };
            int id = s.Vitals.Id;
            if (CapsuleAt(s, seenTime, out var capsule, out float capsuleRadius))
            {
                var c = ExpeditionWorld.Near(capsule, eye);
                if (RaySphere(eye, dir, c, capsuleRadius, best.At, out float at)) best = new ShotObject { Kind = ShotObjectKind.Capsule, At = at };
            }
            // Friends' capsules (bots' too): shooting one reloads you both.
            foreach (var other in _players.Values)
            {
                if (other == s || !CapsuleAt(other, seenTime, out var theirs, out float theirRadius)) continue;
                var c = ExpeditionWorld.Near(theirs, eye);
                if (RaySphere(eye, dir, c, theirRadius, best.At, out float at))
                    best = new ShotObject { Kind = ShotObjectKind.FriendCapsule, Owner = other, At = at };
            }
            if (_boxes != null && ExpeditionWorld.Active)
            {
                foreach (var b in _boxes)
                {
                    var c = ExpeditionWorld.Near(BoxPosition(b), eye) + new float3(0f, 0.4f, 0f);
                    if (!RaySphere(eye, dir, c, BoxHitRadius, best.At, out float at) || _boxLedger.HasOpened(b.Id, id)) continue;
                    best = new ShotObject { Kind = ShotObjectKind.Box, Box = b.Id, At = at };
                }
            }
            foreach (var kv in _items)
            {
                if (kv.Value.Owner != 0 && kv.Value.Owner != id) continue;
                var c = ExpeditionWorld.Near(kv.Value.Position, eye);
                if (!RaySphere(eye, dir, c, ItemHitRadius, best.At, out float at)) continue;
                best = new ShotObject { Kind = ShotObjectKind.Item, Item = kv.Key, At = at };
            }
            if (s.ChestOut)
            {
                var c = ExpeditionWorld.Near(s.ChestPos, eye) + new float3(0f, 0.5f, 0f);
                if (RaySphere(eye, dir, c, ChestHitRadius, best.At, out float at)) best = new ShotObject { Kind = ShotObjectKind.Chest, At = at };
            }
            if (OrbOnShot(id, eye, dir, best.At, out var orb, out float orbAt)) best = new ShotObject { Kind = ShotObjectKind.Orb, Item = orb, At = orbAt };
            if (ExpeditionWorld.Active && _map != null && !InsideRaid(id)) ExpeditionObjectsOnShot(s, eye, dir, ref best);
            return best;
        }

        /// <summary>Caches, the bank crystal's orbs, the gate, the up-portal and the raid door.</summary>
        private void ExpeditionObjectsOnShot(PlayerState s, float3 eye, float3 dir, ref ShotObject best)
        {
            int id = s.Vitals.Id;
            float at;
            if (_caches != null)
            {
                foreach (var cache in _caches.All)
                {
                    float y = ExpeditionTerrain.GroundY(cache.Position.X, cache.Position.Y) + 0.7f;
                    var c = ExpeditionWorld.Near(new float3(cache.Position.X, y, cache.Position.Y), eye);
                    if (RaySphere(eye, dir, c, CacheHitRadius, best.At, out at)) best = new ShotObject { Kind = ShotObjectKind.Cache, Id = cache.Id, At = at };
                }
            }
            var wallet = WalletOf(id);
            if (wallet != null && _banking != null && _banking.CanBank(id, _time))
            {
                foreach (var site in ShotTargets.BankSites(_map))
                {
                    if (!ShotTargets.Bank(site, out var crystal)) continue;
                    foreach (var color in EssenceColors.All)
                    {
                        if (wallet.Carried(color) <= 0) continue;
                        var c = ExpeditionWorld.Near(ShotTargets.BankOrb(crystal, color), eye);
                        if (RaySphere(eye, dir, c, ShotTargets.BankOrbRadius, best.At, out at))
                            best = new ShotObject { Kind = ShotObjectKind.BankOrb, Color = color, At = at };
                    }
                }
            }
            if (_gate != null && ShotTargets.Gate(_gate.Site.Point.World, out var gate) &&
                RaySphere(eye, dir, ExpeditionWorld.Near(gate, eye), ShotTargets.GateRadius, best.At, out at))
                best = new ShotObject { Kind = ShotObjectKind.Gate, At = at };
            if (LayerTravel.TryPortal(_map, ExpeditionWorld.Layer, out var portalSpot) && ShotTargets.Portal(portalSpot, out var portal) &&
                RaySphere(eye, dir, ExpeditionWorld.Near(portal, eye), ShotTargets.PortalRadius, best.At, out at))
                best = new ShotObject { Kind = ShotObjectKind.Portal, At = at };
            if (_raid != null && ShotTargets.Door(RaidDoorPoint, out var door) &&
                RaySphere(eye, dir, ExpeditionWorld.Near(door, eye), ShotTargets.DoorRadius, best.At, out at))
                best = new ShotObject { Kind = ShotObjectKind.Door, At = at };
        }

        /// <summary>Where a ray first meets a sphere, if before <paramref name="maxRange"/>.</summary>
        private static bool RaySphere(float3 origin, float3 dir, float3 center, float radius, float maxRange, out float at)
        {
            at = 0f;
            var oc = center - origin;
            float along = math.dot(oc, dir);
            if (along < 0f || along - radius > maxRange) return false;
            float miss = math.lengthsq(oc) - along * along;
            if (miss > radius * radius) return false;
            at = along - math.sqrt(radius * radius - miss);
            return at < maxRange;
        }

        /// <summary>A shot met a world object: open the box's or chest's gallery, or take the item.</summary>
        private void ShootObject(PlayerState s, ShotObject obj, float yaw, float pitch, EntityCommandBuffer ecb)
        {
            int id = s.Vitals.Id;
            var feet = new float3(s.Position.X, s.Position.Y, s.Position.Z);
            switch (obj.Kind)
            {
                case ShotObjectKind.Box:
                    // One shot: the box is spent as its gallery opens, so a miss loses its items.
                    if (!_boxLedger.TryOpen(obj.Box, id)) return;
                    _boxesOpened++;
                    uint roll = LootBoxLedger.RollSeed(_lootSeed, obj.Box, id);
                    OpenGallery(s, GalleryKind.Box, yaw, pitch, false, LootRolls.BoxChoices(roll), roll, obj.Box);
                    if (_connections.TryGetValue(id, out var connection))
                    {
                        var rpc = ecb.CreateEntity();
                        ecb.AddComponent(rpc, new LootBoxOpened { BoxId = obj.Box });
                        ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                    }
                    break;
                case ShotObjectKind.Chest:
                    s.ChestOut = false;
                    OpenGallery(s, GalleryKind.Reward, yaw, pitch, false, s.ChestItems, s.ChestSeed);
                    s.ChestItems = null;
                    break;
                case ShotObjectKind.Capsule:
                    CatchCapsule(s, ecb);
                    break;
                case ShotObjectKind.FriendCapsule:
                    ShootFriendCapsule(s, obj.Owner, ecb);
                    break;
                case ShotObjectKind.Item:
                    if (!_items.TryGetValue(obj.Item, out var item) || !Take(s, item.Item, feet, ecb)) return;
                    _items.Remove(obj.Item);
                    ecb.DestroyEntity(obj.Item);
                    break;
                case ShotObjectKind.Orb:
                    ShootOrb(s, obj.Item, ecb);
                    break;
                case ShotObjectKind.Cache:
                    OpenGallery(s, GalleryKind.Cache, yaw, pitch, false, subject: obj.Id);
                    break;
                case ShotObjectKind.BankOrb:
                {
                    var wallet = WalletOf(id);
                    if (wallet == null || !_banking.CanBank(id, _time) || wallet.Carried(obj.Color) <= 0) return;
                    var run = new BankRun(obj.Color, wallet.Carried(obj.Color));
                    OpenGallery(s, GalleryKind.Bank, yaw, pitch, false, subject: (int)obj.Color, targets: run.Targets);
                    s.Gallery.Bank = run;
                    break;
                }
                case ShotObjectKind.Gate:
                {
                    var wallet = WalletOf(id);
                    if (_gate.IsOpen && _gate.HasAccess(id)) OpenGallery(s, GalleryKind.Travel, yaw, pitch, false, subject: 0);
                    else if (wallet != null && !_gate.HasAccess(id) && _gate.Shortfall(wallet) == 0) OpenGallery(s, GalleryKind.Gate, yaw, pitch, false);
                    break;
                }
                case ShotObjectKind.Portal:
                    if (_travel != null && !_travel.Pending && RaidAllowsTravel) OpenGallery(s, GalleryKind.Travel, yaw, pitch, false, subject: 1);
                    break;
                case ShotObjectKind.Door:
                    if (_raid.Ready && !RaidArena.InRaidSpace(feet)) OpenGallery(s, GalleryKind.RaidStart, yaw, pitch, false);
                    break;
            }
        }

        private string GalleryLog() => $"galleries={_galleries} galleriesWon={_galleriesWon} bankLost={_bankLost}";
    }
}
