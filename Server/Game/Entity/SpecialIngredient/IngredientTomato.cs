using Protocol;

namespace Server;

public sealed class IngredientTomato : IngredientBehaviour, IPickupHandler, IDropHandler, ICollisionHandler
{
    private const long _spawnDelayMs = 2000;
    private const long _stunDurationMs = 10000;

    private TimerHandle _activationTimer;
    private TimerHandle _stunTimer;
    private bool _active;
    private bool _stunned;
    private string? _targetPlayerId;

    public override void OnEnable()
    {
        _active = false;
        _stunned = false;
        _targetPlayerId = null;
        ScheduleActivation();
    }

    public override void OnDisable()
    {
        Cancel(ref _activationTimer);
        Cancel(ref _stunTimer);
        CancelDistanceQuery();
    }

    public byte[]? OnPickUp(Player player)
    {
        Cancel(ref _activationTimer);
        Cancel(ref _stunTimer);
        CancelDistanceQuery();
        _active = false;
        _stunned = false;
        _targetPlayerId = null;

        return null;
    }

    public byte[]? OnDrop()
    {
        if (!_stunned)
        {
            ScheduleActivation();
        }

        return null;
    }

    public byte[]? OnCollision()
    {
        if (!_active || _stunned) return null;

        _stunned = true;
        _active = false;
        Cancel(ref _activationTimer);
        Cancel(ref _stunTimer);
        _stunTimer = Schedule(_stunDurationMs, this, static tomato => tomato.Reactivate());

        return Serialize(new IngredientActivationPacket
        {
            EntityId = Ingredient.EntityId,
            State = IngredientActivationState.Stunned,
            DurationMs = _stunDurationMs
        });
    }

    private void ScheduleActivation()
    {
        Cancel(ref _activationTimer);
        _activationTimer = Schedule(_spawnDelayMs, this, static tomato => tomato.Activate());
    }

    private void Activate()
    {
        _activationTimer = default;

        if (!Enabled || _stunned || Ingredient.Parent != null) return;

        RequestNearest(targetPlayerId =>
        {
            if (!Enabled || _stunned || Ingredient.Parent != null) return;

            _targetPlayerId = targetPlayerId;
            _active = targetPlayerId != null;

            if (_targetPlayerId == null) return;

            Broadcast(new IngredientActivationPacket
            {
                EntityId = Ingredient.EntityId,
                State = IngredientActivationState.Active,
                TargetPlayerId = _targetPlayerId,
                DurationMs = 0
            });
        });
    }

    private void Reactivate()
    {
        _stunTimer = default;
        _stunned = false;

        RequestNearest(targetPlayerId =>
        {
            if (!Enabled) return;

            _targetPlayerId = targetPlayerId;
            _active = targetPlayerId != null;

            Broadcast(new IngredientActivationPacket
            {
                EntityId = Ingredient.EntityId,
                State = IngredientActivationState.Retargeted,
                TargetPlayerId = _targetPlayerId,
                DurationMs = 0
            });
        });
    }

    // 두 플레이어가 응답한 거리 중 가장 가까운 플레이어를 타겟으로 결정한다
    private void RequestNearest(Action<string?> onDecided)
    {
        RequestDistances(distances =>
        {
            string? nearestPlayerId = null;
            float nearestDistance = float.MaxValue;

            foreach (var pair in distances)
            {
                if (pair.Value >= nearestDistance) continue;

                nearestPlayerId = pair.Key;
                nearestDistance = pair.Value;
            }

            onDecided(nearestPlayerId);
        });
    }
}
