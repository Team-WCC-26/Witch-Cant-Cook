using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server;

public class ServerContext
{
    private static ServerContext _instance = new();
    public static ServerContext Instance => _instance;

    public ServerContext()
    {
        ShardManager = new();
        RoomManager = new(ShardManager);
    }

    public RoomManager RoomManager { get; }
    public ShardManager ShardManager { get; }
}
