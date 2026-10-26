using Protocol;
using System;
using System.Collections.Generic;

namespace Server
{
    public class PacketDispatcher
    {
        private readonly Dictionary<PacketId, Action<ReadOnlyMemory<byte>>> _packetHandlers = new();

        public void Register(PacketId id, Action<ReadOnlyMemory<byte>> action)
        {
            _packetHandlers[id] = action;
        }

        public void UnRegister(PacketId id)
        {
            _packetHandlers.Remove(id);
        }

        public void Initialize()
        {
            _packetHandlers.Clear();
        }

        public void Dispatch(PacketId id, ReadOnlyMemory<byte> data)
        {
            ServerManager.Instance.PushJob(() => InvokeJob(id, data));
        }

        /// <summary>
        /// 유니티 스레드에서만 실행
        /// </summary>
        private void InvokeJob(PacketId id, ReadOnlyMemory<byte> data)
        {
            PacketId wireId = id;
            id = NormalizeMoneyPacketId(id);
            // Log actual receipt independently of handler registration; never replace a gameplay handler.
            if (id == PacketId.S_ServeDish)
            {
                var packet = PacketSerializer.Deserialize<ServeDishPacket>(data);
                UnityEngine.Debug.Log($"<color=#87CEFA>[Submit RX] S_ServeDish / ServeDishPacket {{ EntityId={packet.EntityId} }} handlerRegistered={_packetHandlers.ContainsKey(id)}</color>");
            }
            else if (id == PacketId.S_DishState)
            {
                var packet = PacketSerializer.Deserialize<DishStatePacket>(data);
                UnityEngine.Debug.Log($"<color=#87CEFA>[Submit RX] S_DishState / DishStatePacket {{ RecipeId={packet.RecipeId}, State={packet.State} ({(int)packet.State}) }} handlerRegistered={_packetHandlers.ContainsKey(id)}</color>");
            }
            else if (id == PacketId.S_OpenDoor)
            {
                var packet = PacketSerializer.Deserialize<OpenDoorPacket>(data);
                UnityEngine.Debug.Log($"[Stage Return RX] S_OpenDoor door={packet.DoorId}, handlerRegistered={_packetHandlers.ContainsKey(id)}");
            }
            else if (id == PacketId.S_StageStop)
            {
                var packet = PacketSerializer.Deserialize<StageStopPacket>(data);
                UnityEngine.Debug.Log($"[Stage Return RX] S_StageStop earned={packet.PendingMoney}, wallet={packet.Money}, success={packet.IsSuccess}, handlerRegistered={_packetHandlers.ContainsKey(id)}");
            }
            else if (id == PacketId.S_RoomPendingMoney)
            {
                var packet = PacketSerializer.Deserialize<RoomPendingMoneyPacket>(data);
                UnityEngine.Debug.Log($"[Money RX] S_RoomPendingMoney wireId={(ushort)wireId}, total={packet.Money}, handlerRegistered={_packetHandlers.ContainsKey(id)}");
            }
            else if (id == PacketId.S_RoomMoney)
            {
                var packet = PacketSerializer.Deserialize<RoomMoneyPacket>(data);
                UnityEngine.Debug.Log($"[Money RX] S_RoomMoney wireId={(ushort)wireId}, wallet={packet.Money}, handlerRegistered={_packetHandlers.ContainsKey(id)}");
            }
            if (_packetHandlers.TryGetValue(id, out var handler))
            {
                handler.Invoke(data);
            }
        }

        private static PacketId NormalizeMoneyPacketId(PacketId id)
        {
            // Unity client compatibility: the shared serializer sends even server-only
            // money IDs (406/408) as 407/409. Accept both forms at receipt.
            ushort wireId = (ushort)id;
            ushort walletId = (ushort)PacketId.S_RoomMoney;
            ushort pendingId = (ushort)PacketId.S_RoomPendingMoney;
            if ((walletId & 1) == 0 && wireId == walletId + 1) return PacketId.S_RoomMoney;
            if ((pendingId & 1) == 0 && wireId == pendingId + 1) return PacketId.S_RoomPendingMoney;
            return id;
        }
    }
}
