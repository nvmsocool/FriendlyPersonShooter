using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Leveling and skills on the server (XP for kills, buffs, banking and revives; skill points spent through
    /// <see cref="SkillBuyRequest"/>), enemy attack effects for clients, and Glimmers: small, harmless,
    /// short-lived targets that pop up near each player and drop a little Yellow, so there is always
    /// something to shoot.
    /// </summary>
    public partial class ArenaServerSystem
    {
        private const float GlimmerLife = 6f;
        private const float GlimmerRadius = 0.45f;
        private const float GlimmerCheckInterval = 1.5f;
        private const float GlimmerNearRadius = 30f;
        private const int GlimmersNearPlayer = 2;

        private readonly List<Entity> _expired = new List<Entity>();
        private double _nextGlimmerCheck;
        private int _glimmersSpawned, _levelUps;

        // ---------- Progress ----------

        private void AddXp(int playerId, int amount)
        {
            if (amount <= 0 || !_playerById.TryGetValue(playerId, out var e) || !_players.TryGetValue(e, out var state)) return;
            int gained = state.Progress.AddXp(amount);
            if (gained <= 0) return;
            _levelUps += gained;
            _announce.Add((AnnounceKind.LevelUp, state.Progress.Level, playerId, 0));
        }

        /// <summary>Second Wind: health back over time once nothing has hurt the player for a while.</summary>
        private void StepRegen(PlayerState state, float dt)
        {
            float regen = state.Progress.RegenPerSecond;
            if (regen <= 0f || state.Vitals.IsDown || state.Revive != null || _time - state.LastHurt < SkillTree.RegenDelay) return;
            state.Vitals.Heal(regen * dt, out _);
        }

        private void WriteProgress(Entity entity, PlayerState state)
        {
            if (!SystemAPI.HasComponent<ArenaProgress>(entity)) return;
            var p = state.Progress;
            SystemAPI.SetComponent(entity, new ArenaProgress
            {
                Level = p.Level, Xp = p.Xp, XpNext = p.XpToNext, Points = p.Points, Ranks = p.PackedRanks,
            });
        }

        private void StepSkillRequests()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, source, e) in SystemAPI.Query<RefRO<SkillBuyRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int id = EntityManager.GetComponentData<NetworkId>(source.ValueRO.SourceConnection).Value;
                if (!_playerById.TryGetValue(id, out var pe) || !_players.TryGetValue(pe, out var state)) continue;
                var skill = (SkillId)request.ValueRO.Skill;
                if (!state.Progress.TryBuy(skill)) continue;
                state.Progress.ApplyTo(state.Vitals, _combat.PlayerMaxHealth);
            }
            ecb.Playback(EntityManager);
        }

        // ---------- Enemy attack effects ----------

        private void SendAttackFx(EntityCommandBuffer ecb, EnemyFamily family, float damage, Vector3 from, PlayerState victim, int boltId = 0)
        {
            var to = ToFloat3(victim.Position) + new float3(0f, ArenaWeapon.PlayerHitHeight, 0f);
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new EnemyAttackFx
            {
                VictimNetworkId = victim.Vitals.Id, Family = family, Damage = damage, BoltId = boltId,
                From = ExpeditionWorld.Near(ToFloat3(from), to), To = to,
            });
            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }

        // ---------- Glimmers ----------

        private Vector3 GlimmerPosition(EnemyState s)
        {
            float bob = 0.35f * math.sin(2.5f * (float)(_time - s.Born) + s.Phase);
            return s.Home + new Vector3(0f, bob, 0f);
        }

        /// <summary>Every so often, a Glimmer near each player who has fewer than two close by.</summary>
        private void StepGlimmers(EntityCommandBuffer ecb)
        {
            if (_time < _nextGlimmerCheck) return;
            _nextGlimmerCheck = _time + GlimmerCheckInterval;
            foreach (var kv in _players)
            {
                if (kv.Value.Vitals.IsDown) continue;
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                int near = 0;
                foreach (var en in _enemies)
                {
                    if (en.Value.Family != EnemyFamily.Glimmer) continue;
                    var gp = ExpeditionWorld.Near(ToFloat3(en.Value.Home), pos);
                    if (math.distancesq(gp.xz, pos.xz) <= GlimmerNearRadius * GlimmerNearRadius) near++;
                }
                if (near >= GlimmersNearPlayer) continue;

                var random = new Unity.Mathematics.Random(NextSeed() | 1u);
                var offset = random.NextFloat2Direction() * random.NextFloat(10f, 22f);
                var spot = ExpeditionWorld.Wrap(new float3(pos.x + offset.x, 0f, pos.z + offset.y));
                // Only over ground that is built, and not far above or below the player (no Glimmers inside cliffs or down pits).
                float ground = ExpeditionTerrain.Active ? ExpeditionTerrain.GroundY(spot.x, spot.z, float.NaN) : 0f;
                if (float.IsNaN(ground) || math.abs(ground - pos.y) > 8f) continue;
                var state = new EnemyState
                {
                    Family = EnemyFamily.Glimmer,
                    Home = new Vector3(spot.x, ground + random.NextFloat(1f, 2.5f), spot.z),
                    Born = _time,
                    Phase = random.NextFloat(0f, 6.28f),
                };
                SpawnEnemy(state, state.Home);
                _spawned.Clear();
                _glimmersSpawned++;
            }
        }

        /// <summary>Glimmers nobody shot in time fade away, with no reward.</summary>
        private void RemoveExpiredGlimmers(EntityCommandBuffer ecb)
        {
            foreach (var e in _expired)
            {
                if (!_enemies.TryGetValue(e, out var s)) continue;
                _enemies.Remove(e);
                _enemyById.Remove(s.Vitals.Id);
                _lobby.Combos.Forget(s.Vitals.Id, false);
                _ledger?.Forget(s.Vitals.Id);
                ecb.DestroyEntity(e);
            }
            _expired.Clear();
        }
    }
}
