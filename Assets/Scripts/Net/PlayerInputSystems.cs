using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;

namespace FriendlyPersonShooter.Net
{
    /// <summary>The human tester's input: WASD or arrow keys move the local player.</summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class LocalPlayerInputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var keyboard = Keyboard.current;
            int h = 0, v = 0;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1;
            }

            foreach (var input in SystemAPI.Query<RefRW<PlayerInput>>().WithAll<GhostOwnerIsLocal, NetPlayer>())
            {
                input.ValueRW = new PlayerInput { Horizontal = h, Vertical = v };
            }
        }
    }
}
