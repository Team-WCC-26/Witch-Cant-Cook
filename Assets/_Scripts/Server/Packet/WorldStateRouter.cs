using MemoryPack;
using Protocol;
using System;
using System.Collections.Generic;

namespace Server
{
    public class WorldStateRouter
    {
        public Action<IReadOnlyList<PingResultPacket>> OnPing;
        public Action<IReadOnlyList<EntityPickupPacket>> OnEntityPickup;
        public Action<IReadOnlyList<EntityChangeParentPacket>> OnEntityInserted;
        public Action<IReadOnlyList<EntityDestroyPacket>> OnEntityDestroyed;
        public Action<IReadOnlyList<PlayerMovementPacket>> OnPlayerMoved;
        public Action<IReadOnlyList<CookCompletePacket>> OnCookCompleted;
        public Action<IReadOnlyList<CookProcessPacket>> OnCookProcessChanged;

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
            OnEntityPickup?.Invoke(packet.PickupEntities);
            OnEntityInserted?.Invoke(packet.ParentChangedEntities);
            OnEntityDestroyed?.Invoke(packet.DestroyedEntities);
            OnPlayerMoved?.Invoke(packet.Players);
            OnCookCompleted?.Invoke(packet.CookCompleteIngredients);
            OnCookProcessChanged?.Invoke(packet.CookProcessIngredients);
        }

        ~WorldStateRouter()
        {
            ServerManager.Instance.UnRegisterHandler(_worldStateId);
        }
    }
}
