using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_InteractDoor)]
public partial class InteractDoorPacket
{
    public DoorId DoorId { get; set; }
    public string PlayerId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_StopInteractDoor)]
public partial class StopInteractDoorPacket
{
    public DoorId DoorId { get; set; }
    public string PlayerId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_OpenDoor)]
public partial class OpenDoorPacket
{
    public DoorId DoorId { get; set; }
}

public enum DoorId
{
    Lobby,
    Kitchen
}
