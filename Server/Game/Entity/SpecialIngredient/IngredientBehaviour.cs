namespace Server;

public abstract class IngredientBehaviour
{
    private TimerManager? _timerManager;
    private TimerHandle _handle;

    public void Init(TimerManager timerManager)
    {
        _timerManager = timerManager;
    }

    public void Clear()
    {
        _timerManager = null;
    }
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
