using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Builds the player ghost prefab in code, identically in the server, client and bot worlds,
    /// so no SubScene or baked prefab is needed for the test.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PlayerPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Enabled = false;
            if (SystemAPI.HasSingleton<PlayerPrefab>()) return;

            var prefab = EntityManager.CreateEntity();
            EntityManager.AddComponentData(prefab, LocalTransform.Identity);
            EntityManager.AddComponent<NetPlayer>(prefab);
            EntityManager.AddComponent<PlayerInput>(prefab);
            EntityManager.AddBuffer<InputBufferData<PlayerInput>>(prefab);
            EntityManager.AddComponent<GhostOwner>(prefab);
            EntityManager.AddComponent<GhostOwnerIsLocal>(prefab);
            EntityManager.AddComponentData(prefab, new AutoCommandTarget { Enabled = true });

            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, prefab, new GhostPrefabCreation.Config
            {
                Name = "NetPlayer",
                Importance = 1,
                SupportedGhostModes = GhostModeMask.All,
                DefaultGhostMode = GhostMode.OwnerPredicted,
                OptimizationMode = GhostOptimizationMode.Dynamic,
                UsePreSerialization = false,
            });

            var holder = EntityManager.CreateEntity();
            EntityManager.AddComponentData(holder, new PlayerPrefab { Value = prefab });
        }
    }
}
