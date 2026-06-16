using Protocol;
using SuperSocket.Server.Abstractions.Session;
using System.Numerics;

namespace Server;

public class Player : Entity
{
    public string PlayerId { get; set; }
    public IAppSession Session { get; set; }
    public float LastPingTime { get; set; }
    public float Ping { get; set; }
    public Room? Room { get; set; }
    public PlayerCombinedState State { get; set; }

    public ValueTask Send(ReadOnlyMemory<byte> packet)
    {
        return Session.SendAsync(packet);
    }

    public void LeaveRoom()
    {
        Room?.PushJob(() =>
        {
            Room?.Leave(this);
        });
    }
}
