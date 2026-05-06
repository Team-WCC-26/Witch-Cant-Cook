using Protocol;
using SuperSocket.Server;
using SuperSocket.Server.Abstractions.Session;
using System.Numerics;

namespace Server;

public class Player
{
    public string PlayerId { get; set; }
    public IAppSession Session { get; set; }
    public Room? Room { get; set; }
    public Vector3 Pos { get; set; }
    public Vector3 Rot { get; set; }
    public PlayerCombinedState State { get; set; }

    public void Send(byte[] packet)
    {
        Session?.SendAsync(packet);
    }

    public void LeaveRoom()
    {
        Room?.Leave(this);
    }
}
