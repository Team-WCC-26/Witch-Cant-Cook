using MemoryPack;
using Protocol;
using System;
using System.Collections.Generic;

namespace Server
{
    public class WorldStateRouter
    {
        public Action<IReadOnlyList<PingResultPacket>> OnPing;
        public Action<IReadOnlyList<PlayerMovementPacket>> OnPlayer;
        public Action<IReadOnlyList<IngredientMovementStatePacket>> OnIngredient;

        private PacketId _worldStateId => PacketId.S_WorldState;

        public void Initialize()
        {
            ServerManager.Instance.RegisterHandler(_worldStateId, Handle);

            //OnPing = null;
            //OnPlayer = null;
            //OnIngredient = null;
        }

        private void Handle(ReadOnlyMemory<byte> data)
        {
            var packet = MemoryPackSerializer.Deserialize<WorldStatePacket>(data.Span);

            OnPing?.Invoke(packet.Pings);
            OnPlayer?.Invoke(packet.Players);
            OnIngredient?.Invoke(packet.IngredientMovements);
        }

        ~WorldStateRouter()
        {
            ServerManager.Instance.UnRegisterHandler(_worldStateId);
        }
    }
}
