public enum StageReturnPhase { Lobby, Playing, Settlement, WaitingForServer, Returning, AwaitingSettlement }

// Local presentation state only. No currency changes or network authority here.
public sealed class StageReturnState
{
    public StageReturnPhase Phase { get; private set; } = StageReturnPhase.Lobby;
    public StageSettlementData Settlement { get; private set; }
    public bool BlocksInput => Phase == StageReturnPhase.AwaitingSettlement || Phase == StageReturnPhase.Settlement ||
        Phase == StageReturnPhase.WaitingForServer || Phase == StageReturnPhase.Returning;
    public bool ShowsSettlement => Phase == StageReturnPhase.Settlement ||
        Phase == StageReturnPhase.WaitingForServer || Phase == StageReturnPhase.Returning;
    private int lastSettledRun;

    public void Reset() { Phase = StageReturnPhase.Lobby; Settlement = null; lastSettledRun = 0; }
    public bool StartStage()
    {
        if (Phase == StageReturnPhase.Playing) return false;
        Phase = StageReturnPhase.Playing;
        return true;
    }
    public bool RecordSettlement(StageSettlementData data)
    {
        if (data == null || data.RunNumber <= lastSettledRun) return false;
        lastSettledRun = data.RunNumber;
        Settlement = data;
        Phase = StageReturnPhase.Settlement;
        return true;
    }
    public bool WaitForSettlement()
    {
        if (Phase != StageReturnPhase.Playing) return false;
        Phase = StageReturnPhase.AwaitingSettlement;
        return true;
    }
    public bool RequestReturn()
    {
        if (Phase != StageReturnPhase.Settlement) return false;
        Phase = StageReturnPhase.WaitingForServer;
        return true;
    }
    public bool ConfirmReturn()
    {
        if (Phase != StageReturnPhase.Settlement && Phase != StageReturnPhase.WaitingForServer) return false;
        Phase = StageReturnPhase.Returning;
        return true;
    }
    public bool CancelReturnRequest()
    {
        if (Phase != StageReturnPhase.WaitingForServer) return false;
        Phase = StageReturnPhase.Settlement;
        return true;
    }
    public bool CompleteReturn()
    {
        if (Phase != StageReturnPhase.Returning) return false;
        Phase = StageReturnPhase.Lobby;
        return true;
    }
    public void AbortReturn()
    {
        if (Phase == StageReturnPhase.Returning) Phase = StageReturnPhase.WaitingForServer;
    }
}
