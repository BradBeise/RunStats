using System;
using System.Collections.Generic;
using System.Linq;

namespace RunStats.Tracking;

public readonly record struct StrengthTargetSnapshot(long Amount, ulong? PlayerNetId);

public readonly record struct StrengthObservationResult(
    bool Accepted,
    bool Deferred,
    bool Reset,
    long? EventId);

public readonly record struct StrengthActionCompletion(
    bool Accepted,
    int EventsCreated,
    int TargetsReset);

/// <summary>
/// Reconciles final Strength mutations with deterministic player-action snapshots
/// before committing them to the combat-local event ledger.
/// </summary>
public sealed class StrengthContributionTracker
{
    private enum PendingKind
    {
        Change,
        Reset,
        ExpireSource
    }

    private sealed record PendingMutation(
        PendingKind Kind,
        object Target,
        ulong? ImpactedPlayerNetId,
        long PreviousAmount,
        long CurrentAmount,
        ulong? DirectContributorNetId,
        ulong? DelayedSourceContributorNetId,
        int RemainingTurns,
        StrengthImpactExpiryKind ExpiryKind,
        object? RestorationSource);

    private sealed class ActionScope
    {
        public required ulong OwnerNetId { get; init; }
        public Dictionary<object, StrengthTargetSnapshot> Before { get; } =
            new(ReferenceEqualityComparer.Instance);
        public List<PendingMutation> Mutations { get; } = new();
    }

    private readonly StrengthImpactLedger _ledger;
    private readonly Dictionary<object, ActionScope> _actions =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, ulong?> _sourceOwners =
        new(ReferenceEqualityComparer.Instance);

    public StrengthContributionTracker(StrengthImpactLedger ledger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public StrengthImpactLedger Ledger => _ledger;

    public bool BeginAction(
        object actionToken,
        ulong ownerNetId,
        IReadOnlyDictionary<object, StrengthTargetSnapshot> before)
    {
        ArgumentNullException.ThrowIfNull(actionToken);
        ArgumentNullException.ThrowIfNull(before);
        if (ownerNetId == 0 || _actions.ContainsKey(actionToken))
        {
            return false;
        }

        var scope = new ActionScope { OwnerNetId = ownerNetId };
        foreach (var entry in before)
        {
            if (entry.Key is null)
            {
                return false;
            }

            scope.Before.Add(entry.Key, entry.Value);
        }

        _actions.Add(actionToken, scope);
        return true;
    }

    public StrengthObservationResult ObserveAmount(
        object? actionToken,
        object target,
        ulong? impactedPlayerNetId,
        long previousAmount,
        long currentAmount,
        ulong? directContributorNetId,
        ulong? delayedSourceContributorNetId,
        int remainingTurns,
        StrengthImpactExpiryKind expiryKind,
        object? restorationSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        long delta;
        try
        {
            delta = checked(currentAmount - previousAmount);
        }
        catch (OverflowException)
        {
            return default;
        }

        if (delta == 0)
        {
            return new StrengthObservationResult(true, false, false, null);
        }

        var mutation = new PendingMutation(
            currentAmount == 0 ? PendingKind.Reset : PendingKind.Change,
            target,
            impactedPlayerNetId,
            previousAmount,
            currentAmount,
            directContributorNetId,
            delayedSourceContributorNetId,
            remainingTurns,
            expiryKind,
            restorationSource);

        if (actionToken is not null && _actions.TryGetValue(actionToken, out var scope))
        {
            if (!scope.Before.ContainsKey(target))
            {
                scope.Before.Add(target, new StrengthTargetSnapshot(previousAmount, impactedPlayerNetId));
            }

            scope.Mutations.Add(mutation);
            return new StrengthObservationResult(true, true, mutation.Kind == PendingKind.Reset, null);
        }

        return Commit(mutation, actionOwnerNetId: null);
    }

    public bool ObserveSourceOwner(object sourceToken, ulong contributorNetId)
    {
        ArgumentNullException.ThrowIfNull(sourceToken);
        if (contributorNetId == 0)
        {
            return false;
        }

        if (!_sourceOwners.TryGetValue(sourceToken, out var existing))
        {
            _sourceOwners.Add(sourceToken, contributorNetId);
            return true;
        }

        if (existing == contributorNetId)
        {
            return true;
        }

        _sourceOwners[sourceToken] = null;
        return false;
    }

    public ulong? ResolveSourceOwner(object sourceToken)
    {
        ArgumentNullException.ThrowIfNull(sourceToken);
        return _sourceOwners.GetValueOrDefault(sourceToken);
    }

    public void ForgetSource(object sourceToken)
    {
        ArgumentNullException.ThrowIfNull(sourceToken);
        _sourceOwners.Remove(sourceToken);
    }

    public StrengthActionCompletion CompleteAction(
        object actionToken,
        IReadOnlyDictionary<object, StrengthTargetSnapshot> after)
    {
        ArgumentNullException.ThrowIfNull(actionToken);
        ArgumentNullException.ThrowIfNull(after);
        if (!_actions.Remove(actionToken, out var scope))
        {
            return default;
        }

        var afterByReference = new Dictionary<object, StrengthTargetSnapshot>(
            ReferenceEqualityComparer.Instance);
        foreach (var entry in after)
        {
            afterByReference.Add(entry.Key, entry.Value);
        }

        var invalidTargets = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var noNetChangeTargets = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var lastResetIndexes = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var group in scope.Mutations.GroupBy(
                     mutation => mutation.Target,
                     ReferenceEqualityComparer.Instance))
        {
            if (!scope.Before.TryGetValue(group.Key, out var beforeState) ||
                !afterByReference.TryGetValue(group.Key, out var afterState))
            {
                invalidTargets.Add(group.Key);
                continue;
            }

            try
            {
                var observedDelta = group.Aggregate(
                    0L,
                    (total, mutation) => checked(
                        total + (mutation.CurrentAmount - mutation.PreviousAmount)));
                if (checked(afterState.Amount - beforeState.Amount) != observedDelta)
                {
                    invalidTargets.Add(group.Key);
                }
                else if (observedDelta == 0 &&
                         group.All(mutation => mutation.Kind == PendingKind.Change))
                {
                    noNetChangeTargets.Add(group.Key);
                }
            }
            catch (OverflowException)
            {
                invalidTargets.Add(group.Key);
            }
        }

        var created = 0;
        var reset = 0;
        for (var index = 0; index < scope.Mutations.Count; index++)
        {
            if (scope.Mutations[index].Kind == PendingKind.Reset)
            {
                lastResetIndexes[scope.Mutations[index].Target] = index;
            }
        }

        foreach (var target in invalidTargets)
        {
            if (_ledger.ResetTarget(target))
            {
                reset++;
            }
        }

        for (var index = 0; index < scope.Mutations.Count; index++)
        {
            var mutation = scope.Mutations[index];
            if (invalidTargets.Contains(mutation.Target))
            {
                continue;
            }

            if (noNetChangeTargets.Contains(mutation.Target) ||
                (lastResetIndexes.TryGetValue(mutation.Target, out var lastResetIndex) &&
                 index < lastResetIndex))
            {
                continue;
            }

            var result = Commit(mutation, scope.OwnerNetId);
            if (!result.Accepted)
            {
                if (_ledger.ResetTarget(mutation.Target))
                {
                    reset++;
                }
                invalidTargets.Add(mutation.Target);
                continue;
            }

            if (result.EventId.HasValue)
            {
                created++;
            }

            if (result.Reset)
            {
                reset++;
            }
        }

        return new StrengthActionCompletion(invalidTargets.Count == 0, created, reset);
    }

    public bool CancelAction(object actionToken)
    {
        ArgumentNullException.ThrowIfNull(actionToken);
        return _actions.Remove(actionToken);
    }

    public bool ExpireSource(object? actionToken, object target, object sourceToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceToken);
        if (actionToken is not null && _actions.TryGetValue(actionToken, out var scope))
        {
            scope.Mutations.Add(new PendingMutation(
                PendingKind.ExpireSource,
                target,
                null,
                0,
                0,
                null,
                null,
                -1,
                StrengthImpactExpiryKind.SourceBound,
                sourceToken));
            return true;
        }

        _ledger.ExpireSource(target, sourceToken);
        ForgetSource(sourceToken);
        return true;
    }

    public void ResetCombat()
    {
        _actions.Clear();
        _sourceOwners.Clear();
        _ledger.Clear();
    }

    private StrengthObservationResult Commit(PendingMutation mutation, ulong? actionOwnerNetId)
    {
        if (mutation.Kind == PendingKind.Reset)
        {
            return new StrengthObservationResult(
                true,
                false,
                _ledger.ResetTarget(mutation.Target),
                null);
        }

        if (mutation.Kind == PendingKind.ExpireSource)
        {
            _ledger.ExpireSource(mutation.Target, mutation.RestorationSource!);
            ForgetSource(mutation.RestorationSource!);
            return new StrengthObservationResult(true, false, true, null);
        }

        long delta;
        try
        {
            delta = checked(mutation.CurrentAmount - mutation.PreviousAmount);
        }
        catch (OverflowException)
        {
            return default;
        }

        if (mutation.RestorationSource is not null &&
            TryApplyRestoration(mutation.Target, mutation.RestorationSource, delta))
        {
            return new StrengthObservationResult(true, false, true, null);
        }

        var contributor = mutation.DelayedSourceContributorNetId is > 0
            ? mutation.DelayedSourceContributorNetId
            : actionOwnerNetId is > 0
                ? actionOwnerNetId
                : mutation.DirectContributorNetId;
        if (contributor is not > 0)
        {
            return new StrengthObservationResult(true, false, false, null);
        }

        if (!_ledger.TryAdd(
                contributor.Value,
                mutation.Target,
                mutation.ImpactedPlayerNetId,
                delta,
                mutation.RemainingTurns,
                mutation.ExpiryKind,
                mutation.RestorationSource,
                out var created))
        {
            // Self changes are deliberately ignored, not treated as malformed.
            return mutation.ImpactedPlayerNetId == contributor
                ? new StrengthObservationResult(true, false, false, null)
                : default;
        }

        if (mutation.RestorationSource is not null)
        {
            ObserveSourceOwner(mutation.RestorationSource, contributor.Value);
        }

        return new StrengthObservationResult(true, false, false, created!.EventId);
    }

    private bool TryApplyRestoration(object target, object sourceToken, long delta)
    {
        if (delta == 0 || delta == long.MinValue)
        {
            return false;
        }

        var linked = _ledger.GetEvents(target)
            .Where(item => item.RestorationSource is not null &&
                           ReferenceEquals(item.RestorationSource, sourceToken) &&
                           Math.Sign(item.ImpactValue) == -Math.Sign(delta))
            .ToArray();
        if (linked.Length == 0)
        {
            return false;
        }

        var magnitude = Math.Abs(delta);
        long total;
        try
        {
            total = linked.Aggregate(0L, (sum, item) => checked(sum + Math.Abs(item.ImpactValue)));
        }
        catch (OverflowException)
        {
            _ledger.ResetTarget(target);
            return true;
        }

        if (magnitude == total)
        {
            _ledger.ExpireSource(target, sourceToken);
            return true;
        }

        if (linked.Length == 1 && magnitude < total)
        {
            return _ledger.TryReduceEvent(linked[0].EventId, magnitude);
        }

        // Merged temporary stacks do not retain which player's portion was
        // partially restored. Clear the target rather than inventing ownership.
        _ledger.ResetTarget(target);
        return true;
    }
}
