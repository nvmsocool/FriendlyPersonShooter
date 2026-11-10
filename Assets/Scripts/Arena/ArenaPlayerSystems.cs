using FriendlyPersonShooter.Net;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Builds the arena player ghost prefab in code, identically in every world, and tells the
    /// server to spawn players near the middle of the arena.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ArenaPrefabSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Enabled = false;
            if (SystemAPI.HasSingleton<PlayerPrefab>()) return;

            var prefab = EntityManager.CreateEntity();
            EntityManager.AddComponentData(prefab, LocalTransform.Identity);
            EntityManager.AddComponent<ArenaPlayer>(prefab);
            EntityManager.AddComponent<ArenaInput>(prefab);
            EntityManager.AddBuffer<InputBufferData<ArenaInput>>(prefab);
            EntityManager.AddComponent<ArenaLook>(prefab);
            EntityManager.AddComponentData(prefab, new ArenaMotion { Grounded = true });
            EntityManager.AddComponent<GhostOwner>(prefab);
            EntityManager.AddComponent<GhostOwnerIsLocal>(prefab);
            EntityManager.AddComponentData(prefab, new AutoCommandTarget { Enabled = true });
            ArenaCombatPrefab.AddPlayerCombat(EntityManager, prefab);

            GhostPrefabCreation.ConvertToGhostPrefab(EntityManager, prefab, new GhostPrefabCreation.Config
            {
                Name = "ArenaPlayer",
                Importance = 10,
                SupportedGhostModes = GhostModeMask.All,
                DefaultGhostMode = GhostMode.OwnerPredicted,
                OptimizationMode = GhostOptimizationMode.Dynamic,
                UsePreSerialization = false,
            });

            var holder = EntityManager.CreateEntity();
            EntityManager.AddComponentData(holder, new PlayerPrefab { Value = prefab });

            if (World.IsServer())
            {
                var random = new Unity.Mathematics.Random(0xA7E4A1u);
                ServerAccounts.SpawnOverride = () =>
                {
                    if (ExpeditionWorld.Active) return ExpeditionTerrain.OnGround(ExpeditionWorld.StartSpawn(ref random));
                    var p = random.NextFloat2Direction() * random.NextFloat(2f, 8f);
                    return new float3(p.x, 0f, p.y - 10f);
                };
            }
        }

        protected override void OnDestroy()
        {
            if (World.IsServer()) ServerAccounts.SpawnOverride = null;
        }
    }

    /// <summary>
    /// Moves arena players from their input: walk relative to the look direction, jump, fall, and
    /// stay inside the arena. Runs on the server and, predicted, on the owning client.
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ArenaMovementSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            float limit = ArenaSettings.HalfSize - ArenaSettings.BodyRadius;

            foreach (var (input, look, motion, transform, vitals, buffs) in SystemAPI
                         .Query<RefRO<ArenaInput>, RefRW<ArenaLook>, RefRW<ArenaMotion>, RefRW<LocalTransform>,
                             RefRO<ArenaVitals>, RefRO<ArenaBuffs>>()
                         .WithAll<ArenaPlayer, Simulate>())
            {
                var i = input.ValueRO;
                // Downed players lie still until revived or respawned.
                bool down = vitals.ValueRO.Health <= 0f && vitals.ValueRO.MaxHealth > 0f;
                float speedMul = math.max(buffs.ValueRO.MoveMultiplier, 0.1f);
                // Sprint only while moving forward and not aiming down the sights.
                if (i.Sprint != 0 && i.Vertical > 0 && i.Zoom == 0) speedMul *= ArenaSettings.SprintMultiplier;
                float jumpMul = math.max(buffs.ValueRO.JumpMultiplier, 0.1f);
                float gravity = ArenaSettings.Gravity * (buffs.ValueRO.GravityMultiplier > 0.05f ? buffs.ValueRO.GravityMultiplier : 1f);
                if (down)
                {
                    i.Horizontal = 0;
                    i.Vertical = 0;
                    i.Jump = default;
                }
                look.ValueRW.Yaw = i.Yaw;
                look.ValueRW.Pitch = math.clamp(i.Pitch, -85f, 85f);

                float yaw = math.radians(i.Yaw);
                var forward = new float3(math.sin(yaw), 0f, math.cos(yaw));
                var right = new float3(math.cos(yaw), 0f, -math.sin(yaw));
                var move = right * i.Horizontal + forward * i.Vertical;
                if (math.lengthsq(move) > 0f) move = math.normalize(move) * ArenaSettings.MoveSpeed * speedMul;

                ref var m = ref motion.ValueRW;
                if (m.Grounded && i.Jump.IsSet)
                {
                    m.VerticalSpeed = ArenaSettings.JumpSpeed * jumpMul;
                    m.Grounded = false;
                }
                if (ExpeditionTerrain.Active)
                {
                    // The generated zones: collide, climb slopes and steps, fall off ledges.
                    var p = transform.ValueRO.Position;
                    ExpeditionTerrain.Move(ref p, ref m, move, gravity, dt);
                    transform.ValueRW.Position = p;
                    continue;
                }
                m.VerticalSpeed -= gravity * dt;

                var pos = transform.ValueRO.Position + move * dt;
                pos.y += m.VerticalSpeed * dt;
                if (pos.y <= 0f)
                {
                    pos.y = 0f;
                    m.VerticalSpeed = 0f;
                    m.Grounded = true;
                }
                if (ExpeditionWorld.Active)
                {
                    pos = ExpeditionWorld.Wrap(pos);
                }
                else
                {
                    pos.x = math.clamp(pos.x, -limit, limit);
                    pos.z = math.clamp(pos.z, -limit, limit);
                }
                transform.ValueRW.Position = pos;
            }
        }
    }

    /// <summary>The human's input: mouse look, WASD, Shift to sprint, space to jump, left mouse to fire (every action is a shot: loot, essence, banking, gates and shooting galleries), right mouse to zoom, middle mouse to ping. Reloading is shooting your flung capsule.</summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ArenaLocalInputSystem : SystemBase
    {
        /// <summary>Degrees of turn per pixel of mouse movement.</summary>
        public static float Sensitivity = 0.08f;

        private float _yaw;
        private float _pitch;

        protected override void OnCreate()
        {
            RequireForUpdate<ArenaPlayer>();
        }

        protected override void OnUpdate()
        {
            // Bot ally worlds drive themselves.
            if (SystemAPI.HasSingleton<ArenaBotSeat>()) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            int h = 0, v = 0;
            bool jump = false, fire = false, zoom = false, sprint = false, ping = false;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed) h -= 1;
                if (keyboard.dKey.isPressed) h += 1;
                if (keyboard.sKey.isPressed) v -= 1;
                if (keyboard.wKey.isPressed) v += 1;
                jump = keyboard.spaceKey.wasPressedThisFrame;
                sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }
            if (mouse != null && locked)
            {
                var delta = mouse.delta.ReadValue();
                zoom = mouse.rightButton.isPressed;
                // Zoomed in, the mouse turns slower by the same factor, so aim feels the same on screen.
                float sens = Sensitivity / (zoom ? math.max(1f, ZoomFactor) : 1f);
                _yaw = (_yaw + delta.x * sens) % 360f;
                _pitch = math.clamp(_pitch + delta.y * sens, -85f, 85f);
                fire = mouse.leftButton.isPressed;
                ping = mouse.middleButton.wasPressedThisFrame;
            }
            LocalLook = new float2(_yaw, _pitch);
            Zooming = zoom;
            bool unstuck = RequestUnstuck;
            RequestUnstuck = false;

            foreach (var input in SystemAPI.Query<RefRW<ArenaInput>>().WithAll<GhostOwnerIsLocal, ArenaPlayer>())
            {
                var i = new ArenaInput { Horizontal = h, Vertical = v, Yaw = _yaw, Pitch = _pitch, Fire = fire ? 1 : 0, Zoom = zoom ? 1 : 0, Sprint = sprint ? 1 : 0 };
                if (jump) i.Jump.Set();
                if (unstuck) i.Unstuck.Set();
                if (ping) i.Ping.Set();
                input.ValueRW = i;
            }
        }

        /// <summary>Set by the Esc menu's "Reset position" button; sent with the next input.</summary>
        public static bool RequestUnstuck;

        /// <summary>The human's latest look angles (yaw, pitch), for a camera that doesn't wait for a tick.</summary>
        public static float2 LocalLook;

        /// <summary>True while the human holds right mouse; <see cref="ZoomFactor"/> is the gun in hand's zoom (set by the view).</summary>
        public static bool Zooming;
        public static float ZoomFactor = 1f;
    }
}
