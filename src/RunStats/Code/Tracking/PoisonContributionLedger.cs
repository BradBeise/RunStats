using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace RunStats.Tracking;

public readonly record struct PoisonObservation(
    bool Accepted,
    long CreditedPoisonApplied,
    long UnattributedPoisonApplied,
    bool CycleReset);

public sealed record PoisonDamageAllocation(
    long ActualDamage,
    IReadOnlyDictionary<ulong, long> PlayerDamage,
    long UnattributedDamage)
{
    public long CreditedDamage => PlayerDamage.Values.Sum();
}

public sealed class PoisonContributionLedger
{
    private sealed class Cycle
    {
        public long ObservedAmount;
        public long UnattributedWeight;
        public ExactFraction UnattributedCarry = ExactFraction.Zero;
        public Dictionary<ulong, long> Weights { get; } = new();
        public Dictionary<ulong, ExactFraction> Carries { get; } = new();
        public Dictionary<ulong, long> CreditedDamage { get; } = new();
    }

    private readonly Dictionary<object, Cycle> _cycles =
        new(ReferenceEqualityComparer.Instance);

    public PoisonObservation ObserveAmount(
        object enemyToken,
        long previousAmount,
        long currentAmount,
        ulong? contributorNetId)
    {
        ArgumentNullException.ThrowIfNull(enemyToken);
        if (previousAmount < 0 || currentAmount < 0)
        {
            return default;
        }

        if (currentAmount == 0)
        {
            var reset = _cycles.Remove(enemyToken);
            return new PoisonObservation(true, 0, 0, reset);
        }

        if (!_cycles.TryGetValue(enemyToken, out var cycle))
        {
            cycle = new Cycle { ObservedAmount = previousAmount };
            if (previousAmount > 0)
            {
                cycle.UnattributedWeight = previousAmount;
            }
        }
        else if (cycle.ObservedAmount != previousAmount)
        {
            return default;
        }

        var delta = currentAmount - previousAmount;
        var knownContributor = contributorNetId is > 0;
        if (delta > 0)
        {
            try
            {
                if (knownContributor)
                {
                    cycle.Weights.TryGetValue(contributorNetId!.Value, out var weight);
                    cycle.Weights[contributorNetId.Value] = checked(weight + delta);
                    cycle.Carries.TryAdd(contributorNetId.Value, ExactFraction.Zero);
                }
                else
                {
                    cycle.UnattributedWeight = checked(cycle.UnattributedWeight + delta);
                }
            }
            catch (OverflowException)
            {
                return default;
            }
        }

        cycle.ObservedAmount = currentAmount;
        _cycles[enemyToken] = cycle;
        return new PoisonObservation(
            true,
            knownContributor && delta > 0 ? delta : 0,
            !knownContributor && delta > 0 ? delta : 0,
            false);
    }

    public bool TryAllocateDamage(
        object enemyToken,
        long actualDamage,
        out PoisonDamageAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(enemyToken);
        allocation = EmptyAllocation(actualDamage);
        if (actualDamage < 0 || !_cycles.TryGetValue(enemyToken, out var cycle))
        {
            return false;
        }

        BigInteger totalWeight = cycle.UnattributedWeight;
        foreach (var weight in cycle.Weights.Values)
        {
            totalWeight += weight;
        }

        if (totalWeight <= BigInteger.Zero || totalWeight > long.MaxValue)
        {
            return false;
        }

        var participants = new List<Participant>();
        foreach (var entry in cycle.Weights.OrderBy(entry => entry.Key))
        {
            var carry = cycle.Carries.TryGetValue(entry.Key, out var value)
                ? value
                : ExactFraction.Zero;
            participants.Add(new Participant(
                entry.Key,
                false,
                carry + ExactFraction.FromProduct(
                    actualDamage,
                    entry.Value,
                    (long)totalWeight)));
        }

        if (cycle.UnattributedWeight > 0)
        {
            participants.Add(new Participant(
                0,
                true,
                cycle.UnattributedCarry + ExactFraction.FromProduct(
                    actualDamage,
                    cycle.UnattributedWeight,
                    (long)totalWeight)));
        }

        try
        {
            foreach (var participant in participants)
            {
                var floor = participant.Due.IsPositive ? participant.Due.Floor() : BigInteger.Zero;
                participant.Award = checked((long)floor);
            }

            var assigned = participants.Sum(participant => participant.Award);
            while (assigned < actualDamage)
            {
                var recipient = participants
                    .OrderByDescending(participant => participant.Residual)
                    .ThenBy(participant => participant.IsUnattributed ? 1 : 0)
                    .ThenBy(participant => participant.PlayerNetId)
                    .First();
                recipient.Award = checked(recipient.Award + 1);
                assigned++;
            }

            while (assigned > actualDamage)
            {
                var recipient = participants
                    .Where(participant => participant.Award > 0)
                    .OrderBy(participant => participant.Residual)
                    .ThenByDescending(participant => participant.IsUnattributed ? 1 : 0)
                    .ThenByDescending(participant => participant.PlayerNetId)
                    .First();
                recipient.Award--;
                assigned--;
            }

            var playerDamage = new Dictionary<ulong, long>();
            var nextCarries = new Dictionary<ulong, ExactFraction>();
            var nextCreditedDamage = new Dictionary<ulong, long>(cycle.CreditedDamage);
            var nextUnattributedCarry = cycle.UnattributedCarry;
            var unattributedDamage = 0L;
            foreach (var participant in participants)
            {
                var carry = participant.Residual;
                if (participant.IsUnattributed)
                {
                    nextUnattributedCarry = carry;
                    unattributedDamage = participant.Award;
                    continue;
                }

                nextCarries[participant.PlayerNetId] = carry;
                playerDamage[participant.PlayerNetId] = participant.Award;
                nextCreditedDamage.TryGetValue(participant.PlayerNetId, out var prior);
                nextCreditedDamage[participant.PlayerNetId] = checked(prior + participant.Award);
            }

            cycle.UnattributedCarry = nextUnattributedCarry;
            cycle.Carries.Clear();
            foreach (var entry in nextCarries)
            {
                cycle.Carries.Add(entry.Key, entry.Value);
            }
            cycle.CreditedDamage.Clear();
            foreach (var entry in nextCreditedDamage)
            {
                cycle.CreditedDamage.Add(entry.Key, entry.Value);
            }

            allocation = new PoisonDamageAllocation(
                actualDamage,
                new ReadOnlyDictionary<ulong, long>(playerDamage),
                unattributedDamage);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public ulong? SelectKillRecipient(object enemyToken, ulong deterministicEntropy)
    {
        ArgumentNullException.ThrowIfNull(enemyToken);
        if (!_cycles.TryGetValue(enemyToken, out var cycle) || cycle.Weights.Count == 0)
        {
            return null;
        }

        var candidates = cycle.Weights.Keys.OrderBy(id => id).ToArray();
        var highestDamage = candidates.Max(id => cycle.CreditedDamage.GetValueOrDefault(id));
        candidates = candidates
            .Where(id => cycle.CreditedDamage.GetValueOrDefault(id) == highestDamage)
            .ToArray();
        var highestApplied = candidates.Max(id => cycle.Weights[id]);
        candidates = candidates.Where(id => cycle.Weights[id] == highestApplied).ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var mixed = deterministicEntropy + 0x9E3779B97F4A7C15UL;
        foreach (var candidate in candidates)
        {
            mixed ^= candidate + 0x9E3779B97F4A7C15UL + (mixed << 6) + (mixed >> 2);
        }
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        mixed ^= mixed >> 31;
        return candidates[mixed % (ulong)candidates.Length];
    }

    public void Clear() => _cycles.Clear();

    private static PoisonDamageAllocation EmptyAllocation(long damage) =>
        new(damage, new ReadOnlyDictionary<ulong, long>(new Dictionary<ulong, long>()), 0);

    private sealed class Participant
    {
        public Participant(ulong playerNetId, bool isUnattributed, ExactFraction due)
        {
            PlayerNetId = playerNetId;
            IsUnattributed = isUnattributed;
            Due = due;
        }

        public ulong PlayerNetId { get; }
        public bool IsUnattributed { get; }
        public ExactFraction Due { get; }
        public long Award { get; set; }
        public ExactFraction Residual => Due - Award;
    }
}
