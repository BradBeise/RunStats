using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunStats.Tracking;

public enum StrengthImpactExpiryKind
{
    CombatPersistent = 0,
    FixedTemporary = 1,
    SourceBound = 2,
    UntilStrengthReset = 3
}

public enum StrengthAssistDirection
{
    OutgoingDamage = 0,
    IncomingPrevention = 1
}

public sealed record StrengthImpactEvent(
    long EventId,
    ulong ImpactorPlayerNetId,
    object EntityImpacted,
    ulong? ImpactedPlayerNetId,
    long ImpactValue,
    int RemainingTurns,
    StrengthImpactExpiryKind ExpiryKind,
    object? RestorationSource);

public readonly record struct StrengthImpactCapacity(long EventId, long SignedCapacity);

public sealed record StrengthAssistAllocation(
    long EligibilityPool,
    IReadOnlyDictionary<long, long> EventAwards,
    IReadOnlyDictionary<ulong, long> PlayerAwards,
    long UnassignedAward)
{
    public long CreditedAward => PlayerAwards.Values.Sum();
}

/// <summary>
/// Combat-local model for ordered, signed, non-self Strength contributions.
/// Runtime observation and damage counterfactuals are deliberately kept outside
/// this type so the lifecycle and allocation policy can be tested without STS2.
/// </summary>
public sealed class StrengthImpactLedger
{
    private readonly Dictionary<object, List<StrengthImpactEvent>> _eventsByTarget =
        new(ReferenceEqualityComparer.Instance);

    private long _nextEventId;

    public bool TryAdd(
        ulong impactorPlayerNetId,
        object entityImpacted,
        ulong? impactedPlayerNetId,
        long impactValue,
        int remainingTurns,
        StrengthImpactExpiryKind expiryKind,
        object? restorationSource,
        out StrengthImpactEvent? impactEvent)
    {
        ArgumentNullException.ThrowIfNull(entityImpacted);
        impactEvent = null;
        if (impactorPlayerNetId == 0 ||
            impactedPlayerNetId == impactorPlayerNetId ||
            impactValue is 0 or long.MinValue ||
            !HasValidLifetime(remainingTurns, expiryKind, restorationSource) ||
            _nextEventId == long.MaxValue)
        {
            return false;
        }

        var eventId = _nextEventId + 1;
        var created = new StrengthImpactEvent(
            eventId,
            impactorPlayerNetId,
            entityImpacted,
            impactedPlayerNetId,
            impactValue,
            remainingTurns,
            expiryKind,
            restorationSource);

        if (!_eventsByTarget.TryGetValue(entityImpacted, out var events))
        {
            events = new List<StrengthImpactEvent>();
            _eventsByTarget.Add(entityImpacted, events);
        }

        events.Add(created);
        _nextEventId = eventId;
        impactEvent = created;
        return true;
    }

    public IReadOnlyList<StrengthImpactEvent> GetEvents(object entityImpacted)
    {
        ArgumentNullException.ThrowIfNull(entityImpacted);
        return !_eventsByTarget.TryGetValue(entityImpacted, out var events)
            ? Array.Empty<StrengthImpactEvent>()
            : Array.AsReadOnly(events.ToArray());
    }

    public bool TryReduceEvent(long eventId, long magnitude)
    {
        if (eventId <= 0 || magnitude <= 0)
        {
            return false;
        }

        foreach (var pair in _eventsByTarget.ToArray())
        {
            var index = pair.Value.FindIndex(item => item.EventId == eventId);
            if (index < 0)
            {
                continue;
            }

            var current = pair.Value[index];
            var currentMagnitude = Math.Abs(current.ImpactValue);
            if (magnitude > currentMagnitude)
            {
                return false;
            }

            if (magnitude == currentMagnitude)
            {
                pair.Value.RemoveAt(index);
                RemoveEmptyTarget(pair.Key, pair.Value);
            }
            else
            {
                var reducedMagnitude = currentMagnitude - magnitude;
                pair.Value[index] = current with
                {
                    ImpactValue = current.ImpactValue < 0
                        ? -reducedMagnitude
                        : reducedMagnitude
                };
            }

            return true;
        }

        return false;
    }

    public int ExpireSource(object entityImpacted, object restorationSource)
    {
        ArgumentNullException.ThrowIfNull(entityImpacted);
        ArgumentNullException.ThrowIfNull(restorationSource);
        if (!_eventsByTarget.TryGetValue(entityImpacted, out var events))
        {
            return 0;
        }

        var removed = events.RemoveAll(item =>
            item.RestorationSource is not null &&
            ReferenceEquals(item.RestorationSource, restorationSource));
        RemoveEmptyTarget(entityImpacted, events);
        return removed;
    }

    public int AdvancePlayerTurn()
    {
        var expired = 0;
        foreach (var pair in _eventsByTarget.ToArray())
        {
            for (var index = pair.Value.Count - 1; index >= 0; index--)
            {
                var current = pair.Value[index];
                if (current.RemainingTurns < 0)
                {
                    continue;
                }

                var remaining = current.RemainingTurns - 1;
                if (remaining <= 0)
                {
                    pair.Value.RemoveAt(index);
                    expired++;
                }
                else
                {
                    pair.Value[index] = current with { RemainingTurns = remaining };
                }
            }

            RemoveEmptyTarget(pair.Key, pair.Value);
        }

        return expired;
    }

    public bool ResetTarget(object entityImpacted)
    {
        ArgumentNullException.ThrowIfNull(entityImpacted);
        return _eventsByTarget.Remove(entityImpacted);
    }

    public void Clear()
    {
        _eventsByTarget.Clear();
        _nextEventId = 0;
    }

    public bool TryAllocate(
        object entityImpacted,
        StrengthAssistDirection direction,
        long eligibilityPool,
        IReadOnlyList<StrengthImpactCapacity> capacities,
        out StrengthAssistAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(entityImpacted);
        ArgumentNullException.ThrowIfNull(capacities);
        allocation = EmptyAllocation(eligibilityPool);
        if (!Enum.IsDefined(direction) ||
            !_eventsByTarget.TryGetValue(entityImpacted, out var events) ||
            capacities.Count == 0)
        {
            return false;
        }

        var eventMap = events.ToDictionary(item => item.EventId);
        var capacityMap = new Dictionary<long, long>();
        foreach (var capacity in capacities)
        {
            if (capacity.EventId <= 0 ||
                capacity.SignedCapacity == long.MinValue ||
                !eventMap.ContainsKey(capacity.EventId) ||
                !capacityMap.TryAdd(capacity.EventId, capacity.SignedCapacity))
            {
                return false;
            }

            var expectedSign = direction == StrengthAssistDirection.OutgoingDamage
                ? Math.Sign(eventMap[capacity.EventId].ImpactValue)
                : -Math.Sign(eventMap[capacity.EventId].ImpactValue);
            if (capacity.SignedCapacity != 0 &&
                Math.Sign(capacity.SignedCapacity) != expectedSign)
            {
                return false;
            }
        }

        var ordered = events
            .Where(item => capacityMap.ContainsKey(item.EventId))
            .OrderBy(item => capacityMap[item.EventId] < 0 ? 0 : 1)
            .ThenBy(item => item.EventId)
            .ToArray();
        var eventAwards = new Dictionary<long, long>();
        var playerAwards = new Dictionary<ulong, long>();
        var remaining = eligibilityPool;

        try
        {
            if (remaining < 0)
            {
                foreach (var impactEvent in ordered.Where(item => capacityMap[item.EventId] < 0))
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    var capacity = capacityMap[impactEvent.EventId];
                    var award = -Math.Min(Math.Abs(capacity), Math.Abs(remaining));
                    AddAward(impactEvent, award, eventAwards, playerAwards);
                    remaining = checked(remaining - award);
                }
            }
            else
            {
                foreach (var impactEvent in ordered.Where(item => capacityMap[item.EventId] < 0))
                {
                    var award = capacityMap[impactEvent.EventId];
                    AddAward(impactEvent, award, eventAwards, playerAwards);
                    remaining = checked(remaining - award);
                }

                foreach (var impactEvent in ordered.Where(item => capacityMap[item.EventId] > 0))
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    var award = Math.Min(capacityMap[impactEvent.EventId], remaining);
                    AddAward(impactEvent, award, eventAwards, playerAwards);
                    remaining = checked(remaining - award);
                }
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        allocation = new StrengthAssistAllocation(
            eligibilityPool,
            new ReadOnlyDictionary<long, long>(eventAwards),
            new ReadOnlyDictionary<ulong, long>(playerAwards),
            remaining);
        return true;
    }

    private static bool HasValidLifetime(
        int remainingTurns,
        StrengthImpactExpiryKind expiryKind,
        object? restorationSource) => expiryKind switch
    {
        StrengthImpactExpiryKind.FixedTemporary =>
            remainingTurns > 0 && restorationSource is not null,
        StrengthImpactExpiryKind.SourceBound =>
            remainingTurns == -1 && restorationSource is not null,
        StrengthImpactExpiryKind.CombatPersistent or StrengthImpactExpiryKind.UntilStrengthReset =>
            remainingTurns == -1 && restorationSource is null,
        _ => false
    };

    private static void AddAward(
        StrengthImpactEvent impactEvent,
        long award,
        IDictionary<long, long> eventAwards,
        IDictionary<ulong, long> playerAwards)
    {
        if (award == 0)
        {
            return;
        }

        eventAwards.Add(impactEvent.EventId, award);
        playerAwards.TryGetValue(impactEvent.ImpactorPlayerNetId, out var current);
        playerAwards[impactEvent.ImpactorPlayerNetId] = checked(current + award);
    }

    private void RemoveEmptyTarget(object target, ICollection<StrengthImpactEvent> events)
    {
        if (events.Count == 0)
        {
            _eventsByTarget.Remove(target);
        }
    }

    private static StrengthAssistAllocation EmptyAllocation(long eligibilityPool) =>
        new(
            eligibilityPool,
            new ReadOnlyDictionary<long, long>(new Dictionary<long, long>()),
            new ReadOnlyDictionary<ulong, long>(new Dictionary<ulong, long>()),
            eligibilityPool);
}
