using Protocol;

namespace Server;

public sealed class IngredientSalmon : IngredientBehaviour, IPickupHandler, IDropHandler, ICollisionHandler
{
    private const long _flyDelayMs = 2000;
    private const long _stunDurationMs = 10000;

    private TimerHandle _flyTimer;
    private TimerHandle _stunTimer;
    private bool _stunned;
    private bool _flying;   // BeginFly 이후 클라에 Active로 동기화된 상태

    public override void OnEnable()
    {
        _stunned = false;
        _flying = false;
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
        _flying = false;

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
        _flying = false;
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

    // 스냅샷 수집용: 기절(Stunned)/활성(Active) 상태만 클라에 동기화된 상태이므로 이것만 전달한다
    public override bool TryGetEffectState(out IngredientEffectSnapshot effect)
    {
        if (_stunned && TryGetRemainingTime(_stunTimer, out long remainingMs))
        {
            effect = new IngredientEffectSnapshot()
            {
                State = IngredientActivationState.Stunned,
                RemainingMs = remainingMs,
            };

            return true;
        }

        if (_flying)
        {
            effect = new IngredientEffectSnapshot()
            {
                State = IngredientActivationState.Active,
                RemainingMs = 0,
            };

            return true;
        }

        effect = new IngredientEffectSnapshot();

        return false;
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

        _flying = true;

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
