using Protocol;

namespace Server;

public abstract class IngredientBehaviour
{
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;

            _enabled = value;

            if (value)
            {
                OnEnable();
            }
            else
            {
                OnDisable();
            }
        }
    }

    protected Ingredient Ingredient => _ingredient ?? throw new InvalidOperationException("Ingredient behaviour is not initialized.");
    protected TimerManager TimerManager => _timerManager ?? throw new InvalidOperationException("Ingredient behaviour is not initialized.");

    private Ingredient? _ingredient;
    private TimerManager? _timerManager;
    private bool _enabled = true;
    private readonly Dictionary<string, float> _distanceResults = new();
    private Action<IReadOnlyDictionary<string, float>>? _distanceCallback;

    public void Init(Ingredient ingredient, TimerManager timerManager)
    {
        _ingredient = ingredient;
        _timerManager = timerManager;

        if (_enabled)
        {
            OnEnable();
        }
    }

    public void Init(TimerManager timerManager)
    {
        _timerManager = timerManager;
    }

    public void Clear()
    {
        if (_enabled)
        {
            OnDisable();
        }

        CancelDistanceQuery();
        _enabled = false;
        _ingredient = null;
        _timerManager = null;
    }

    protected TimerHandle Schedule<T>(long delayMs, T state, Action<T> callback)
    {
        return TimerManager.Schedule(delayMs, state, callback);
    }

    protected void Cancel(ref TimerHandle handle)
    {
        if (handle.Id != 0 && _timerManager != null)
        {
            _timerManager.Cancel(handle);
        }

        handle = default;
    }

    protected void Broadcast<TPacket>(TPacket packet) where TPacket : class
    {
        Ingredient.Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }

    protected static int EffectSeed() => (int)TimeUtil.NowMs();

    protected byte[] Serialize<TPacket>(TPacket packet) where TPacket : class
    {
        return PacketSerializer.Serialize(packet, true);
    }

    // 서버는 재료의 정확한 위치를 모르므로 각 플레이어가 측정한 재료와의 거리를 요청-응답으로 수집한다
    protected void RequestDistances(Action<IReadOnlyDictionary<string, float>> onCollected)
    {
        CancelDistanceQuery();
        _distanceCallback = onCollected;

        byte[] body = Serialize(new IngredientDistanceRequestPacket
        {
            EntityId = Ingredient.EntityId
        });

        foreach (var player in Ingredient.Room.Players)
        {
            player.Send(body);
        }
    }

    protected void CancelDistanceQuery()
    {
        _distanceCallback = null;
        _distanceResults.Clear();
    }

    public void OnDistanceResponse(Player player, float distance)
    {
        if (_distanceCallback == null) return;

        _distanceResults[player.PlayerId] = distance;

        foreach (var roomPlayer in Ingredient.Room.Players)
        {
            if (!_distanceResults.ContainsKey(roomPlayer.PlayerId)) return;
        }

        Action<IReadOnlyDictionary<string, float>> callback = _distanceCallback;
        Dictionary<string, float> results = new(_distanceResults);

        CancelDistanceQuery();

        callback(results);
    }

    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
}

public interface IPickupHandler
{
    byte[]? OnPickUp(Player player);
}

public interface IDropHandler
{
    byte[]? OnDrop();
}

public interface ICollisionHandler
{
    byte[]? OnCollision();
}
