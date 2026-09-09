using System;
using System.Collections.Generic;
using System.Linq;

namespace RunStats.Tracking;

public sealed class AccelerantSponsorLedger
{
    private readonly List<Segment> _segments = new();
    private readonly Dictionary<ulong, long> _recordedAmounts = new();

    public bool ObserveApplication(ulong playerNetId, long positiveDelta)
    {
        if (playerNetId == 0 || positiveDelta <= 0)
        {
            return false;
        }

        try
        {
            _recordedAmounts.TryGetValue(playerNetId, out var prior);
            var updated = checked(prior + positiveDelta);
            if (_segments.Count > 0 && _segments[^1].PlayerNetId == playerNetId)
            {
                var last = _segments[^1];
                var updatedSegment = last with { Count = checked(last.Count + positiveDelta) };
                _recordedAmounts[playerNetId] = updated;
                _segments[^1] = updatedSegment;
            }
            else
            {
                _recordedAmounts[playerNetId] = updated;
                _segments.Add(new Segment(playerNetId, positiveDelta));
            }

            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public IReadOnlyList<ulong?> ResolveSponsors(
        int extraTriggerCount,
        IReadOnlyDictionary<ulong, long> liveAmounts,
        IReadOnlySet<ulong> livingPlayers)
    {
        ArgumentNullException.ThrowIfNull(liveAmounts);
        ArgumentNullException.ThrowIfNull(livingPlayers);
        if (extraTriggerCount <= 0)
        {
            return Array.Empty<ulong?>();
        }

        var mismatch = liveAmounts.Any(entry => entry.Value < 0) ||
            liveAmounts.Any(entry =>
                livingPlayers.Contains(entry.Key) &&
                entry.Value > 0 &&
                _recordedAmounts.GetValueOrDefault(entry.Key) != entry.Value) ||
            _recordedAmounts.Any(entry =>
                livingPlayers.Contains(entry.Key) &&
                liveAmounts.GetValueOrDefault(entry.Key) != entry.Value);
        if (mismatch)
        {
            return Enumerable.Repeat<ulong?>(null, extraTriggerCount).ToArray();
        }

        var sponsors = new List<ulong?>(extraTriggerCount);
        foreach (var segment in _segments)
        {
            if (!livingPlayers.Contains(segment.PlayerNetId))
            {
                continue;
            }

            for (var index = 0L; index < segment.Count && sponsors.Count < extraTriggerCount; index++)
            {
                sponsors.Add(segment.PlayerNetId);
            }

            if (sponsors.Count == extraTriggerCount)
            {
                break;
            }
        }

        while (sponsors.Count < extraTriggerCount)
        {
            sponsors.Add(null);
        }

        return sponsors;
    }

    public void Clear()
    {
        _segments.Clear();
        _recordedAmounts.Clear();
    }

    private readonly record struct Segment(ulong PlayerNetId, long Count);
}
