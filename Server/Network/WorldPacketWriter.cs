using Protocol;

namespace Server;

public class WorldPacketWriter
{
    public WorldStatePacket Packet;

    public void Init()
    {
        Packet = new();
    }
}

