using System;
using System.Linq;
using RunStats.Models;

namespace RunStats.Tracking;

public sealed class StrengthStatTracker
{
    private readonly RunStatsState _state;

    public StrengthStatTracker(RunStatsState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool Record(
        StrengthAssistAllocation allocation,
        StatKind stat)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (!StatSemantics.IsSigned(stat) || allocation.UnassignedAward != 0)
        {
            return false;
        }

        try
        {
            var eventTotal = allocation.EventAwards.Aggregate(
                0L,
                static (current, entry) => checked(current + entry.Value));
            var playerTotal = allocation.PlayerAwards.Aggregate(
                0L,
                static (current, entry) => checked(current + entry.Value));
            if (allocation.EventAwards.Any(entry => entry.Key <= 0) ||
                allocation.PlayerAwards.Any(entry => entry.Key == 0) ||
                eventTotal != playerTotal || playerTotal != allocation.EligibilityPool)
            {
                return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        var mutations = allocation.PlayerAwards
            .Where(entry => entry.Value != 0)
            .OrderBy(entry => entry.Key)
            .Select(entry => StatMutation.Add(entry.Key, stat, entry.Value))
            .ToArray();
        return mutations.Length == 0 ||
            _state.TryApplyBatch(mutations) == MutationResult.Applied;
    }
}
