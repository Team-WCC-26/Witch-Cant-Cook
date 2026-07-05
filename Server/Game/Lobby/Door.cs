using Protocol;

namespace Server;

public class Door
{
    public event Action<Door> OnOpened;

    public bool IsOpen { get; set; }

    private readonly HashSet<string> _interactors = new();

    private readonly int _requiredCount;
    private readonly int _openTime;

    private readonly TimerManager _timerManager;

    private TimerHandle? _openTimer;

    public DoorId DoorId { get; }

    public Door(DoorId doorId, int requiredCount, int openTime, TimerManager timerManager, Action<Door> openAction)
    {
        OnOpened = openAction;
        DoorId = doorId;
        _requiredCount = requiredCount;
        _openTime = openTime;
        _timerManager = timerManager;
    }

    public void BeginInteract(string playerId)
    {
        bool wasEnough = _interactors.Count >= _requiredCount;

        _interactors.Add(playerId);

        bool isEnough = _interactors.Count >= _requiredCount;

        if (!wasEnough && isEnough)
        {
            _openTimer = _timerManager.Schedule(
                _openTime,
                this,
                OnOpened);
        }
    }

    public void EndInteract(string playerId)
    {
        bool wasEnough = _interactors.Count >= _requiredCount;

        _interactors.Remove(playerId);

        bool isEnough = _interactors.Count >= _requiredCount;

        if (wasEnough && !isEnough)
        {
            if (_openTimer != null)
            {
                _timerManager.Cancel(_openTimer.Value);
                _openTimer = null;
            }
        }
    }
}
