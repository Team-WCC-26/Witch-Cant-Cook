using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_WorldState)]
public partial class WorldStatePacket
{
    public int Tick { get; set; }
    public List<PingResultPacket> Pings { get; set; } = new();
    public List<EntityPickupPacket> PickupEntities { get; set; } = new();
    public List<EntityChangeParentPacket> ParentChangedEntities { get; set; } = new();
    public List<EntityDestroyPacket> DestroyedEntities { get; set; } = new();
    public List<PlayerMovementPacket> Players { get; set; } = new();
    public List<CookCompletePacket> CookCompleteIngredients { get; set; } = new();
    public List<CookProcessPacket> CookProcessIngredients { get; set; } = new();
}
