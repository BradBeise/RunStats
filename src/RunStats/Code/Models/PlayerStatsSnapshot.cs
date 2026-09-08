using System.Collections.Generic;

namespace RunStats.Models;

public sealed record PlayerStatsSnapshot(
    ulong PlayerNetId,
    long Revision,
    IReadOnlyDictionary<StatKind, long> Totals,
    IReadOnlyDictionary<string, long> CardPlayCounts)
{
    public long GetTotal(StatKind kind) => Totals.TryGetValue(kind, out var value) ? value : 0;

    public long GetCardPlayCount(string cardId) =>
        CardPlayCounts.TryGetValue(cardId, out var value) ? value : 0;

    public string? GetMostPlayedCardId()
    {
        string? bestCardId = null;
        long bestCount = 0;
        foreach (var pair in CardPlayCounts)
        {
            if (pair.Value > bestCount ||
                (pair.Value == bestCount && string.CompareOrdinal(pair.Key, bestCardId) < 0))
            {
                bestCardId = pair.Key;
                bestCount = pair.Value;
            }
        }

        return bestCardId;
    }
}
