using RunStats.Models;

namespace RunStats.Tracking;

public sealed class RunProgressTracker
{
    private readonly RunStatsState _state;

    public RunProgressTracker(RunStatsState state)
    {
        _state = state;
    }

    public void RecordCardPlayed(ulong playerNetId, string cardId)
    {
        _state.TryApply(StatMutation.CardPlayed(playerNetId, cardId));
    }

    public void RecordCount(ulong playerNetId, StatKind kind, long amount = 1)
    {
        if (amount > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, kind, amount));
        }
    }

    public void RecordGoldGained(
        ulong playerNetId,
        int previousGold,
        int currentGold,
        bool wasStolenBack)
    {
        if (wasStolenBack)
        {
            return;
        }

        RecordCount(playerNetId, StatKind.GoldEarned, ActualDelta.Positive(previousGold, currentGold));
    }

    public void RecordGoldLost(
        ulong playerNetId,
        int previousGold,
        int currentGold,
        bool wasSpent)
    {
        if (!wasSpent)
        {
            return;
        }

        RecordCount(playerNetId, StatKind.GoldSpent, ActualDelta.Positive(currentGold, previousGold));
    }
}
