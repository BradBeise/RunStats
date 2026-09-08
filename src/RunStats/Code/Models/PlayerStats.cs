using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunStats.Models;

internal sealed class PlayerStats
{
    private readonly Dictionary<StatKind, long> _totals = new();
    private readonly Dictionary<string, long> _cardPlayCounts = new(StringComparer.Ordinal);

    public PlayerStats(ulong playerNetId)
    {
        PlayerNetId = playerNetId;
        foreach (var kind in Enum.GetValues<StatKind>())
        {
            _totals[kind] = 0;
        }
    }

    public ulong PlayerNetId { get; }

    public long Revision { get; private set; }

    public static bool TryCreateFromSnapshot(
        PlayerStatsSnapshot snapshot,
        out PlayerStats? player)
    {
        player = null;
        if (snapshot.Revision < 0 ||
            snapshot.Totals.Count != Enum.GetValues<StatKind>().Length ||
            snapshot.CardPlayCounts.Count > ushort.MaxValue)
        {
            return false;
        }

        var restored = new PlayerStats(snapshot.PlayerNetId);
        foreach (var kind in Enum.GetValues<StatKind>())
        {
            if (!snapshot.Totals.TryGetValue(kind, out var total) || total < 0)
            {
                return false;
            }

            restored._totals[kind] = total;
        }

        long cardPlayTotal = 0;
        try
        {
            foreach (var pair in snapshot.CardPlayCounts)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    !string.Equals(pair.Key, pair.Key.Trim(), StringComparison.Ordinal) ||
                    pair.Value <= 0)
                {
                    return false;
                }

                cardPlayTotal = checked(cardPlayTotal + pair.Value);
                restored._cardPlayCounts.Add(pair.Key, pair.Value);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (cardPlayTotal != restored._totals[StatKind.CardsPlayed])
        {
            return false;
        }

        restored.Revision = snapshot.Revision;
        player = restored;
        return true;
    }

    public MutationResult TryApply(StatMutation mutation)
    {
        if (!Enum.IsDefined(mutation.Kind))
        {
            return MutationResult.InvalidStat;
        }

        if (mutation.Amount <= 0)
        {
            return MutationResult.InvalidAmount;
        }

        var isCardPlay = mutation.Kind == StatKind.CardsPlayed;
        if (isCardPlay != !string.IsNullOrWhiteSpace(mutation.SourceId))
        {
            return MutationResult.InvalidSource;
        }

        try
        {
            var nextTotal = checked(_totals[mutation.Kind] + mutation.Amount);
            var nextRevision = checked(Revision + 1);
            long? nextCardCount = null;

            if (isCardPlay)
            {
                var cardId = mutation.SourceId!.Trim();
                _cardPlayCounts.TryGetValue(cardId, out var currentCardCount);
                nextCardCount = checked(currentCardCount + mutation.Amount);
            }

            _totals[mutation.Kind] = nextTotal;
            if (nextCardCount.HasValue)
            {
                _cardPlayCounts[mutation.SourceId!.Trim()] = nextCardCount.Value;
            }

            Revision = nextRevision;
            return MutationResult.Applied;
        }
        catch (OverflowException)
        {
            return MutationResult.Overflow;
        }
    }

    public PlayerStatsSnapshot CaptureSnapshot()
    {
        return new PlayerStatsSnapshot(
            PlayerNetId,
            Revision,
            new ReadOnlyDictionary<StatKind, long>(new Dictionary<StatKind, long>(_totals)),
            new ReadOnlyDictionary<string, long>(
                new Dictionary<string, long>(_cardPlayCounts, StringComparer.Ordinal)));
    }
}
