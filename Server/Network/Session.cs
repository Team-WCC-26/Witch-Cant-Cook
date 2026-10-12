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

    protected override async ValueTask OnSessionClosedAsync(CloseEventArgs e)
    {
        // 지연 시뮬 큐를 먼저 정리해 종료 세션에 오래된 패킷이 유출되지 않게 한다
        await LatencySimulator.RemoveAsync(SessionID, this);

        Player.LeaveRoom();

        await base.OnSessionClosedAsync(e);
    }
}
