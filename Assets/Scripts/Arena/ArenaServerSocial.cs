using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Social;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition's social layer on the server (MVP step 5): quick-chat to everyone within
    /// earshot, kudos (only after real help, see <see cref="KudosLedger"/>), and the step-5
    /// measurement: how many stranger encounters lead to an exchange (info, buff, revive, cache
    /// return). In this prototype every player counts as a stranger to every other.
    /// </summary>
    public partial class ArenaServerSystem
    {
        private const float EncounterRange = 30f;

        private QuickChatChannel _chat;
        private KudosLedger _kudos;
        private ExchangeTracker _exchanges;
        private readonly Dictionary<int, int> _lastHelper = new Dictionary<int, int>();
        private readonly List<ChatListener> _listeners = new List<ChatListener>();
        private int _chatLines, _kudosGiven;

        private void CreateSocial()
        {
            _chat = new QuickChatChannel(_map);
            _kudos = new KudosLedger();
            _exchanges = new ExchangeTracker();
        }

        private void NoteHelp(int helper, int helped, KudosReason reason, ExchangeKind kind)
        {
            if (_kudos == null || helper == helped) return;
            _kudos.NoteHelp(helper, helped, reason, _time);
            _exchanges.Exchange(helper, helped, kind, _time);
            _lastHelper[helped] = helper;
        }

        /// <summary>An accepted ally hit: a buff, or a chain link passed on.</summary>
        private void SocialBuff(int shooter, int target, HitResult result)
        {
            if (result.Refused != RefuseReason.None) return;
            bool chained = result.ChainEvent == ChainEvent.Started || result.ChainEvent == ChainEvent.Grew;
            NoteHelp(shooter, target, chained ? KudosReason.Chain : KudosReason.Buff, ExchangeKind.Buff);
        }

        /// <summary>A soul fragment shot: counts as revive help (the kudos opens up for every contributor once revived).</summary>
        private void SocialFragment(int shooter, int downed, ReviveState revive, bool revived)
        {
            if (_kudos == null) return;
            _exchanges.Exchange(shooter, downed, ExchangeKind.Revive, _time);
            if (!revived) return;
            foreach (var kv in revive.Contributions) NoteHelp(kv.Key, downed, KudosReason.Revive, ExchangeKind.Revive);
            _lastHelper[downed] = shooter;
        }

        private void SocialCacheReturn(int finder, int owner)
        {
            if (_kudos == null) return;
            _exchanges.Exchange(finder, owner, ExchangeKind.CacheReturn, _time);
            _kudos.RecordCacheReturn(finder, owner, _time);
            _lastHelper[owner] = finder;
        }

        /// <summary>Encounters, quick-chat requests and kudos requests.</summary>
        private void StepSocial(EntityCommandBuffer ecb)
        {
            var connectionOf = new Dictionary<int, Entity>();
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithEntityAccess()) connectionOf[id.ValueRO.Value] = connection;

            _listeners.Clear();
            foreach (var kv in _players)
            {
                var pos = EntityManager.GetComponentData<LocalTransform>(kv.Key).Position;
                _listeners.Add(new ChatListener(kv.Value.Vitals.Id, ExpeditionWorld.Ground(pos)));
            }
            for (int i = 0; i < _listeners.Count; i++)
            {
                for (int j = i + 1; j < _listeners.Count; j++)
                {
                    if (_map.Distance(_listeners[i].Position, _listeners[j].Position) <= EncounterRange)
                    {
                        _exchanges.Met(_listeners[i].PlayerId, _listeners[j].PlayerId, _time);
                    }
                }
            }

            foreach (var (request, source, e) in SystemAPI.Query<RefRO<QuickChatRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int sender = EntityManager.GetComponentData<NetworkId>(source.ValueRO.SourceConnection).Value;
                var r = request.ValueRO;
                var message = QuickChatWire.ToMessage(r.Id, r.Color, r.LandmarkId, r.PlayerId, r.ZoneX, r.ZoneY);
                var from = default(System.Numerics.Vector2);
                foreach (var l in _listeners) if (l.PlayerId == sender) from = l.Position;
                var result = _chat.Send(sender, from, message, _listeners, _time);
                if (!result.Delivered) continue;
                _chatLines++;
                _exchanges.RecordQuickChat(result);
                // Everyone in earshot, and the sender too so they see what they said.
                _intScratch.Clear();
                _intScratch.AddRange(result.Recipients);
                if (!_intScratch.Contains(sender)) _intScratch.Add(sender);
                foreach (int to in _intScratch)
                {
                    if (!connectionOf.TryGetValue(to, out var connection)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new QuickChatLine
                    {
                        SenderId = sender, Id = r.Id, Color = r.Color, LandmarkId = r.LandmarkId, PlayerId = r.PlayerId, ZoneX = r.ZoneX, ZoneY = r.ZoneY,
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }

            foreach (var (request, source, e) in SystemAPI.Query<RefRO<KudosRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                int giver = EntityManager.GetComponentData<NetworkId>(source.ValueRO.SourceConnection).Value;
                int receiver = request.ValueRO.ReceiverId;
                if (receiver == 0 && !_lastHelper.TryGetValue(giver, out receiver)) receiver = 0;
                var outcome = receiver == 0 ? KudosOutcome.NoInteraction : _kudos.Give(giver, receiver, _time);
                if (outcome == KudosOutcome.Given) _kudosGiven++;
                var notice = new KudosNotice { GiverId = giver, ReceiverId = receiver, Outcome = outcome };
                foreach (int to in new[] { giver, receiver })
                {
                    if (to == 0 || !connectionOf.TryGetValue(to, out var connection)) continue;
                    if (to == receiver && outcome != KudosOutcome.Given) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, notice);
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
        }

        private string SocialLog()
        {
            var report = _exchanges.Report(_time);
            return $"encounters={report.Encounters} withExchange={report.WithExchange} ({report.Share * 100:0}%) " +
                   $"info={report.CountOf(ExchangeKind.Info)} buff={report.CountOf(ExchangeKind.Buff)} revive={report.CountOf(ExchangeKind.Revive)} " +
                   $"cache={report.CountOf(ExchangeKind.CacheReturn)} chatLines={_chatLines} kudos={_kudosGiven}";
        }
    }
}
