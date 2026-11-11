using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Things you shoot to use them: your essence orbs, and a prompt when your crosshair is on the gate, its essence
    /// slots, the up-portal, the raid door's key slots or a cache.
    /// </summary>
    public partial class ArenaView
    {
        private const float ShootPromptRange = 80f;

        private EntityQuery _orbQuery;
        private Material[] _essenceMats;
        private readonly List<Matrix4x4>[] _essenceLists = new List<Matrix4x4>[EssenceColors.Count];

        private void CreateShootableQueries(EntityManager em)
        {
            _orbQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaEssenceOrb>(), ComponentType.ReadOnly<LocalTransform>());
        }

        private void EnsureShootableMaterials()
        {
            if (_essenceMats != null) return;
            _essenceMats = new Material[EssenceColors.Count];
            foreach (var c in EssenceColors.All)
            {
                ColorUtility.TryParseHtmlString(EssencePack.Hex(c), out var color);
                _essenceMats[(int)c] = MakeGlow(color, 0.45f);
                _essenceLists[(int)c] = new List<Matrix4x4>();
            }
        }

        /// <summary>Draws your orbs, and says what shooting the thing under your crosshair does.</summary>
        private void DrawShootables(World world, Entity local)
        {
            EnsureShootableMaterials();
            foreach (var l in _essenceLists) l.Clear();
            if (local == Entity.Null || !_wrap)
            {
                FlushShootables();
                return;
            }
            var em = world.EntityManager;
            int myId = em.GetComponentData<GhostOwner>(local).NetworkId;
            Vector3 eye = viewCamera != null ? viewCamera.transform.position : Vector3.zero;
            var dir = viewCamera != null ? (float3)viewCamera.transform.forward : new float3(0f, 0f, 1f);
            float reach = math.min(ShootPromptRange, ArenaLayout.RaycastCover(eye, dir, ShootPromptRange));
            float best = reach;
            string prompt = null;

            using (var orbs = _orbQuery.ToComponentDataArray<ArenaEssenceOrb>(Allocator.Temp))
            using (var at = _orbQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < orbs.Length; i++)
                {
                    if (orbs[i].Owner != myId || orbs[i].Color >= EssenceColors.Count) continue;
                    // A glowing ball the size of its hit sphere, rising as the server moves it.
                    Vector3 p = Near(at[i].Position);
                    _essenceLists[orbs[i].Color].Add(Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (ArenaServerSystem.OrbHitRadius * 2f)));
                }
            }

            if (TryGetStatus(out var status) && !status.InRaid)
            {
                var map = ExpeditionWorld.Map;
                if (ShotTargets.Gate(map.Gates.Count > 0 ? map.Gates[0].Point.World : default, out var gate) && map.Gates.Count > 0 &&
                    RayHits(eye, dir, Near(gate), ShotTargets.GateRadius, best, out float gd))
                {
                    best = gd;
                    prompt = status.Phase == ExpeditionPhase.PassThrough && status.LayerNext == 0
                        ? (ExpeditionWorld.Layer >= LayerSettings.LayerCount ? "Shoot the gate to start the countdown to the end" : "Shoot the gate to start the countdown down a layer")
                        : null;
                }
                if (LayerTravel.TryPortal(map, ExpeditionWorld.Layer, out var portalSpot) && ShotTargets.Portal(portalSpot, out var portal) &&
                    RayHits(eye, dir, Near(portal), ShotTargets.PortalRadius, best, out float pd))
                {
                    best = pd;
                    prompt = "Shoot the portal to head up a layer";
                }
                using (var caches = _cacheQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                {
                    foreach (var c in caches)
                    {
                        if (!RayHits(eye, dir, Near(c.Position) + Vector3.up * 0.4f, 0.6f, best, out float cd)) continue;
                        best = cd;
                        prompt = "Shoot the cache, then hit every target";
                    }
                }
                string slotPrompt = SlotPrompt(map, status, eye, dir, best);
                if (slotPrompt != null) prompt = slotPrompt;
            }
            if (prompt != null) _prompt = prompt;
            FlushShootables();
        }

        /// <summary>What shooting the essence slot under the crosshair would do (the slot takes the shot over the gate or door).</summary>
        private string SlotPrompt(LayerMap map, ExpeditionStatus status, float3 eye, float3 dir, float range)
        {
            if (map.Gates.Count > 0 && status.Phase < ExpeditionPhase.PassThrough && status.Goal > 0)
            {
                var site = map.Gates[0].Point.World;
                float3 foot = Near(ExpeditionWorld.ToFloat3(site, GroundAt(site)));
                if (EssenceSlots.GateOnShot(foot, status.Goal, eye, dir, range, out int k, out _))
                {
                    var color = status.GoalColor;
                    if (GateSlotLit(status, k)) return $"That slot is full: {status.Banked}/{status.Goal} {EssencePack.Named(color)} banked";
                    return status.Carried > 0
                        ? $"Shoot an empty slot to bank one {EssencePack.Named(color)} ({status.Carried} carried, {status.Banked}/{status.Goal} banked)"
                        : $"Bring {EssencePack.Named(color)} and shoot an empty slot to bank it ({status.Banked}/{status.Goal} banked)";
                }
            }
            if (status.RaidPhase == RaidPhase.Collecting)
            {
                var door = ExpeditionWorld.RaidDoor;
                float3 foot = Near(ExpeditionWorld.ToFloat3(door, GroundAt(door)));
                if (EssenceSlots.DoorOnShot(foot, eye, dir, range, out var color, out int k, out _))
                {
                    int have = EssencePack.Get(status.CarriedAll, color) + EssencePack.Get(status.BankedAll, color);
                    string key = $"{EssencePack.Get(status.Key, color)}/{RaidKey.Cost(color)}";
                    if (RaidKey.SlotLit(status.KeySlots, color, k)) return $"That slot is full: raid key {EssencePack.Named(color)} {key}";
                    return have > 0
                        ? $"Shoot an empty slot to put one {EssencePack.Named(color)} into the raid key ({key}; you have {have})"
                        : $"Bring {EssencePack.Named(color)} and shoot an empty slot to put it into the raid key ({key})";
                }
            }
            return null;
        }

        private void FlushShootables()
        {
            for (int i = 0; i < _essenceLists.Length; i++) DrawList(_essenceLists[i], _essenceMats[i], _sphere);
        }
    }
}
