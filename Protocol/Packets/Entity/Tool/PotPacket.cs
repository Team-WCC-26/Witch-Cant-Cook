using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_PotForceEject)]
public partial class PotForceEjectPacket
{
    public long PotEntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_PotPlayerEnter)]
[PacketId(PacketId.S_PotPlayerEnter)]
public partial class PotPlayerEnterPacket
{
    public long PotEntityId { get; set; }
    public string PlayerId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_PotEject)]
public partial class PotEjectPacket
{
    public long PotEntityId { get; set; }
    public int EjectSeed { get; set; }
    public long EjectDelayMs { get; set; }
    public List<string> PlayerIds { get; set; } = new();
    public List<long> EntityIds { get; set; } = new();
}

[MemoryPackable]
[PacketId(PacketId.S_PotCookComplete)]
public partial class PotCookCompletePacket
{
    public long PotEntityId { get; set; }
    public long DishEntityId { get; set; }
    public int ResultIngredientId { get; set; }
}
