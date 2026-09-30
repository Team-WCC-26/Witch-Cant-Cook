using Protocol;

namespace Server;

public sealed class IngredientCorn : IngredientBehaviour, IPickupHandler, IDropHandler
{
    private const long _holdDurationMs = 3000;
    private const long _abandonDurationMs = 10000;

    private TimerHandle _holdTimer;
    private TimerHandle _abandonTimer;
    private bool _triggered;

    public override void OnEnable()
    {
        _triggered = false;

        if (Ingredient.Parent is Player)
        {
            ScheduleHold();
        }
        else
        {
            ScheduleAbandon();
        }
    }

    public override void OnDisable()
    {
        Cancel(ref _holdTimer);
        Cancel(ref _abandonTimer);
    }

    public byte[]? OnPickUp(Player player)
    {
        if (_triggered) return null;

        Cancel(ref _abandonTimer);
        ScheduleHold();

        return null;
    }

    public byte[]? OnDrop()
    {
        if (_triggered) return null;

        Cancel(ref _holdTimer);
        ScheduleAbandon();

        return null;
    }

    private void ScheduleHold()
    {
        Cancel(ref _holdTimer);
        _holdTimer = Schedule(_holdDurationMs, this, static corn => corn.TriggerExplosion());
    }

    private void ScheduleAbandon()
    {
        Cancel(ref _abandonTimer);
        _abandonTimer = Schedule(_abandonDurationMs, this, static corn => corn.TriggerExplosion());
    }

    private void TriggerExplosion()
    {
        _holdTimer = default;
        _abandonTimer = default;

        if (_triggered || !Enabled) return;

        _triggered = true;

        Broadcast(new IngredientCornPacket
        {
            EntityId = Ingredient.EntityId,
            ExplosionSeed = EffectSeed()
        });

        Ingredient.Destroy();
    }
}
