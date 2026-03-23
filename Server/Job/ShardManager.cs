namespace Server;

public class ShardManager
{
    private readonly int _shardCnt = 4;
    private Shard[] _shards;
    
    public ShardManager()
    {
        _shards = new Shard[_shardCnt];

        for (int i = 0; i < _shardCnt; i++)
        {
            Shard shard = new();
            _ = Task.Run(async () => await shard.StartProcess());
            _shards[i] = shard;
        }
    }

    public void RegisterRoom(Room room)
    {
        GetShard().RegisterRoom(room);
    }

    private Shard GetShard()
    {
        return _shards.OrderBy(s => s.RoomCnt).First();
    }
}
