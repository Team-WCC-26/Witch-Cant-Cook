using Protocol;
using System.Numerics;

namespace Server;

public sealed class IngredientCorn : IngredientBehaviour, ICollisionHandler
{
    private const long _expoldeDelayMs = 3000;
    private const int _popcornId = 12400;

    private bool _triggered;

    public override void OnEnable()
    {
        _triggered = false;
    }

    public byte[]? OnCollision()
    {
        if (_triggered || !Enabled) return null;

        _triggered = true;

        TimerManager.Schedule(_expoldeDelayMs, this, static c => c.Explode());

        IngredientCornExplodeStartPacket packet = new()
        {
            EntityId = Ingredient.EntityId,
            ExplosionDelayMs = _expoldeDelayMs,
        };

        return Serialize(packet);
    }

    private void Explode()
    {
        Vector3? explosionOrigin = null;

        if (Ingredient.Parent is Player holder)
        {
            explosionOrigin = holder.Position;
        }

        Broadcast(new IngredientCornPacket
        {
            EntityId = Ingredient.EntityId,
            ExplosionSeed = EffectSeed(),
            ExplosionOrigin = explosionOrigin
        });

        Ingredient.InitIngredientId(_popcornId);
    }
}
