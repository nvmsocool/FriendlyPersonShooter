using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Avatars;
using Unity.Entities;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Players as goofy floating robots (see <see cref="RobotMeshes"/>): they hover and bob, lean into their
    /// movement, look where they aim with a wobbly head, and hold their gun in two floating hands.
    /// Downed robots lie on their backs. Your own robot shows only as the hands on your gun.
    /// </summary>
    public partial class ArenaView
    {
        /// <summary>Height of the robot's body centre above the feet.</summary>
        private const float RobotBodyHeight = 0.95f;
        /// <summary>Guns held by robots are drawn at this scale (the first-person gun is smaller and closer).</summary>
        private const float RobotGunScale = 1.4f;

        private Material _robotMat;
        private readonly Dictionary<long, RobotParts> _robotParts = new Dictionary<long, RobotParts>();
        private Dictionary<Entity, (Vector3 Pos, Vector3 Vel)> _robotMotion = new Dictionary<Entity, (Vector3, Vector3)>();
        private Dictionary<Entity, (Vector3 Pos, Vector3 Vel)> _robotMotionNext = new Dictionary<Entity, (Vector3, Vector3)>();
        /// <summary>Each robot's gun muzzle this frame, by network id, so their tracers start at the gun.</summary>
        private readonly Dictionary<int, Vector3> _robotMuzzle = new Dictionary<int, Vector3>();

        private void CreateRobotMaterials()
        {
            try
            {
                _robotMat = FriendlyPersonShooter.ZoneArt.ZoneLook.VertexColor("Robot", wrap: 0.45f, glow: 4f);
            }
            catch (System.ArgumentException e)
            {
                Debug.LogWarning("[Arena] Robot shader missing, robots use the plain material: " + e.Message);
                _robotMat = MakeMat(Color.white);
            }
        }

        private RobotParts RobotFor(uint seed, int colorIndex)
        {
            if (seed == 0) seed = 1;
            long key = (long)seed * 16 + colorIndex;
            if (_robotParts.TryGetValue(key, out var parts)) return parts;
            if (_robotParts.Count >= 64)
            {
                // Players come and go; drop the old meshes now and then.
                foreach (var p in _robotParts.Values) DestroyRobot(p);
                _robotParts.Clear();
            }
            parts = RobotMeshes.Build(seed, PlayerColors.Palette[colorIndex]);
            _robotParts[key] = parts;
            return parts;
        }

        private static void DestroyRobot(RobotParts p)
        {
            Destroy(p.Body);
            Destroy(p.Head);
            Destroy(p.Hand);
            if (p.Spinner != null) Destroy(p.Spinner);
        }

        private void RenderRobotMesh(Mesh mesh, Matrix4x4 m)
        {
            Graphics.RenderMesh(new RenderParams(_robotMat), mesh, 0, m);
        }

        /// <summary>Call once per frame before drawing robots; forgets players who left.</summary>
        private void BeginRobots()
        {
            var t = _robotMotion;
            _robotMotion = _robotMotionNext;
            _robotMotionNext = t;
            _robotMotionNext.Clear();
            _robotMuzzle.Clear();
        }

        /// <summary>One other player's robot, with their gun in its hands.</summary>
        private void DrawRobot(Entity entity, int networkId, Vector3 feet, float yaw, float pitch, uint seed, int colorIndex,
            ArenaLoadout? loadout, bool downed)
        {
            var parts = RobotFor(seed, colorIndex);
            var look = parts.Look;
            float t = Time.time;
            float dt = Time.deltaTime;

            // Smoothed velocity from the last frame's position, for leaning into movement.
            var vel = Vector3.zero;
            if (_robotMotion.TryGetValue(entity, out var prev) && dt > 0f)
            {
                var raw = (feet - prev.Pos) / dt;
                raw.y = 0f;
                // A teleport or a wrap across the world edge isn't movement.
                if (raw.sqrMagnitude > 900f) raw = prev.Vel;
                vel = Vector3.Lerp(prev.Vel, raw, 1f - Mathf.Exp(-dt * 8f));
            }
            _robotMotionNext[entity] = (feet, vel);

            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 bodyPos;
            Quaternion bodyRot, headRot;
            if (downed)
            {
                // Flat on its back, head lolling to one side.
                bodyPos = feet + Vector3.up * (parts.BodyRadius * 0.85f + 0.05f);
                bodyRot = yawRot * Quaternion.Euler(-82f, 0f, 18f);
                headRot = bodyRot * Quaternion.Euler(0f, 0f, 35f);
            }
            else
            {
                var local = Quaternion.Inverse(yawRot) * vel;
                float lean = Mathf.Clamp(local.z * 2.2f, -14f, 14f);
                float roll = Mathf.Clamp(-local.x * 2.2f, -12f, 12f);
                float bob = Mathf.Sin(t * 2.4f * look.BobSpeed + look.BobPhase) * 0.05f;
                bodyPos = feet + Vector3.up * (RobotBodyHeight + bob);
                bodyRot = yawRot * Quaternion.Euler(lean, 0f, roll);
                float wobble = Mathf.Sin(t * 3.1f * look.BobSpeed + look.BobPhase * 1.7f) * 4f;
                headRot = yawRot * Quaternion.Euler(-pitch * 0.6f + lean * 0.5f, 0f, look.HeadTilt + wobble);
            }

            var bodyM = Matrix4x4.TRS(bodyPos, bodyRot, Vector3.one);
            RenderRobotMesh(parts.Body, bodyM);
            var headM = Matrix4x4.TRS(bodyM.MultiplyPoint3x4(parts.HeadAt), headRot, Vector3.one);
            RenderRobotMesh(parts.Head, headM);
            if (parts.Spinner != null)
            {
                RenderRobotMesh(parts.Spinner, headM * Matrix4x4.TRS(parts.SpinnerAt, Quaternion.Euler(0f, t * (downed ? 90f : 900f), 0f), Vector3.one));
            }

            var gun = loadout.HasValue ? Gun.Unpack(loadout.Value.Active == 0 ? loadout.Value.GunA : loadout.Value.GunB) : default;
            if (!downed && !gun.IsEmpty)
            {
                // Held out in front of the right side of the chest, pointing where they look.
                var aim = Quaternion.Euler(-pitch, yaw, 0f);
                GripOf(gun.Frame, out var right, out var left);
                var anchor = bodyPos + yawRot * new Vector3(0.17f, -0.04f, parts.BodyRadius + 0.14f);
                var basis = Matrix4x4.TRS(anchor, aim, Vector3.one * RobotGunScale) * Matrix4x4.Translate(-right);
                AddGun(basis, gun.Frame, gun.Payload);
                AddHands(parts, basis, right, left, 1f / RobotGunScale);
                ShapeOf(gun.Frame, out var muzzle);
                _robotMuzzle[networkId] = basis.MultiplyPoint3x4(muzzle);
            }
            else
            {
                // Empty hands float at its sides (or lie beside it when downed).
                float side = parts.BodyRadius + 0.17f;
                for (int s = -1; s <= 1; s += 2)
                {
                    float bob = downed ? 0f : Mathf.Sin(t * 2.4f * look.BobSpeed + look.BobPhase + s) * 0.04f;
                    var at = bodyM.MultiplyPoint3x4(new Vector3(s * side, -0.05f + bob, 0.04f));
                    RenderRobotMesh(parts.Hand, Matrix4x4.TRS(at, bodyRot * Quaternion.Euler(70f, 0f, s * 15f), Vector3.one));
                }
            }
        }

        /// <summary>Both hands on a gun: right on the grip, left on the support point (gun-space points).</summary>
        private void AddHands(RobotParts parts, Matrix4x4 gunBasis, Vector3 right, Vector3 left, float handScale)
        {
            RenderRobotMesh(parts.Hand, gunBasis * Matrix4x4.TRS(right, Quaternion.Euler(10f, 0f, 0f), Vector3.one * handScale));
            RenderRobotMesh(parts.Hand, gunBasis * Matrix4x4.TRS(left, Quaternion.Euler(-5f, 0f, 20f), Vector3.one * handScale));
        }

        /// <summary>Your own hands on the first-person gun.</summary>
        private void AddHeldHands(uint seed, int colorIndex, FrameKind frame)
        {
            var parts = RobotFor(seed, colorIndex);
            GripOf(frame, out var right, out var left);
            AddHands(parts, _heldPose, right, left, 1f / RobotGunScale);
        }

        /// <summary>Where the hands hold each frame, in gun space: the trigger hand and the support hand.</summary>
        private static void GripOf(FrameKind frame, out Vector3 right, out Vector3 left)
        {
            switch (frame)
            {
                case FrameKind.Pistol:
                    right = new Vector3(0f, -0.07f, 0.02f);
                    left = new Vector3(-0.07f, -0.075f, 0.04f);
                    break;
                case FrameKind.Smg:
                    right = new Vector3(0f, -0.06f, 0f);
                    left = new Vector3(0f, -0.13f, 0.11f);
                    break;
                case FrameKind.Marksman:
                    right = new Vector3(0f, -0.06f, 0.04f);
                    left = new Vector3(0f, -0.05f, 0.4f);
                    break;
                case FrameKind.Beam:
                    right = new Vector3(0f, -0.09f, 0.04f);
                    left = new Vector3(0f, -0.075f, 0.25f);
                    break;
                case FrameKind.Launcher:
                    right = new Vector3(0f, -0.09f, 0.05f);
                    left = new Vector3(0f, -0.07f, 0.3f);
                    break;
                default:
                    right = new Vector3(0f, -0.06f, 0.06f);
                    left = new Vector3(0f, -0.05f, 0.33f);
                    break;
            }
        }

        /// <summary>Where a remote player's shot appears to leave from: their robot's muzzle when we drew one.</summary>
        private Vector3 ShotOrigin(int networkId, Vector3 serverFrom)
        {
            return _robotMuzzle.TryGetValue(networkId, out var m) ? m : serverFrom;
        }

        private static uint SeedOf(EntityManager em, Entity e, int networkId)
        {
            uint seed = em.HasComponent<ArenaAvatar>(e) ? em.GetComponentData<ArenaAvatar>(e).Seed : 0;
            return seed != 0 ? seed : RobotLook.SeedFrom((uint)networkId);
        }
    }
}
