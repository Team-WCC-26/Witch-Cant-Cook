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
            if (_packetHandlers.TryGetValue(id, out var handler))
            {
                handler.Invoke(data);
            }
        }
    }
}
