using Protocol;
using System.Runtime.Serialization.DataContracts;

namespace Server;

public abstract class CookingTool(IContainerStorage storage) : ContainerTool(storage)
{
    public event Action? OnCookingCompleted;

    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;

    public Ingredient? Ingredient => First as Ingredient;

    protected abstract IngredientState _cookState { get; }

    private bool _cookable = true;
    //private float _process = 0;
    //private bool _bIsProcessing = false;

    protected TimerManager _timerManager;
    protected TimerHandle _cookTimer;

    //public void Tick(long deltaTime)
    //{
    //    if (Ingredient == null || !_bIsProcessing) return;

    //    _process += _damage * deltaTime * 0.001f;

    //    if (_process > Ingredient.Hp)
    //    {
    //        _bIsProcessing = false;
    //        _process = 0;

    //        OnCookingCompleted?.Invoke();
    //        OnCookingCompleted = null;
    //    }
    //}

    //public bool TryStartCook(Ingredient ingredient, Action completeAction)
    //{
    //    if (_bIsProcessing)
    //    {
    //        if (TryCombine(ingredient, out _))
    //        {
    //            _process = 0;

    //            return true;
    //        }
    //        else
    //        {
    //            return false;
    //        }
    //    }

    //    Ingredient = ingredient;
    //    _bIsProcessing = true;
    //    OnCookingCompleted = completeAction;

    //    return true;
    //}

    public void StartCook()
    {
        if (!_cookable) return;
        if (_storage.Count <= 0) return;

        if (!_timerManager.Resume(_cookTimer))
        {
            float maxHp = 0;

            foreach (var item in _storage)
            {
                Ingredient ingredient;

                if (item is ContainerTool ct)
                {
                    ingredient = ct.First as Ingredient;
                }
                else
                {
                    ingredient = item as Ingredient;
                }

                maxHp = MathF.Max(maxHp, ingredient.Hp);
            }

            long delayMs = (long)MathF.Ceiling(maxHp / Damage);

            if (!_timerManager.ResetDelayMs(_cookTimer, delayMs))
            {
                _cookTimer = _timerManager.Schedule(delayMs, this, static t => t.Cook());
            }
        }

        CookStartPacket packet = new()
        {
            ToolEntityId = EntityId,
            CookingTimeMs = _timerManager.RemainingTime(_cookTimer)
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }

    public void SetCookEnable(bool enable)
    {
        _cookable = enable;
    }

    //public override bool TryCombine(ICombinable other, out ICombinable combinable)
    //{
    //    combinable = this;

    //    if (other is Dish)
    //    {
    //        if (_bIsProcessing) return false;

    //        if (!other.TryCombine(this, out combinable)) return false;

    //        Clear();

    //        return true;
    //    }

    //    if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

    //    Entity = other as Ingredient;

    //    return true;
    //}

    public override bool Interact(Player player)
    {
        if (Ingredient == null) return false;
        if (player.HoldingEntity is not Dish dish) return false;
        if (!dish.TryCombine(Ingredient)) return false;

        _storage.Clear();

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is not Ingredient _) return false;

        return base.Insert(entity);
    }

    protected virtual void Cook()
    {
        Ingredient?.TryCook(_cookState);
    }
}
