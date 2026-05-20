using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_WorldState)]
public partial class WorldStatePacket
{
    public int Tick { get; set; }
    public List<PingResultPacket> Pings { get; set; } = new();
    public List<PlayerMovementPacket> Players { get; set; } = new();
    public List<IngredientMovementStatePacket> Ingredients { get; set; } = new();
}
