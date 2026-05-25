namespace Server;

public class ServerContext
{
    private static ServerContext _instance = new();
    public static ServerContext Instance => _instance;

    public ServerContext()
    {
        ShardManager = new();
        RoomManager = new(ShardManager);
        DataBase = new();
    }

    public RoomManager RoomManager { get; }
    public ShardManager ShardManager { get; }
    public DataBase DataBase { get; }
}
