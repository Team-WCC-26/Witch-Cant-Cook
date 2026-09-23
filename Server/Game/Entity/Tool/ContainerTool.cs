namespace Server;

public abstract class ContainerTool(IContainerStorage storage) : Tool
{
    private const long _damageIntervalMs = 1000;

    public Entity? First => _storage.First;
    protected IContainerStorage _storage { get; init; } = storage;

    public IReadOnlyList<Player> InsidePlayers => _insidePlayers;
    private readonly List<Player> _insidePlayers = new();
    private TimerHandle _damageTimer;

    //public override bool Interact(Player player)
    //{
    //    if (player.HoldingEntity == null || player.HoldingEntity is not ICombinable combinable) return false;

    //    return TryCombine(combinable, out _);
    //}

    public virtual bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;

        if (!_storage.TryInsert(entity)) return false;

        entity.Parent = this;

        return true;
    }

    public virtual bool Remove(Entity entity)
    {
        return _storage.TryRemove(entity);
    }

    public void Clear()
    {
        foreach (var entity in _storage)
        {
            if (entity.Parent == this)
            {
                entity.Parent = null;
            }
        }

        _storage.Clear();
    }

    public virtual bool AddPlayer(Player player)
    {
        if (player.Parent != null) return false;
        if (_insidePlayers.Contains(player)) return false;

        _insidePlayers.Add(player);
        player.Parent = this;

        return true;
    }

    public virtual void RemovePlayer(Player player)
    {
        if (!_insidePlayers.Remove(player)) return;

        player.Parent = null;
    }

    public void ClearInsidePlayers()
    {
        List<Player> players = new(_insidePlayers);

        _insidePlayers.Clear();

        foreach (var player in players)
        {
            player.Parent = null;
        }
    }

    protected void StartPlayerDamage()
    {
        if (_damageTimer.Id != 0) return;

        _damageTimer = Room.TimerManager.Schedule(_damageIntervalMs, this, static t => t.DamageInsidePlayers());
    }

    protected void StopPlayerDamage()
    {
        if (_damageTimer.Id == 0) return;

        Room.TimerManager.Cancel(_damageTimer);
        _damageTimer = default;
    }

    // 내부의 플레이어에게 초당 데미지
    private void DamageInsidePlayers()
    {
        _damageTimer = Room.TimerManager.Schedule(_damageIntervalMs, this, static t => t.DamageInsidePlayers());

        foreach (var player in _insidePlayers)
        {
            player.ApplyDamage(Damage);
        }
    }
}