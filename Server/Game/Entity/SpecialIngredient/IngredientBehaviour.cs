namespace Server;

public abstract class IngredientBehaviour
{
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;

            if (value)
            {
                OnEnable();
            }
            else
            {
                OnDisable();
            }
        }
    }

    private TimerManager? _timerManager;
    private TimerHandle _handle;

    private bool _enabled = true;

    public void Init(TimerManager timerManager)
    {
        _timerManager = timerManager;
    }

    public void Clear()
    {
        _timerManager = null;
    }

    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
}

public interface IPickupHandler
{
    byte[] OnPickUp(Player player);
}

public interface IDropHandler
{
    byte[] OnDrop();
}

public interface ICollisionHandler
{
    byte[] OnCollision();
}
