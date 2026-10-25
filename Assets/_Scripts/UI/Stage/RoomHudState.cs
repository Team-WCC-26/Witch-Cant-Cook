using System;
using System.Collections.Generic;

// Server snapshots replace shared money; stage changes never award or clear currency locally.
public sealed class RoomHudState
{
    public const int StageDurationSeconds = 240;
    public int Money { get; private set; }
    public bool HasMoney { get; private set; }
    public int PendingMoney { get; private set; }
    public bool HasPendingMoney { get; private set; }
    public int StageNumber { get; private set; } = 1;
    public bool HasStageSelection { get; private set; }
    public bool IsStageRunning { get; private set; }
    public IReadOnlyList<StageSettlementData> Settlements { get; }
    public StageSettlementData LatestSettlement => settlements.Count > 0 ? settlements[settlements.Count - 1] : null;
    public int? StageStartMoney => activeRunNumber != 0 ? activeStartMoney : null;
    public int? CurrentStageEarnedMoney => HasPendingMoney ? PendingMoney : (int?)null;
    private readonly List<StageSettlementData> settlements = new();
    private int nextRunNumber;
    private int activeRunNumber;
    private int activeStageNumber;
    private int? activeStartMoney;
    private bool hasStarted;
    private double endTime;

    public RoomHudState() => Settlements = settlements.AsReadOnly();

    public void ResetRoom()
    {
        Money = 0;
        HasMoney = false;
        PendingMoney = 0;
        HasPendingMoney = false;
        StageNumber = 1;
        HasStageSelection = false;
        IsStageRunning = false;
        hasStarted = false;
        endTime = 0d;
        settlements.Clear();
        nextRunNumber = 0;
        activeRunNumber = 0;
        activeStageNumber = 0;
        activeStartMoney = null;
    }

    public bool ApplyMoney(int total)
    {
        bool changed = !HasMoney || Money != total;
        Money = total;
        HasMoney = true;
        return changed;
    }

    public void ApplyStage(int number, bool hasSelection, bool running, double now)
    {
        if (running && (!IsStageRunning || StageNumber != number))
        {
            activeRunNumber = ++nextRunNumber;
            activeStageNumber = number;
            // Never assume zero or use a first reward snapshot as the starting balance.
            activeStartMoney = HasMoney ? Money : (int?)null;
            PendingMoney = 0;
            HasPendingMoney = true;
            hasStarted = true;
            endTime = now + StageDurationSeconds;
        }
        if (!running && !IsStageRunning && StageNumber != number) hasStarted = false;
        StageNumber = number;
        HasStageSelection = hasSelection;
        IsStageRunning = running;
    }

    public bool ApplyPendingMoney(int total)
    {
        // Ignore late progress after the final snapshot. Snapshots are totals, never deltas.
        if (!IsStageRunning || activeRunNumber == 0) return false;
        bool changed = !HasPendingMoney || PendingMoney != total;
        PendingMoney = total;
        HasPendingMoney = true;
        return changed;
    }

    public bool ApplyStageSettlement(int earnedMoney, int finalMoney, bool isSuccess)
    {
        // One final result per observed start. Do not add rewards on either client.
        if (activeRunNumber == 0) return false;
        settlements.Add(new StageSettlementData(activeRunNumber, activeStageNumber,
            activeStartMoney, finalMoney, earnedMoney, isSuccess));
        ApplyMoney(finalMoney);
        PendingMoney = earnedMoney;
        HasPendingMoney = true;
        IsStageRunning = false;
        activeRunNumber = 0;
        activeStageNumber = 0;
        activeStartMoney = null;
        return true;
    }

    public int RemainingSeconds(double now) => IsStageRunning
        ? (int)Math.Ceiling(Math.Max(0d, endTime - now))
        : hasStarted ? 0 : StageDurationSeconds;

    public static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
