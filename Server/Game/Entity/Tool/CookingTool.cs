namespace Server;

public class CookingTool(int toolId) : ContainerTool(toolId)
{
    public event Action? OnCookingCompleted;

    private int _damage = ServerContext.Instance.DataBase.Tools[toolId].Damage;

    private float _process = 0;
    private bool _bIsProcessing = false;

    public void Tick(long deltaTime)
    {
        if (Ingredient == null || !_bIsProcessing) return;

        _process += _damage * deltaTime * 0.001f;

        if (_process > Ingredient.Hp)
        {
            _bIsProcessing = false;
            _process = 0;

            OnCookingCompleted?.Invoke();
            OnCookingCompleted = null;
        }
    }

    public bool TryStartCook(Ingredient ingredient, Action completeAction)
    {
        if (_bIsProcessing)
        {
            if (TryCombine(ingredient, out _))
            {
                _process = 0;

                return true;
            }
            else
            {
                return false;
            }
        }

        Ingredient = ingredient;
        _bIsProcessing = true;
        OnCookingCompleted = completeAction;

        return true;
    }

    public override bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        if (other is Dish dish)
        {
            if (_bIsProcessing) return false;

            if (dish.TryCombine(this, out combinable))
            {
                Clear();

                return true;
            }
            else
            {
                return false;
            }
        }

        if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

        Ingredient = other as Ingredient;

        return true;
    }
}
