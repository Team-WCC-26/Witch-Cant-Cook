using SuperSocket;
using SuperSocket.Connection;
using SuperSocket.Server;

namespace Server;

public class Session : AppSession
{
    public Player Player { get; set; } = new();

    protected override ValueTask OnSessionConnectedAsync()
    {
        Player.PlayerId = SessionID;
        Player.Session = this;

        return base.OnSessionConnectedAsync();
    }

    protected override ValueTask OnSessionClosedAsync(CloseEventArgs e)
    {
        Player.LeaveRoom();

        return base.OnSessionClosedAsync(e);
    }
}
