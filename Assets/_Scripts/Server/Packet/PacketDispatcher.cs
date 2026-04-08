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

        public void Dispatch(PacketId id, ReadOnlyMemory<byte> data)
        {
            ServerManager.Instance.PushJob(() => InvokeJob(id, data));
        }

        /// <summary>
        /// 유니티 스레드에서만 실행
        /// </summary>
        private void InvokeJob(PacketId id, ReadOnlyMemory<byte> data)
        {
            if (_packetHandlers.TryGetValue(id, out var handler))
            {
                handler.Invoke(data);
            }
        }
    }
}
