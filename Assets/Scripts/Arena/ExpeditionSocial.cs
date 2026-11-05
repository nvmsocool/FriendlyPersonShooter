using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Social;
using FriendlyPersonShooter.Core.World;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.InputSystem;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Client-to-server: say a quick-chat line. The server checks it and passes it to everyone nearby.</summary>
    public struct QuickChatRequest : IRpcCommand
    {
        public byte Id;
        public byte Color;
        public int LandmarkId;
        public int PlayerId;
        /// <summary>A <see cref="ZoneCoord.Key"/>.</summary>
        public int Zone;
    }

    /// <summary>Server-to-listener: a quick-chat line someone nearby said. Each client renders it from its own copy of the map.</summary>
    public struct QuickChatLine : IRpcCommand
    {
        public int SenderId;
        public byte Id;
        public byte Color;
        public int LandmarkId;
        public int PlayerId;
        /// <summary>A <see cref="ZoneCoord.Key"/>.</summary>
        public int Zone;
    }

    /// <summary>Client-to-server: give kudos to a player (0 = whoever helped me last).</summary>
    public struct KudosRequest : IRpcCommand
    {
        public int ReceiverId;
    }

    /// <summary>Server-to-both: a kudos was given (or why not, for the giver).</summary>
    public struct KudosNotice : IRpcCommand
    {
        public int GiverId;
        public int ReceiverId;
        public KudosOutcome Outcome;
    }

    public static class QuickChatWire
    {
        public static QuickChatMessage ToMessage(byte id, byte color, int landmark, int player, int zone) =>
            new QuickChatMessage((QuickChatId)id, (EssenceColor)color, landmark, player, ZoneCoord.FromKey(zone));

        public static QuickChatRequest ToRequest(QuickChatMessage m) => new QuickChatRequest
        {
            Id = (byte)m.Id, Color = (byte)m.Color, LandmarkId = m.LandmarkId, PlayerId = m.PlayerId, Zone = m.Zone.Key,
        };
    }

    /// <summary>
    /// Client side of the expedition's social layer (MVP step 5):
    /// - The human says quick-chat lines with the number keys and gives kudos with K.
    /// - Bot allies call for a res when downed and say thanks (and give kudos) after a revive.
    /// - Incoming lines and kudos go to <see cref="ArenaFx"/> for the HUD.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ExpeditionSocialClientSystem : SystemBase
    {
        public static readonly List<(int Sender, QuickChatMessage Message, float Time)> Lines = new List<(int, QuickChatMessage, float)>();
        public static readonly List<(KudosNotice Notice, float Time)> Kudos = new List<(KudosNotice, float)>();

        private bool _wasDown;
        private EntityQuery _local;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
            _local = GetEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<ArenaVitals>());
        }

        protected override void OnUpdate()
        {
            if (!ExpeditionWorld.Active)
            {
                Enabled = false;
                return;
            }
            bool bot = SystemAPI.HasSingleton<ArenaBotSeat>();
            float now = UnityEngine.Time.unscaledTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (line, e) in SystemAPI.Query<RefRO<QuickChatLine>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                if (bot) continue;
                var l = line.ValueRO;
                Lines.Add((l.SenderId, QuickChatWire.ToMessage(l.Id, l.Color, l.LandmarkId, l.PlayerId, l.Zone), now));
                if (Lines.Count > 20) Lines.RemoveAt(0);
            }
            foreach (var (notice, e) in SystemAPI.Query<RefRO<KudosNotice>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                if (!bot) Kudos.Add((notice.ValueRO, now));
            }

            if (_local.CalculateEntityCount() == 1)
            {
                using var transforms = _local.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var vitals = _local.ToComponentDataArray<ArenaVitals>(Allocator.Temp);
                var pos = ExpeditionWorld.Ground(transforms[0].Position);
                bool down = vitals[0].Health <= 0f && vitals[0].MaxHealth > 0f;
                if (bot) BotSocial(ecb, down, vitals[0]);
                else HumanSocial(ecb, pos);
                _wasDown = down;
            }
            ecb.Playback(EntityManager);
        }

        private void HumanSocial(EntityCommandBuffer ecb, System.Numerics.Vector2 pos)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var map = ExpeditionWorld.Map;
            QuickChatMessage? say = null;
            if (keyboard.digit1Key.wasPressedThisFrame) say = QuickChatMessage.Simple(QuickChatId.Thanks);
            if (keyboard.digit2Key.wasPressedThisFrame) say = QuickChatMessage.Simple(QuickChatId.NeedRevive);
            if (keyboard.digit3Key.wasPressedThisFrame) say = QuickChatMessage.SeenAny(EssenceColor.Yellow);
            if (keyboard.digit4Key.wasPressedThisFrame) say = QuickChatMessage.ColorPast(EssenceColor.Yellow, map.NearestLandmark(pos).Id);
            if (keyboard.digit5Key.wasPressedThisFrame) say = QuickChatMessage.Simple(QuickChatId.FollowMe);
            if (keyboard.digit6Key.wasPressedThisFrame) say = QuickChatMessage.Simple(QuickChatId.GroupUp);
            if (keyboard.digit7Key.wasPressedThisFrame) say = QuickChatMessage.GateAt(map.NearestLandmark(map.Gates[0].Point.World).Id);
            if (say.HasValue) Send(ecb, say.Value);
            if (keyboard.kKey.wasPressedThisFrame) SendKudos(ecb, 0);
        }

        /// <summary>Bots: "Need a res!" when they go down; "Thanks!" and a kudos for whoever helped when they're back up.</summary>
        private void BotSocial(EntityCommandBuffer ecb, bool down, ArenaVitals vitals)
        {
            if (down && !_wasDown) Send(ecb, QuickChatMessage.Simple(QuickChatId.NeedRevive));
            if (!down && _wasDown && vitals.Health < vitals.MaxHealth)
            {
                // Back up with partial health: revived by someone (bleeding out respawns at full health).
                Send(ecb, QuickChatMessage.Simple(QuickChatId.Thanks));
                SendKudos(ecb, 0);
            }
        }

        private static void Send(EntityCommandBuffer ecb, QuickChatMessage message)
        {
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, QuickChatWire.ToRequest(message));
            ecb.AddComponent<SendRpcCommandRequest>(rpc);
        }

        private static void SendKudos(EntityCommandBuffer ecb, int receiver)
        {
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new KudosRequest { ReceiverId = receiver });
            ecb.AddComponent<SendRpcCommandRequest>(rpc);
        }
    }
}
