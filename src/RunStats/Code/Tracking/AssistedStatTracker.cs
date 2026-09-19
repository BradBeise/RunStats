using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunStats.Models;

namespace RunStats.Tracking;

public sealed class AssistedStatTracker
{
    private readonly RunStatsState _state;
    private readonly AssistedContributionTracker _contributions;

    public AssistedStatTracker(
        RunStatsState state,
        AssistedContributionTracker contributions)
    {
        _state = state;
        _contributions = contributions;
    }

    public bool RecordVulnerableAssist(
        object enemyToken,
        long assistPool,
        ulong attackerNetId,
        out AssistedShareAllocation allocation)
    {
        if (!_contributions.TryAllocate(
                AssistedEffectKind.Vulnerable,
                enemyToken,
                assistPool,
                attackerNetId,
                out allocation))
        {
            _state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamage);
            return false;
        }

        foreach (var entry in allocation.PlayerAwards)
        {
            if (entry.Value > 0)
            {
                _state.TryApply(StatMutation.Add(
                    entry.Key,
                    StatKind.AssistedDamage,
                    entry.Value));
            }
        }

        return true;
    }

    public bool RecordWeakPreventionCommand(
        object enemyToken,
        IReadOnlyList<WeakPreventionRow> targetRows,
        out AssistedShareAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(targetRows);
        allocation = EmptyAllocation();
        var preventionByPlayer = new Dictionary<ulong, long>();
        long preventionPool = 0;
        try
        {
            foreach (var row in targetRows)
            {
                if (row.ProtectedPlayerNetId == 0 || row.Prevention < 0)
                {
                    _state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamagePrevented);
                    return false;
                }

                preventionPool = checked(preventionPool + row.Prevention);
                preventionByPlayer.TryGetValue(row.ProtectedPlayerNetId, out var existing);
                preventionByPlayer[row.ProtectedPlayerNetId] = checked(existing + row.Prevention);
            }
        }
        catch (OverflowException)
        {
            _state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamagePrevented);
            return false;
        }

        if (preventionPool <= 0)
        {
            return true;
        }

        if (!_contributions.TryAllocateWeakCommand(
                enemyToken,
                preventionPool,
                preventionByPlayer,
                out var grossAllocation))
        {
            _state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamagePrevented);
            return false;
        }

        foreach (var entry in grossAllocation.PlayerAwards)
        {
            if (entry.Value > 0)
            {
                _state.TryApply(StatMutation.Add(
                    entry.Key,
                    StatKind.AssistedDamagePrevented,
                    entry.Value));
            }
        }

        allocation = grossAllocation;
        return true;
    }

    private static AssistedShareAllocation EmptyAllocation() =>
        new(
            0,
            new ReadOnlyDictionary<ulong, long>(new Dictionary<ulong, long>()),
            0,
            0);
}

public readonly record struct WeakPreventionRow(
    ulong ProtectedPlayerNetId,
    long Prevention);
