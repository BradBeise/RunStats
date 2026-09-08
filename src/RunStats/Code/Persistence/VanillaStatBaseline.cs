using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RunStats.Models;

namespace RunStats.Persistence;

public sealed class VanillaStatBaseline
{
    private readonly Dictionary<ulong, Dictionary<StatKind, long>> _players = new();

    public static long HistoricalHealing(bool isAncientMapPoint, long hpHealed) =>
        isAncientMapPoint && hpHealed >= 0 ? 0 : hpHealed;

    public bool TryAdd(ulong playerNetId, StatKind kind, long amount)
    {
        if (!Enum.IsDefined(kind) || amount < 0)
        {
            return false;
        }

        if (!_players.TryGetValue(playerNetId, out var totals))
        {
            totals = new Dictionary<StatKind, long>();
            _players.Add(playerNetId, totals);
        }

        totals.TryGetValue(kind, out var current);
        try
        {
            totals[kind] = checked(current + amount);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public bool TryMergeInto(RunStatsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var before = state.CaptureSnapshot();
        if (before.Lifecycle != RunLifecycle.Active || before.Identity is null)
        {
            return false;
        }

        try
        {
            var replacementPlayers = new Dictionary<ulong, PlayerStatsSnapshot>(before.Players);
            long totalChanges = 0;
            foreach (var playerPair in _players)
            {
                if (!before.Players.TryGetValue(playerPair.Key, out var player))
                {
                    return false;
                }

                var totals = new Dictionary<StatKind, long>(player.Totals);
                long playerChanges = 0;
                foreach (var statPair in playerPair.Value)
                {
                    if (statPair.Value > player.GetTotal(statPair.Key))
                    {
                        totals[statPair.Key] = statPair.Value;
                        playerChanges = checked(playerChanges + 1);
                    }
                }

                if (playerChanges > 0)
                {
                    totalChanges = checked(totalChanges + playerChanges);
                    replacementPlayers[playerPair.Key] = player with
                    {
                        Revision = checked(player.Revision + playerChanges),
                        Totals = new ReadOnlyDictionary<StatKind, long>(totals)
                    };
                }
            }

            if (totalChanges == 0)
            {
                return true;
            }

            var replacement = before with
            {
                Revision = checked(before.Revision + totalChanges),
                Players = new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(replacementPlayers)
            };
            return state.TryReplaceFromSnapshot(replacement) == SnapshotImportResult.Applied;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
