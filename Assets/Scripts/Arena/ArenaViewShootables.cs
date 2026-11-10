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
    /// Things you shoot to use them: your essence orbs, the bank crystal beside each gate and raid door (an orb per
    /// color you carry), and a prompt when your crosshair is on the gate, the up-portal, the raid door or a cache.
    /// </summary>
    public partial class ArenaView
    {
        private const float ShootPromptRange = 80f;

        private EntityQuery _orbQuery;
        private Material[] _essenceMats;
        private Material _crystalMat;
        private readonly List<Matrix4x4>[] _essenceLists = new List<Matrix4x4>[EssenceColors.Count];
        private readonly List<Matrix4x4> _crystals = new List<Matrix4x4>();

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
            _crystalMat = MakeGlow(new Color(0.75f, 0.9f, 1f), 1.1f);
        }

        /// <summary>Draws your orbs and the bank crystals, and says what shooting the thing under your crosshair does.</summary>
        private void DrawShootables(World world, Entity local)
        {
            EnsureShootableMaterials();
            foreach (var l in _essenceLists) l.Clear();
            _crystals.Clear();
            if (local == Entity.Null || !_wrap)
            {
                FlushShootables();
                return;
            }
            var em = world.EntityManager;
            int myId = em.GetComponentData<GhostOwner>(local).NetworkId;
            float now = Time.time;
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
                float spin = now * 50f;
                foreach (var site in ShotTargets.BankSites(map))
                {
                    if (!ShotTargets.Bank(site, out var crystalAt)) continue;
                    Vector3 c = Near(crystalAt);
                    if ((c - eye).sqrMagnitude > 250f * 250f) continue;
                    _crystals.Add(Matrix4x4.TRS(c, Quaternion.Euler(0f, spin, 0f), new Vector3(0.7f, 1.6f, 0.7f)));
                    _crystals.Add(Matrix4x4.TRS(c - Vector3.up * (ShotTargets.BankHeight * 0.5f), Quaternion.identity, new Vector3(0.3f, ShotTargets.BankHeight, 0.3f)));
                    foreach (var color in EssenceColors.All)
                    {
                        int carried = EssencePack.Get(status.CarriedAll, color);
                        if (carried <= 0) continue;
                        Vector3 o = Near(ShotTargets.BankOrb(crystalAt, color));
                        _essenceLists[(int)color].Add(Matrix4x4.TRS(o, Quaternion.Euler(0f, spin * 2f, 0f), Vector3.one * (ShotTargets.BankOrbRadius * 2f)));
                        if (RayHits(eye, dir, o, ShotTargets.BankOrbRadius, best, out float d))
                        {
                            best = d;
                            prompt = $"Shoot to bank your {EssencePack.Named(color)} ({carried}): hit each target as it appears. A missed target's essence is lost.";
                        }
                    }
                }

                if (ShotTargets.Gate(map.Gates.Count > 0 ? map.Gates[0].Point.World : default, out var gate) && map.Gates.Count > 0 &&
                    RayHits(eye, dir, Near(gate), ShotTargets.GateRadius, best, out float gd))
                {
                    best = gd;
                    prompt = status.Phase >= ExpeditionPhase.PassThrough
                        ? (ExpeditionWorld.Layer >= LayerSettings.LayerCount ? "Shoot the gate to finish the expedition" : "Shoot the gate to head down a layer")
                        : "Shoot the gate to unlock it (when the party bank can pay)";
                }
                if (LayerTravel.TryPortal(map, ExpeditionWorld.Layer, out var portalSpot) && ShotTargets.Portal(portalSpot, out var portal) &&
                    RayHits(eye, dir, Near(portal), ShotTargets.PortalRadius, best, out float pd))
                {
                    best = pd;
                    prompt = "Shoot the portal to head up a layer";
                }
                if (status.RaidPhase == RaidPhase.Collecting && RaidKey.Full(status.Key) && ShotTargets.Door(ExpeditionWorld.RaidDoor, out var door) &&
                    RayHits(eye, dir, Near(door), ShotTargets.DoorRadius, best, out float dd))
                {
                    best = dd;
                    prompt = "Shoot the raid door to start the countdown";
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
            }
            if (prompt != null) _prompt = prompt;
            FlushShootables();
        }

        private void FlushShootables()
        {
            for (int i = 0; i < _essenceLists.Length; i++) DrawList(_essenceLists[i], _essenceMats[i], _sphere);
            DrawList(_crystals, _crystalMat);
        }
    }
}
