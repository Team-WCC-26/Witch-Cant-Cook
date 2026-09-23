using Protocol;

namespace Server;

public sealed class IngredientSalmon : IngredientBehaviour, IPickupHandler, IDropHandler, ICollisionHandler
{
    private const long _flyDelayMs = 2000;
    private const long _stunDurationMs = 10000;

    private TimerHandle _flyTimer;
    private TimerHandle _stunTimer;
    private bool _stunned;

    public override void OnEnable()
    {
        _stunned = false;
        ScheduleFly();
    }

    public override void OnDisable()
    {
        Cancel(ref _flyTimer);
        Cancel(ref _stunTimer);
    }

    public byte[]? OnPickUp(Player player)
    {
        Cancel(ref _flyTimer);
        Cancel(ref _stunTimer);
        _stunned = false;

        return null;
    }

    public byte[]? OnDrop()
    {
        if (_stunned) return null;

        ScheduleFly();

        return null;
    }

    public byte[]? OnCollision()
    {
        if (_stunned) return null;

        _stunned = true;
        Cancel(ref _flyTimer);
        Cancel(ref _stunTimer);
        _stunTimer = Schedule(_stunDurationMs, this, static salmon => salmon.RecoverFromStun());

        return Serialize(new IngredientActivationPacket
        {
            EntityId = Ingredient.EntityId,
            State = IngredientActivationState.Stunned,
            DurationMs = _stunDurationMs
        });
    }

    private void ScheduleFly()
    {
        Cancel(ref _flyTimer);
        _flyTimer = Schedule(_flyDelayMs, this, static salmon => salmon.BeginFly());
    }

    private void BeginFly()
    {
        _flyTimer = default;

        if (!Enabled || _stunned || Ingredient.Parent != null) return;

        BroadcastState(IngredientActivationState.Active, null, 0);
    }

    private void RecoverFromStun()
    {
        _stunTimer = default;
        _stunned = false;
        BeginFly();
    }

    private void BroadcastState(IngredientActivationState state, string? targetPlayerId, long durationMs)
    {
        Broadcast(new IngredientActivationPacket
        {
            EntityId = Ingredient.EntityId,
            State = state,
            TargetPlayerId = targetPlayerId,
            DurationMs = durationMs
        });
    }
}
