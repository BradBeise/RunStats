using System;
using System.Collections.Generic;

namespace RunStats.Tracking;

public enum AssistedEffectKind
{
    Vulnerable = 0,
    Weak = 1
}

/// <summary>
/// Keeps Weak and Vulnerable ownership cycles separate while presenting one
/// mutation path to runtime power hooks.
/// </summary>
public sealed class AssistedContributionTracker
{
    private readonly AssistedContributionLedger _vulnerable = new();
    private readonly AssistedContributionLedger _weak = new();

    public AssistedContributionObservation ObserveAmount(
        AssistedEffectKind kind,
        object enemyToken,
        long previousAmount,
        long currentAmount,
        ulong? contributorNetId) =>
        Ledger(kind).ObserveAmount(
            enemyToken,
            previousAmount,
            currentAmount,
            contributorNetId);

    public ContributorResult ResolveContributor(
        AssistedEffectKind kind,
        object enemyToken) =>
        Ledger(kind).ResolveContributor(enemyToken);

    public bool TryAllocate(
        AssistedEffectKind kind,
        object enemyToken,
        long assistPool,
        ulong? selfBeneficiaryNetId,
        out AssistedShareAllocation allocation) =>
        Ledger(kind).TryAllocate(
            enemyToken,
            assistPool,
            selfBeneficiaryNetId,
            out allocation);

    public bool TryAllocateWeakCommand(
        object enemyToken,
        long preventionPool,
        IReadOnlyDictionary<ulong, long> selfPreventionPools,
        out AssistedShareAllocation allocation) =>
        _weak.TryAllocateWithSelfFractions(
            enemyToken,
            preventionPool,
            selfPreventionPools,
            out allocation);

    public bool ResetCycle(AssistedEffectKind kind, object enemyToken) =>
        Ledger(kind).ResetCycle(enemyToken);

    public void ResetCombat()
    {
        _vulnerable.Clear();
        _weak.Clear();
    }

    private AssistedContributionLedger Ledger(AssistedEffectKind kind) => kind switch
    {
        AssistedEffectKind.Vulnerable => _vulnerable,
        AssistedEffectKind.Weak => _weak,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
