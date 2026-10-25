// Immutable client snapshot for one completed play, including retries of the same stage.
public sealed class StageSettlementData
{
    // Local sequence within this room, not a server-issued run ID.
    public int RunNumber { get; }
    public int StageNumber { get; }
    public int? StartMoney { get; }
    public int? EndMoney { get; }
    public int? EarnedMoney => StartMoney.HasValue && EndMoney.HasValue
        ? EndMoney.Value - StartMoney.Value : (int?)null;

    public StageSettlementData(int runNumber, int stageNumber, int? startMoney, int? endMoney)
    {
        RunNumber = runNumber;
        StageNumber = stageNumber;
        StartMoney = startMoney;
        EndMoney = endMoney;
    }
}
