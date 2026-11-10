using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Social;
using FriendlyPersonShooter.Net;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Layers 2 and 3 on the server (see <see cref="LayerTravel"/>): the open gate on layers 1 and 2 and the
    /// up-portal by the start on layers 2 and 3 start a countdown for the whole group, then everyone moves.
    /// A layer change swaps the map and everything built from it (spawns, blooms, gate, banks, raid doors,
    /// the Colossus) and keeps what the players own (carried essence, the party bank, the raid key, kudos).
    /// </summary>
    public partial class ArenaServerSystem
    {
        private LayerTravel _travel;
        private int _layerMoves;

        /// <summary>Debug: set to a layer number to move the group there at once (going up lands at the gate).</summary>
        public static int DebugLayer;

        /// <summary>No moving while a raid gathers, runs or celebrates: the raiders would be left behind.</summary>
        private bool RaidAllowsTravel => _raid == null || _raid.Phase == RaidPhase.Collecting;

        private void StepLayers(EntityCommandBuffer ecb)
        {
            if (DebugLayer != 0)
            {
                int target = math.clamp(DebugLayer, 1, LayerSettings.LayerCount);
                DebugLayer = 0;
                var arrival = target < ExpeditionWorld.Layer ? LayerArrival.Gate : LayerArrival.Start;
                _travel = new LayerTravel(target);
                ChangeLayer(ecb, target, arrival);
                return;
            }

            if (_travel.Pending && !RaidAllowsTravel)
            {
                _travel.Cancel();
                _announce.Add((AnnounceKind.LayerTravel, 0, 0, 0));
            }

            // The up-portal: a bot standing in it starts the move up (players shoot it: ArenaServerGallery).
            if (!_travel.Pending && RaidAllowsTravel && LayerTravel.TryPortal(_map, ExpeditionWorld.Layer, out var portal))
            {
                foreach (var kv in _players)
                {
                    if (kv.Value.Vitals.IsDown || !kv.Value.BotKit) continue;
                    var pos = ExpeditionWorld.Ground(EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(kv.Key).Position);
                    if (_map.Distance(portal, pos) > LayerTravel.PortalRadius) continue;
                    if (_travel.BeginUp(_time, kv.Value.Vitals.Id))
                    {
                        _announce.Add((AnnounceKind.LayerTravel, (int)LayerTravel.Countdown, kv.Value.Vitals.Id, _travel.Target));
                    }
                    break;
                }
            }

            if (_travel.Step(_time)) ChangeLayer(ecb, _travel.Layer, _travel.Arrival);
        }

        /// <summary>The up-portal was shot (or a bot stood in it): the group heads up.</summary>
        private bool TryGoUp(int id)
        {
            if (_travel == null || _travel.Pending || !RaidAllowsTravel || !LayerTravel.TryPortal(_map, ExpeditionWorld.Layer, out _)) return false;
            if (!_travel.BeginUp(_time, id)) return false;
            _announce.Add((AnnounceKind.LayerTravel, (int)LayerTravel.Countdown, id, _travel.Target));
            return true;
        }

        /// <summary>Someone went through the open gate: the group heads down (false on the deepest layer).</summary>
        private bool TryGoDown(int id)
        {
            if (_travel == null || !_travel.CanGoDown) return false;
            if (_gate.HasAccess(id) && RaidAllowsTravel && _travel.BeginDown(_time, id))
            {
                _announce.Add((AnnounceKind.LayerTravel, (int)LayerTravel.Countdown, id, _travel.Target));
            }
            return true;
        }

        private void ChangeLayer(EntityCommandBuffer ecb, int layer, LayerArrival arrival)
        {
            int from = ExpeditionWorld.Layer;
            ExpeditionWorld.Layer = layer;
            _layerMoves++;

            // Everything living on the old map goes, except raid enemies (none while a raid is idle) .
            _scratch.Clear();
            foreach (var kv in _enemies) if (!kv.Value.Raid) _scratch.Add(kv.Key);
            foreach (var e in _scratch)
            {
                var s = _enemies[e];
                _enemies.Remove(e);
                _enemyById.Remove(s.Vitals.Id);
                _lobby.Combos.Forget(s.Vitals.Id, false);
                _ledger.Forget(s.Vitals.Id);
                ecb.DestroyEntity(e);
            }
            _groupEnds.Clear();
            foreach (var kv in _items) ecb.DestroyEntity(kv.Key);
            _items.Clear();
            ClearOrbs(ecb);

            // Dropped caches go home to their owners rather than being lost on the old map.
            _intScratch.Clear();
            foreach (var c in _caches.All) _intScratch.Add(c.Id);
            foreach (int cacheId in _intScratch)
            {
                var c = _caches.Find(cacheId);
                if (c == null) continue;
                var pickup = _caches.PickUp(cacheId, c.OwnerId, _time);
                if (pickup.Delivers && _wallets.TryGetValue(c.OwnerId, out var owner)) pickup.DeliverTo(owner);
            }

            // The new map and everything built from it.
            _map = ExpeditionWorld.Map;
            var settings = ExpeditionWorld.Settings;
            _blooms = new BloomSchedule(_map, settings.Blooms());
            _schedule = new SpawnSchedule(_map, _blooms, settings.Spawns());
            _gate = new Gate(_map.Gates[0], LayerSettings.SeedFor(NetTestSettings.WorldSeed, layer), settings.Gate());
            _chat = new QuickChatChannel(_map);
            _doors = new RaidDoorRotation(ExpeditionWorld.RaidDoors.Count);
            ExpeditionWorld.RaidDoorIndex = _doors.Active;
            ExpeditionWorld.RaidDoorsSpent = 0;
            RaidArena.Ensure();
            _world = new WorldBoss(null, _time);
            _banking = new Banking(_map);
            foreach (var door in ExpeditionWorld.RaidDoors) _banking.AddSite(door, _raid.Settings.UseRadius);
            foreach (var g in _map.Gates) _banking.AddSite(g.Point.World, ExpeditionWorld.GateUseRadius);
            // Coming back up, the group already went through this gate: it lets them back down for free.
            if (arrival == LayerArrival.Gate) foreach (int member in _wallets.Keys) _gate.GrantAccess(member);

            // Runs keep their clock across layers.
            var started = new Dictionary<int, double>();
            foreach (var kv in _trackers) started[kv.Key] = kv.Value.StartedAt;
            _trackers.Clear();
            foreach (var kv in started) _trackers[kv.Key] = new ExpeditionTracker(_map, _gate, WalletOf(kv.Key), kv.Value, _blooms, null, _banking);

            // Everyone moves together.
            var random = new Unity.Mathematics.Random(NextSeed() | 1u);
            var ids = new List<int>(_playerById.Keys);
            foreach (int id in ids)
            {
                float3 spot;
                if (arrival == LayerArrival.Start) spot = ExpeditionWorld.StartSpawn(ref random);
                else
                {
                    var at = LayerTravel.GateArrival(_map);
                    var o = random.NextFloat2Direction() * random.NextFloat(0f, 2.5f);
                    spot = ExpeditionTerrain.OnGround(ExpeditionWorld.Wrap(new float3(at.X + o.x, 0f, at.Y + o.y)));
                }
                TeleportPlayer(id, spot + new float3(0f, 0.2f, 0f));
            }
            _nextSpawnCheck = _time + 1.0;

            _announce.Add((AnnounceKind.LayerArrived, layer, 0, from));
            Debug.Log($"[Expedition] layer {from} -> {layer} ({arrival}): seed={_map.Seed} start={_map.StartZone} gate={_map.Gates[0].Point.Zone} " +
                      $"gate cost {_gate.Cost} {_gate.Color}, {ids.Count} player(s) moved");
        }

        private string LayerLog() =>
            $"layer={ExpeditionWorld.Layer} moves={_layerMoves}{(_travel != null && _travel.Pending ? $" moving to {_travel.Target} in {_travel.SecondsLeft(_time):0}s" : "")}";
    }
}
