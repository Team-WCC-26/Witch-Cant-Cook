using Protocol;

namespace Server;

public class ToolHandler : PacketHandlerBase
{
    public static void SpawnTool(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ToolSpawnPacket>(package.Body);
        var room = session.Player.Room;
        var tool = room.GenerateTool(packet.ToolId);

        packet.EntityId = tool.EntityId;
        tool.Position = packet.Position;
        tool.Rotation = packet.Quaternion;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
