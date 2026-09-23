using Protocol;

namespace Server;

public sealed class IngredientSquid : IngredientBehaviour, IPickupHandler, IDropHandler, ICollisionHandler
{
    private const long _holdDurationMs = 3000;
    private const long _blindDurationMs = 3000;
    private const long _cooldownMs = 10000;
    private const float _inkRadius = 10f;

    private TimerHandle _holdTimer;
    private TimerHandle _cooldownTimer;
    private bool _canSpray;

    public override void OnEnable()
    {
        _canSpray = true;
    }

    public override void OnDisable()
    {
        Cancel(ref _holdTimer);
        Cancel(ref _cooldownTimer);
        CancelDistanceQuery();
    }

    public byte[]? OnPickUp(Player player)
    {
        Cancel(ref _holdTimer);
        _holdTimer = Schedule(_holdDurationMs, this, static squid => squid.SprayFromHold());

        return null;
    }

    public byte[]? OnDrop()
    {
        Cancel(ref _holdTimer);

        return null;
    }

    public byte[]? OnCollision()
    {
        if (!TryBeginSpray()) return null;

        RequestDistances(BroadcastInk);

        return null;
    }

    private void SprayFromHold()
    {
        _holdTimer = default;

        if (Ingredient.Parent is not Player) return;

        if (!TryBeginSpray())
        {
            _holdTimer = Schedule(_holdDurationMs, this, static squid => squid.SprayFromHold());
            return;
        }

        _holdTimer = Schedule(_holdDurationMs, this, static squid => squid.SprayFromHold());

        RequestDistances(BroadcastInk);
    }

    private bool TryBeginSpray()
    {
        if (!Enabled || !_canSpray) return false;

        _canSpray = false;
        Cancel(ref _cooldownTimer);
        _cooldownTimer = Schedule(_cooldownMs, this, static squid => squid.ResetCooldown());

        return true;
    }

    // 응답받은 거리 기준으로 먹물 반경 안에 있는 플레이어에게 먹물을 뿌린다
    private void BroadcastInk(IReadOnlyDictionary<string, float> distances)
    {
        if (!Enabled) return;

        IngredientSquidPacket packet = new()
        {
            EntityId = Ingredient.EntityId,
            BlindDurationMs = _blindDurationMs
        };

        foreach (var player in Ingredient.Room.Players)
        {
            if (!distances.TryGetValue(player.PlayerId, out float distance)) continue;
            if (distance > _inkRadius) continue;

            packet.PlayerIds.Add(player.PlayerId);
        }

        if (packet.PlayerIds.Count == 0) return;

        Broadcast(packet);
    }

    private void ResetCooldown()
    {
        _cooldownTimer = default;
        _canSpray = true;
    }
}
