using Protocol;

namespace Server;

public class ToolHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_ToolSpawn)]
    public static void SpawnTool(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ToolSpawnPacket>(package.Body);
        var room = session.Player.Room;
        var tool = room.GenerateTool(packet.ToolId, out var entityId);

        packet.EntityId = entityId;
        tool.Position = packet.Position;
        tool.Rotation = packet.Quaternion;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_ToolRegister)]
    public static void RegisterTool(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ToolRegisterPacket>(package.Body);
        var room = session.Player.Room;
        var tool = room.GenerateTool(packet.ToolId, out var entityId);

        packet.EntityId = entityId;
        tool.Position = packet.Position;
        tool.Rotation = packet.Quaternion;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
