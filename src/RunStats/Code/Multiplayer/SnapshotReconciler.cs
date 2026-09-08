using System;
using System.Collections.Generic;
using System.Linq;
using RunStats.Models;
using RunStats.Tracking;

namespace RunStats.Multiplayer;

public sealed class SnapshotReconciler
{
    public const int MaxAssistedOwnershipEntries = 64;

    public long LastAcceptedSequence { get; private set; }

    public SnapshotReconcileResult TryApply(
        ulong senderNetId,
        ulong hostNetId,
        StatsSyncSnapshot snapshot,
        RunStatsState state)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(state);

        if (senderNetId != hostNetId)
        {
            return SnapshotReconcileResult.UnauthorizedSender;
        }

        if (snapshot.ProtocolVersion != StatsSyncSnapshot.CurrentProtocolVersion)
        {
            return SnapshotReconcileResult.ProtocolMismatch;
        }

        if (snapshot.Sequence <= 0)
        {
            return SnapshotReconcileResult.InvalidSequence;
        }

        if (snapshot.Sequence <= LastAcceptedSequence)
        {
            return SnapshotReconcileResult.StaleOrDuplicate;
        }

        if (!ValidateAssistedOwnership(snapshot.AssistedOwnership, snapshot.Stats.Identity))
        {
            return SnapshotReconcileResult.InvalidAssistedOwnership;
        }

        if (state.TryReplaceFromSnapshot(snapshot.Stats) != SnapshotImportResult.Applied)
        {
            return SnapshotReconcileResult.SnapshotRejected;
        }

        LastAcceptedSequence = snapshot.Sequence;
        return SnapshotReconcileResult.Applied;
    }

    public void Reset() => LastAcceptedSequence = 0;

    public static bool ValidateAssistedOwnership(
        IReadOnlyList<AssistedOwnershipRecord> entries,
        RunIdentity? identity)
    {
        if (identity is null || entries.Count > MaxAssistedOwnershipEntries)
        {
            return false;
        }

        var keys = new HashSet<(uint, AssistedPowerKind)>();
        foreach (var entry in entries)
        {
            if (!Enum.IsDefined(entry.PowerKind) ||
                !Enum.IsDefined(entry.Resolution) ||
                !keys.Add((entry.OwnerCombatId, entry.PowerKind)))
            {
                return false;
            }

            if (entry.Resolution == ContributorResolution.Unique)
            {
                if (!identity.PlayerNetIds.Contains(entry.ContributorNetId))
                {
                    return false;
                }
            }
            else if (entry.ContributorNetId != 0)
            {
                return false;
            }
        }

        return true;
    }
}
