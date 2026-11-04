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
            em.AddComponentData(prefab, new ArenaBuffs { MoveMultiplier = 1f, JumpMultiplier = 1f });
            em.AddComponentData(prefab, new ArenaLoadout { Payload = PayloadKind.None, Reload = 1f });
            em.AddComponentData(prefab, new ArenaProgress { Level = 1, XpNext = FriendlyPersonShooter.Core.Progression.SkillTree.XpToNext(1) });
            if (ExpeditionWorld.Active) em.AddComponent<ExpeditionStatus>(prefab);
            // Lets the server rewind other ghosts to what this player saw when they fired.
            em.AddComponent<CommandDataInterpolationDelay>(prefab);
        }
    }

    /// <summary>Builds the enemy and soul fragment ghost prefabs in code, identically in every arena world.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ArenaEnemyPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Enabled = false;
            if (!NetTestSettings.IsFirstPerson || SystemAPI.HasSingleton<ArenaEnemyPrefab>()) return;

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

            var fragment = EntityManager.CreateEntity();
            EntityManager.AddComponentData(fragment, LocalTransform.Identity);
            EntityManager.AddComponent<ArenaFragment>(fragment);
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, fragment, new GhostPrefabCreation.Config
            {
                Name = "ArenaFragment",
                Importance = 8,
                SupportedGhostModes = GhostModeMask.Interpolated,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Dynamic,
                UsePreSerialization = false,
            });
            EntityManager.AddComponentData(holder, new ArenaFragmentPrefab { Value = fragment });

            var cache = EntityManager.CreateEntity();
            EntityManager.AddComponentData(cache, LocalTransform.Identity);
            EntityManager.AddComponent<ArenaCache>(cache);
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, cache, new GhostPrefabCreation.Config
            {
                Name = "ArenaCache",
                Importance = 2,
                SupportedGhostModes = GhostModeMask.Interpolated,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Static,
                UsePreSerialization = false,
            });
            EntityManager.AddComponentData(holder, new ArenaCachePrefab { Value = cache });

            var item = EntityManager.CreateEntity();
            EntityManager.AddComponentData(item, LocalTransform.Identity);
            EntityManager.AddComponent<ArenaGroundItem>(item);
            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, item, new GhostPrefabCreation.Config
            {
                Name = "ArenaGroundItem",
                Importance = 3,
                SupportedGhostModes = GhostModeMask.Interpolated,
                DefaultGhostMode = GhostMode.Interpolated,
                OptimizationMode = GhostOptimizationMode.Static,
                UsePreSerialization = false,
            });
            EntityManager.AddComponentData(holder, new ArenaGroundItemPrefab { Value = item });
        }
    }
}
