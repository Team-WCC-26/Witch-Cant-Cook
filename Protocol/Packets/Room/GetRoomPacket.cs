using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_GetRoom)]
[PacketId(PacketId.S_GetRoom)]
public partial class GetRoomPacket
{
    public List<RoomData> RoomDatas { get; set; } = new();
}

[MemoryPackable]
public partial struct RoomData
{
    public string Id;
    public string Name;
    public bool BIsPrivate;
}
