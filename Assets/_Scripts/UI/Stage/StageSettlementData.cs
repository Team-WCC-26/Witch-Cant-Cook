// Immutable client snapshot for one completed play, including retries of the same stage.
public sealed class StageSettlementData
{
    // Local sequence within this room, not a server-issued run ID.
    public int RunNumber { get; }
    public int StageNumber { get; }
    public int? StartMoney { get; }
    public int EndMoney { get; }
    // Server-confirmed earnings, independent of wallet spending or the start snapshot.
    public int EarnedMoney { get; }
    public bool IsSuccess { get; }

    public StageSettlementData(int runNumber, int stageNumber, int? startMoney, int endMoney,
        int earnedMoney, bool isSuccess)
    {
        RunNumber = runNumber;
        StageNumber = stageNumber;
        StartMoney = startMoney;
        EndMoney = endMoney;
        EarnedMoney = earnedMoney;
        IsSuccess = isSuccess;
    }
}
