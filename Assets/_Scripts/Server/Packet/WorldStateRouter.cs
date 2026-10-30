using MemoryPack;
using Protocol;
using System;
using System.Collections.Generic;

namespace Server
{
    public class WorldStateRouter
    {
        public event Action<IReadOnlyList<PingResultPacket>> OnPing;
        public event Action<IReadOnlyList<EntityPickupPacket>> OnEntityPickup;
        public event Action<IReadOnlyList<EntityChangeParentPacket>> OnEntityParentChanged;
        public event Action<IReadOnlyList<EntityDestroyPacket>> OnEntityDestroyed;
        public event Action<IReadOnlyList<PlayerMovementPacket>> OnPlayerMoved;
        public event Action<IReadOnlyList<PlayerHpPacket>> OnPlayerHealthChanged;
        public event Action<IReadOnlyList<CookCompletePacket>> OnCookCompleted;
        public event Action<IReadOnlyList<CookProcessPacket>> OnCookProcessChanged;

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

            // World state dispatch
            OnPing?.Invoke(packet.Pings);
            OnEntityPickup?.Invoke(packet.PickupEntities);
            OnEntityParentChanged?.Invoke(packet.ParentChangedEntities);
            OnEntityDestroyed?.Invoke(packet.DestroyedEntities);
            OnPlayerMoved?.Invoke(packet.Players);
            OnPlayerHealthChanged?.Invoke(packet.PlayerHps);
            OnCookCompleted?.Invoke(packet.CookCompleteIngredients);
            OnCookProcessChanged?.Invoke(packet.CookProcessIngredients);
        }

        ~WorldStateRouter()
        {
            ServerManager.Instance.UnRegisterHandler(_worldStateId);
        }
    }
}
