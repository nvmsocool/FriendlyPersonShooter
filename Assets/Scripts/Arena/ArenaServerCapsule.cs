using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Gallery;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Payload capsules on the server: when a gun's capsule runs dry, the next shot flings it into the air
    /// (Core.Arsenal.CapsuleFlight); its owner shoots it for a full capsule, at the position they saw it. A friend
    /// can shoot it too, which fills both their capsules. Bots can't aim at it, so the server catches it for them by chance (by layer).
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>A bot's capsule comes back this long after it's launched, when the dice say they'd have hit it.</summary>
        private const float BotCatchDelay = 0.8f;

        private int _capsulesLaunched, _capsulesCaught, _friendCapsules;

        private void LaunchCapsule(PlayerState s, int networkId, float3 eye, float yaw, float pitch, PayloadKind payload, EntityCommandBuffer ecb)
        {
            int layer = GalleryLayer();
            var rng = new CombatRandom(NextSeed() | 1u);
            var from = eye + new float3(0f, 0.25f, 0f);
            s.Capsule = CapsuleFlight.Launch(ToVec(from), yaw, pitch, layer, ref rng);
            _capsulesLaunched++;
            s.BotCatchAt = s.BotKit && rng.Chance(GalleryDifficulty.BotSuccess(layer)) ? _time + BotCatchDelay : 0;
            SendCapsule(networkId, s, payload, false, ecb);
        }

        /// <summary>Where this player's capsule was at <paramref name="time"/>, if it's in the air and can be hit then.</summary>
        private bool CapsuleAt(PlayerState s, double time, out float3 position, out float radius)
        {
            position = default;
            radius = 0f;
            if (s.Loadout == null) return false;
            var st = s.Gun;
            if (st.Capsule != CapsuleState.Flying) return false;
            float t = (float)(time - st.CapsuleAt);
            if (!CapsuleFlight.Catchable(t)) return false;
            position = ToFloat3(s.Capsule.At(t));
            radius = s.Capsule.HitRadius;
            return true;
        }

        /// <summary>The capsule was shot: back on the gun, full.</summary>
        private void CatchCapsule(PlayerState s, EntityCommandBuffer ecb)
        {
            if (!FireControl.Catch(ref s.Gun, s.Stats)) return;
            _capsulesCaught++;
            SendCapsule(s.Vitals.Id, s, s.Loadout.InHand.Payload, true, ecb);
        }

        /// <summary>A friend shot <paramref name="owner"/>'s capsule out of the air: both capsules are full.</summary>
        private void ShootFriendCapsule(PlayerState shooter, PlayerState owner, EntityCommandBuffer ecb)
        {
            if (owner == null || owner.Loadout == null) return;
            owner.BotCatchAt = 0;
            CatchCapsule(owner, ecb);
            _friendCapsules++;
            if (shooter.Loadout == null) return;
            // The shooter's own capsule may be in the air too: it snaps back like a catch.
            if (shooter.Gun.Capsule == CapsuleState.Flying) CatchCapsule(shooter, ecb);
            else FireControl.Refill(ref shooter.Gun, shooter.Stats);
        }

        private void StepBotCapsule(PlayerState s, EntityCommandBuffer ecb)
        {
            if (s.BotCatchAt <= 0 || _time < s.BotCatchAt) return;
            s.BotCatchAt = 0;
            CatchCapsule(s, ecb);
        }

        private static void SendCapsule(int networkId, PlayerState s, PayloadKind payload, bool caught, EntityCommandBuffer ecb)
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent(e, new CapsuleFx
            {
                ShooterNetworkId = networkId, From = ToFloat3(s.Capsule.From), Velocity = ToFloat3(s.Capsule.Velocity),
                Radius = s.Capsule.HitRadius, Payload = payload, Caught = (byte)(caught ? 1 : 0),
            });
            ecb.AddComponent(e, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }

        private string CapsuleLog() => $"capsules={_capsulesLaunched} caught={_capsulesCaught} friend={_friendCapsules}";
    }
}
