using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_PlayerDamage)]
public partial class PlayerDamagePacket
{
    public string? PlayerId { get; set; }
    public int Damage { get; set; }
}

[MemoryPackable]
public partial class PlayerHpPacket
{
    public string? PlayerId { get; set; }
    public int Hp { get; set; }
}
