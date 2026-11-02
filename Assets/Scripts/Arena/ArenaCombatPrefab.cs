using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Net;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Adds the combat components to the arena player prefab.</summary>
    public static class ArenaCombatPrefab
    {
        public static void AddPlayerCombat(EntityManager em, Entity prefab)
        {
            var settings = new CombatSettings();
            em.AddComponentData(prefab, new ArenaVitals { Health = settings.PlayerMaxHealth, MaxHealth = settings.PlayerMaxHealth });
            em.AddComponent<ArenaBuffs>(prefab);
            em.AddComponentData(prefab, new ArenaLoadout { Payload = PayloadKind.Vigor });
            // Lets the server rewind other ghosts to what this player saw when they fired.
            em.AddComponent<CommandDataInterpolationDelay>(prefab);
        }
    }

    /// <summary>Builds the enemy ghost prefab in code, identically in every arena world.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ArenaEnemyPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Enabled = false;
            if (NetTestSettings.Game != GameMode.Arena || SystemAPI.HasSingleton<ArenaEnemyPrefab>()) return;

            var prefab = EntityManager.CreateEntity();
            EntityManager.AddComponentData(prefab, LocalTransform.Identity);
            EntityManager.AddComponent<ArenaEnemy>(prefab);
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, prefab, new GhostPrefabCreation.Config
            {
                Name = "ArenaEnemy",
                Importance = 5,
                SupportedGhostModes = GhostModeMask.Interpolated,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Dynamic,
                UsePreSerialization = false,
            });
            var holder = EntityManager.CreateEntity();
            EntityManager.AddComponentData(holder, new ArenaEnemyPrefab { Value = prefab });
        }
    }
}
