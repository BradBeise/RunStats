using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunStats.Tracking;

public sealed record StrengthOutgoingCalculation(
    long EligibilityPool,
    long VulnerableLayerPool,
    IReadOnlyList<StrengthImpactCapacity> EventCapacities);

public sealed record StrengthIncomingCalculation(
    long EligibilityPool,
    long WeakLayerPool,
    IReadOnlyList<StrengthImpactCapacity> EventCapacities);

/// <summary>
/// Builds the exact HP-loss counterfactual for externally supplied Strength.
/// Vulnerable is removed first so its existing attribution owns only the outer
/// multiplicative layer. Event capacities are measured against Block and HP in
/// the same harmful-first, earliest-event order used by the ledger.
/// </summary>
public static class StrengthDamageCalculator
{
    public static bool TryCalculateOutgoing(
        decimal actualModifiedDamage,
        decimal vulnerableMultiplier,
        decimal strengthDownstreamMultiplier,
        int preHitBlock,
        int preHitHp,
        int actualHpRemoved,
        IReadOnlyList<StrengthImpactEvent> events,
        out StrengthOutgoingCalculation calculation)
    {
        calculation = EmptyCalculation();
        if (actualModifiedDamage < 0m || vulnerableMultiplier < 1m ||
            strengthDownstreamMultiplier <= 0m || preHitBlock < 0 ||
            preHitHp < 0 || actualHpRemoved < 0 || actualHpRemoved > preHitHp ||
            events is null || events.Count == 0 ||
            events.Any(item => item.ImpactValue == 0))
        {
            return false;
        }

        try
        {
            var noVulnerableDamage = actualModifiedDamage / vulnerableMultiplier;
            if (HpRemoved(actualModifiedDamage, preHitBlock, preHitHp) != actualHpRemoved)
            {
                return false;
            }

            var totalImpact = events.Aggregate(
                0L,
                static (current, item) => checked(current + item.ImpactValue));
            if (noVulnerableDamage == 0m && totalImpact != 0)
            {
                // The game's final zero clamp discards the exact unclamped value.
                return false;
            }

            var baselineDamage = noVulnerableDamage -
                checked(totalImpact * strengthDownstreamMultiplier);
            var baselineHp = HpRemoved(baselineDamage, preHitBlock, preHitHp);
            var noVulnerableHp = HpRemoved(noVulnerableDamage, preHitBlock, preHitHp);
            var eligibility = checked((long)noVulnerableHp - baselineHp);
            var vulnerableLayer = checked((long)actualHpRemoved - noVulnerableHp);

            var ordered = events
                .OrderBy(item => item.ImpactValue < 0 ? 0 : 1)
                .ThenBy(item => item.EventId)
                .ToArray();
            var capacities = new List<StrengthImpactCapacity>(ordered.Length);
            var runningDamage = baselineDamage;
            var runningHp = baselineHp;
            foreach (var impactEvent in ordered)
            {
                runningDamage = checked(
                    runningDamage + impactEvent.ImpactValue * strengthDownstreamMultiplier);
                var nextHp = HpRemoved(runningDamage, preHitBlock, preHitHp);
                capacities.Add(new StrengthImpactCapacity(
                    impactEvent.EventId,
                    checked((long)nextHp - runningHp)));
                runningHp = nextHp;
            }

            if (runningHp != noVulnerableHp ||
                capacities.Sum(item => item.SignedCapacity) != eligibility)
            {
                return false;
            }

            calculation = new StrengthOutgoingCalculation(
                eligibility,
                vulnerableLayer,
                new ReadOnlyCollection<StrengthImpactCapacity>(capacities));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryCalculateIncoming(
        decimal actualModifiedDamage,
        decimal? exactUnmodifiedDamage,
        long currentStrength,
        decimal weakMultiplier,
        decimal strengthDownstreamMultiplier,
        ulong protectedPlayerNetId,
        IReadOnlyList<StrengthImpactEvent> events,
        out StrengthIncomingCalculation calculation)
    {
        calculation = EmptyIncomingCalculation();
        if (actualModifiedDamage < 0m ||
            (exactUnmodifiedDamage.HasValue && exactUnmodifiedDamage.Value < 0m) ||
            weakMultiplier <= 0m ||
            weakMultiplier > 1m || strengthDownstreamMultiplier <= 0m ||
            protectedPlayerNetId == 0 || events is null || events.Count == 0 ||
            events.Any(item => item.ImpactValue == 0))
        {
            return false;
        }

        try
        {
            var selectedEvents = events
                .Where(item => item.ImpactorPlayerNetId != protectedPlayerNetId)
                .ToArray();
            if (selectedEvents.Length == 0)
            {
                return false;
            }

            var noWeakDamage = actualModifiedDamage / weakMultiplier;
            var totalImpact = selectedEvents.Aggregate(
                0L,
                static (current, item) => checked(current + item.ImpactValue));
            decimal counterfactualDamage;
            if (noWeakDamage == 0m && totalImpact != 0)
            {
                if (!exactUnmodifiedDamage.HasValue)
                {
                    return false;
                }

                var actualBeforeClamp = checked(
                    exactUnmodifiedDamage.Value + currentStrength);
                if (actualBeforeClamp > 0m)
                {
                    return false;
                }

                var counterfactualBeforeClamp = checked(
                    actualBeforeClamp - totalImpact);
                counterfactualDamage = checked(
                    Math.Max(counterfactualBeforeClamp, 0m) * strengthDownstreamMultiplier);
            }
            else
            {
                counterfactualDamage = noWeakDamage -
                    checked(totalImpact * strengthDownstreamMultiplier);
            }
            var actualPreBlock = PreBlockDamage(noWeakDamage);
            var counterfactualPreBlock = PreBlockDamage(counterfactualDamage);
            var eligibility = checked((long)counterfactualPreBlock - actualPreBlock);
            var weakLayer = checked(
                (long)actualPreBlock - PreBlockDamage(actualModifiedDamage));

            var ordered = selectedEvents
                .OrderBy(item => item.ImpactValue > 0 ? 0 : 1)
                .ThenBy(item => item.EventId)
                .ToArray();
            var capacities = new List<StrengthImpactCapacity>(ordered.Length);
            var runningDamage = counterfactualDamage;
            var runningPreBlock = counterfactualPreBlock;
            foreach (var impactEvent in ordered)
            {
                runningDamage = checked(
                    runningDamage + impactEvent.ImpactValue * strengthDownstreamMultiplier);
                var nextPreBlock = PreBlockDamage(runningDamage);
                capacities.Add(new StrengthImpactCapacity(
                    impactEvent.EventId,
                    checked((long)runningPreBlock - nextPreBlock)));
                runningPreBlock = nextPreBlock;
            }

            if (runningPreBlock != actualPreBlock ||
                capacities.Sum(item => item.SignedCapacity) != eligibility)
            {
                return false;
            }

            calculation = new StrengthIncomingCalculation(
                eligibility,
                weakLayer,
                new ReadOnlyCollection<StrengthImpactCapacity>(capacities));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int HpRemoved(decimal damage, int block, int hp)
    {
        var afterBlock = Math.Max(damage - block, 0m);
        var truncated = afterBlock <= 0m
            ? 0
            : (int)Math.Min(decimal.Truncate(afterBlock), 999999999m);
        return Math.Min(truncated, hp);
    }

    private static int PreBlockDamage(decimal damage) => damage <= 0m
        ? 0
        : (int)Math.Min(decimal.Truncate(damage), 999999999m);

    private static StrengthOutgoingCalculation EmptyCalculation() =>
        new(0, 0, Array.Empty<StrengthImpactCapacity>());

    private static StrengthIncomingCalculation EmptyIncomingCalculation() =>
        new(0, 0, Array.Empty<StrengthImpactCapacity>());
}
