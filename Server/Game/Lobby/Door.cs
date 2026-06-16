namespace Server;

public class Door
{
    public bool IsOpen { get; set; }

    private readonly HashSet<string> _interactors = new();

    private readonly int _requiredCount;
    private readonly int _openTime; // ms

    private long _startTime;

    public Door(int requiredCount, int openTime)
    {
        _requiredCount = requiredCount;
        _openTime = openTime;
    }

    public void BeginInteract(string playerId)
    {
        bool wasEnough = _interactors.Count >= _requiredCount;

        _interactors.Add(playerId);

        bool isEnough = _interactors.Count >= _requiredCount;

        if (!wasEnough && isEnough)
        {
            _startTime = TimeUtil.NowMs();
        }
    }

    public void EndInteract(string playerId)
    {
        _interactors.Remove(playerId);
    }

    public bool Tick()
    {
        if (IsOpen) return false;

        if (_interactors.Count >= _requiredCount)
        {
            return TimeUtil.NowMs() - _startTime >= _openTime;
        }

        return false;
    }
}
