using Protocol;

namespace Server;

public class LobbyHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_InteractLobbyDoor)]
    public static void InteractLobbyDoor(Session session, PacketPackageInfo package)
    {

    }

    [PacketHandler(PacketId.C_InteractKitchenDoor)]
    public static void InteractKitchenDoor(Session session, PacketPackageInfo package)
    {

    }
}
