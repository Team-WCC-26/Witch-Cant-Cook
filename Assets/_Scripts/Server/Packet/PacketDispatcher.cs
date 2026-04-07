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
            if (_packetHandlers.TryGetValue(id, out var handler))
            {
                ServerManager.Instance.PushJob(() => handler.Invoke(data));
            }
        }
    }
}
