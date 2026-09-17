using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace RunStats.Tracking;

public readonly record struct AssistedContributionObservation(
    bool Accepted,
    long CreditedContribution,
    long UnattributedContribution,
    bool CycleReset);

public sealed record AssistedShareAllocation(
    long AssistPool,
    IReadOnlyDictionary<ulong, long> PlayerAwards,
    long UnattributedAward,
    long DiscardedSelfAward)
{
    public long CreditedAward => PlayerAwards.Values.Sum();
}

/// <summary>
/// Tracks cumulative, per-source contribution weights for one effect independently
/// on each reference-identified target. Natural decay does not change the weights;
/// reaching zero starts a fresh ownership cycle.
/// </summary>
public sealed class AssistedContributionLedger
{
    private readonly record struct ContributorBucket(ulong PlayerNetId, bool IsUnattributed)
    {
        public static ContributorBucket ForPlayer(ulong playerNetId) => new(playerNetId, false);
        public static ContributorBucket Unattributed => new(0, true);
    }

    private sealed class Cycle
    {
        public long ObservedAmount;
        public long UnattributedWeight;
        public Dictionary<ulong, long> PlayerWeights { get; } = new();
        public Dictionary<ulong, ExactFraction> SelfSplitCarries { get; } = new();
        public List<ContributorBucket> RemainderOrder { get; } = new();
        public int NextRemainderIndex;
    }

    private readonly Dictionary<object, Cycle> _cycles =
        new(ReferenceEqualityComparer.Instance);

    public AssistedContributionObservation ObserveAmount(
        object targetToken,
        long previousAmount,
        long currentAmount,
        ulong? contributorNetId)
    {
        ArgumentNullException.ThrowIfNull(targetToken);
        if (previousAmount < 0 || currentAmount < 0)
        {
            return default;
        }

        _cycles.TryGetValue(targetToken, out var existing);
        if (existing is not null && existing.ObservedAmount != previousAmount)
        {
            return default;
        }

        if (currentAmount == 0)
        {
            var reset = _cycles.Remove(targetToken);
            return new AssistedContributionObservation(true, 0, 0, reset);
        }

        var delta = currentAmount - previousAmount;
        var knownContributor = contributorNetId is > 0;
        try
        {
            var cycle = Clone(existing, previousAmount);
            if (delta > 0)
            {
                var bucket = knownContributor
                    ? ContributorBucket.ForPlayer(contributorNetId!.Value)
                    : ContributorBucket.Unattributed;

                if (knownContributor)
                {
                    cycle.PlayerWeights.TryGetValue(contributorNetId!.Value, out var weight);
                    cycle.PlayerWeights[contributorNetId.Value] = checked(weight + delta);
                }
                else
                {
                    cycle.UnattributedWeight = checked(cycle.UnattributedWeight + delta);
                }

                cycle.RemainderOrder.Remove(bucket);
                cycle.RemainderOrder.Insert(0, bucket);
                cycle.NextRemainderIndex = 0;
            }

            cycle.ObservedAmount = currentAmount;
            _cycles[targetToken] = cycle;
            return new AssistedContributionObservation(
                true,
                knownContributor && delta > 0 ? delta : 0,
                !knownContributor && delta > 0 ? delta : 0,
                false);
        }
        catch (OverflowException)
        {
            return default;
        }
    }

    public bool TryAllocate(
        object targetToken,
        long assistPool,
        ulong? selfBeneficiaryNetId,
        out AssistedShareAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(targetToken);
        allocation = EmptyAllocation(assistPool);
        if (assistPool < 0 || !_cycles.TryGetValue(targetToken, out var cycle))
        {
            return false;
        }

        BigInteger totalWeight = cycle.UnattributedWeight;
        foreach (var weight in cycle.PlayerWeights.Values)
        {
            totalWeight += weight;
        }

        if (totalWeight <= BigInteger.Zero || cycle.RemainderOrder.Count == 0)
        {
            return false;
        }

        var awards = new Dictionary<ContributorBucket, long>();
        long assigned = 0;
        foreach (var bucket in cycle.RemainderOrder)
        {
            var weight = bucket.IsUnattributed
                ? cycle.UnattributedWeight
                : cycle.PlayerWeights.GetValueOrDefault(bucket.PlayerNetId);
            var floor = (new BigInteger(assistPool) * weight) / totalWeight;
            var award = (long)floor;
            awards.Add(bucket, award);
            assigned = checked(assigned + award);
        }

        var remainder = assistPool - assigned;
        var nextIndex = cycle.NextRemainderIndex;
        for (long point = 0; point < remainder; point++)
        {
            var bucket = cycle.RemainderOrder[nextIndex];
            awards[bucket] = checked(awards[bucket] + 1);
            nextIndex = (nextIndex + 1) % cycle.RemainderOrder.Count;
        }

        var playerAwards = new Dictionary<ulong, long>();
        long unattributedAward = 0;
        long discardedSelfAward = 0;
        foreach (var entry in awards)
        {
            if (entry.Key.IsUnattributed)
            {
                unattributedAward = entry.Value;
            }
            else if (selfBeneficiaryNetId is > 0 &&
                     entry.Key.PlayerNetId == selfBeneficiaryNetId.Value)
            {
                discardedSelfAward = entry.Value;
            }
            else
            {
                playerAwards.Add(entry.Key.PlayerNetId, entry.Value);
            }
        }

        cycle.NextRemainderIndex = nextIndex;
        allocation = new AssistedShareAllocation(
            assistPool,
            new ReadOnlyDictionary<ulong, long>(playerAwards),
            unattributedAward,
            discardedSelfAward);
        return true;
    }

    public ContributorResult ResolveContributor(object targetToken)
    {
        ArgumentNullException.ThrowIfNull(targetToken);
        if (!_cycles.TryGetValue(targetToken, out var cycle))
        {
            return ContributorResult.Unsupported;
        }

        if (cycle.UnattributedWeight == 0 && cycle.PlayerWeights.Count == 1)
        {
            return ContributorResult.Unique(cycle.PlayerWeights.Keys.Single());
        }

        return cycle.PlayerWeights.Count == 0
            ? ContributorResult.Unsupported
            : ContributorResult.Ambiguous;
    }

    public bool TryAllocateWithSelfFractions(
        object targetToken,
        long assistPool,
        IReadOnlyDictionary<ulong, long> selfBenefitPools,
        out AssistedShareAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(targetToken);
        ArgumentNullException.ThrowIfNull(selfBenefitPools);
        allocation = EmptyAllocation(assistPool);
        if (assistPool <= 0 || selfBenefitPools.Any(entry =>
                entry.Key == 0 || entry.Value < 0 || entry.Value > assistPool))
        {
            return false;
        }

        if (!TryAllocate(targetToken, assistPool, null, out var grossAllocation) ||
            !_cycles.TryGetValue(targetToken, out var cycle))
        {
            return false;
        }

        var creditedAwards = new Dictionary<ulong, long>();
        var nextCarries = new Dictionary<ulong, ExactFraction>(cycle.SelfSplitCarries);
        long discardedSelfAward = 0;
        foreach (var entry in grossAllocation.PlayerAwards)
        {
            var carry = cycle.SelfSplitCarries.TryGetValue(entry.Key, out var existingCarry)
                ? existingCarry
                : ExactFraction.Zero;
            var selfBenefit = selfBenefitPools.GetValueOrDefault(entry.Key);
            var due = carry + ExactFraction.FromProduct(
                entry.Value,
                selfBenefit,
                assistPool);
            var discarded = (long)due.Floor();
            if (discarded < 0 || discarded > entry.Value)
            {
                return false;
            }

            creditedAwards.Add(entry.Key, entry.Value - discarded);
            discardedSelfAward = checked(discardedSelfAward + discarded);
            nextCarries[entry.Key] = due - discarded;
        }

        cycle.SelfSplitCarries.Clear();
        foreach (var entry in nextCarries)
        {
            cycle.SelfSplitCarries.Add(entry.Key, entry.Value);
        }

        allocation = new AssistedShareAllocation(
            assistPool,
            new ReadOnlyDictionary<ulong, long>(creditedAwards),
            grossAllocation.UnattributedAward,
            discardedSelfAward);
        return true;
    }

    public bool ResetCycle(object targetToken)
    {
        ArgumentNullException.ThrowIfNull(targetToken);
        return _cycles.Remove(targetToken);
    }

    public void Clear() => _cycles.Clear();

    private static Cycle Clone(Cycle? source, long previousAmount)
    {
        var clone = new Cycle { ObservedAmount = previousAmount };
        if (source is null)
        {
            if (previousAmount > 0)
            {
                clone.UnattributedWeight = previousAmount;
                clone.RemainderOrder.Add(ContributorBucket.Unattributed);
            }

            return clone;
        }

        clone.UnattributedWeight = source.UnattributedWeight;
        clone.NextRemainderIndex = source.NextRemainderIndex;
        foreach (var entry in source.PlayerWeights)
        {
            clone.PlayerWeights.Add(entry.Key, entry.Value);
        }

        foreach (var entry in source.SelfSplitCarries)
        {
            clone.SelfSplitCarries.Add(entry.Key, entry.Value);
        }

        clone.RemainderOrder.AddRange(source.RemainderOrder);
        return clone;
    }

    private static AssistedShareAllocation EmptyAllocation(long assistPool) =>
        new(
            assistPool,
            new ReadOnlyDictionary<ulong, long>(new Dictionary<ulong, long>()),
            0,
            0);
}
