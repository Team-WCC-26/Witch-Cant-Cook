using Protocol;

namespace Server;

public abstract class CookingTool(int toolId, IContainerStorage storage) : ContainerTool(toolId, storage)
{
    public event Action? OnCookingCompleted;

    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;

    public Ingredient? Ingredient => _storage.First as Ingredient;

    protected abstract IngredientState _cookState { get; }
    private int _damage => ServerContext.Instance.DataBase.Tools[ToolId].Damage;

    private bool _cookable = true;
    private float _process = 0;
    private bool _bIsProcessing = false;

    private TimerManager _timerManager;
    private TimerHandle _cookTimer;

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
        if (_storage.Count <= 0) return;

        _cookTimer = _timerManager.Schedule(200, this, Cook);
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
        //if (!base.Interact(player)) return false;

        _cookTimer = _timerManager.Schedule(100, this, static t => t.Cook(t));

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is not Ingredient _) return false;

        return _storage.TryInsert(entity);
    }

    protected virtual void Cook(CookingTool tool)
    {
        Ingredient?.TryCook(_cookState);
    }
}
