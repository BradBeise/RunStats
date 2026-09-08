using System;

namespace RunStats.Tracking;

public static class AssistedDamageCalculator
{
    public static long DamageAdded(
        decimal actualModifiedDamage,
        decimal contributorMultiplier,
        int preHitBlock,
        int preHitHp,
        int actualHpRemoved)
    {
        if (actualModifiedDamage < 0m || contributorMultiplier <= 1m ||
            preHitBlock < 0 || preHitHp < 0 || actualHpRemoved <= 0)
        {
            return 0;
        }

        var counterfactualDamage = actualModifiedDamage / contributorMultiplier;
        var counterfactualAfterBlock = Math.Max(counterfactualDamage - preHitBlock, 0m);
        var counterfactualHpRemoved = Math.Min(
            DecimalToNonNegativeInt(counterfactualAfterBlock),
            preHitHp);
        return Math.Max((long)actualHpRemoved - counterfactualHpRemoved, 0L);
    }

    public static long DamagePrevented(
        decimal actualModifiedDamage,
        decimal contributorMultiplier)
    {
        if (actualModifiedDamage < 0m || contributorMultiplier <= 0m ||
            contributorMultiplier >= 1m)
        {
            return 0;
        }

        var actualIncoming = DecimalToNonNegativeInt(actualModifiedDamage);
        var counterfactualIncoming = DecimalToNonNegativeInt(
            actualModifiedDamage / contributorMultiplier);
        return Math.Max((long)counterfactualIncoming - actualIncoming, 0L);
    }

    private static int DecimalToNonNegativeInt(decimal value)
    {
        if (value <= 0m)
        {
            return 0;
        }

        return (int)Math.Min(decimal.Truncate(value), 999999999m);
    }
}
