using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunStats.Models;

public sealed class RunStatsState
{
    private readonly Dictionary<ulong, PlayerStats> _players = new();
    private readonly Dictionary<DiagnosticKind, long> _diagnostics = new();

    public RunStatsState()
    {
        InitializeDiagnostics();
    }

    public RunLifecycle Lifecycle { get; private set; } = RunLifecycle.Empty;

    public RunIdentity? Identity { get; private set; }

    public long Revision { get; private set; }

    public event Action? Changed;

    public void StartRun(RunIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        _players.Clear();
        foreach (var playerNetId in identity.PlayerNetIds)
        {
            _players.Add(playerNetId, new PlayerStats(playerNetId));
        }

        _diagnostics.Clear();
        InitializeDiagnostics();
        Identity = identity;
        Revision = 0;
        Lifecycle = RunLifecycle.Active;
        Changed?.Invoke();
    }

    public MutationResult TryApply(StatMutation mutation)
    {
        if (Lifecycle != RunLifecycle.Active)
        {
            return MutationResult.NoActiveRun;
        }

        if (!_players.TryGetValue(mutation.PlayerNetId, out var player))
        {
            return MutationResult.UnknownPlayer;
        }

        if (Revision == long.MaxValue)
        {
            return MutationResult.Overflow;
        }

        var result = player.TryApply(mutation);
        if (result == MutationResult.Applied)
        {
            Revision++;
            Changed?.Invoke();
        }

        return result;
    }

    public MutationResult TryApplyBatch(IReadOnlyList<StatMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        if (Lifecycle != RunLifecycle.Active)
        {
            return MutationResult.NoActiveRun;
        }

        if (mutations.Count == 0)
        {
            return MutationResult.InvalidAmount;
        }

        var replacements = new Dictionary<ulong, PlayerStats>();
        try
        {
            var nextRevision = checked(Revision + mutations.Count);
            foreach (var mutation in mutations)
            {
                if (!_players.TryGetValue(mutation.PlayerNetId, out var current))
                {
                    return MutationResult.UnknownPlayer;
                }

                if (!replacements.TryGetValue(mutation.PlayerNetId, out var replacement))
                {
                    if (!PlayerStats.TryCreateFromSnapshot(
                            current.CaptureSnapshot(),
                            out replacement))
                    {
                        return MutationResult.InvalidAmount;
                    }

                    replacements.Add(mutation.PlayerNetId, replacement!);
                }

                var result = replacement!.TryApply(mutation);
                if (result != MutationResult.Applied)
                {
                    return result;
                }
            }

            foreach (var pair in replacements)
            {
                _players[pair.Key] = pair.Value;
            }

            Revision = nextRevision;
            Changed?.Invoke();
            return MutationResult.Applied;
        }
        catch (OverflowException)
        {
            return MutationResult.Overflow;
        }
    }

    public MutationResult TryRecordDiagnostic(DiagnosticKind kind, long amount = 1)
    {
        if (Lifecycle != RunLifecycle.Active)
        {
            return MutationResult.NoActiveRun;
        }

        if (!Enum.IsDefined(kind))
        {
            return MutationResult.InvalidStat;
        }

        if (amount <= 0)
        {
            return MutationResult.InvalidAmount;
        }

        try
        {
            var nextValue = checked(_diagnostics[kind] + amount);
            var nextRevision = checked(Revision + 1);
            _diagnostics[kind] = nextValue;
            Revision = nextRevision;
            Changed?.Invoke();
            return MutationResult.Applied;
        }
        catch (OverflowException)
        {
            return MutationResult.Overflow;
        }
    }

    public bool TryEndRun()
    {
        if (Lifecycle != RunLifecycle.Active || Revision == long.MaxValue)
        {
            return false;
        }

        Revision++;
        Lifecycle = RunLifecycle.Ended;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _players.Clear();
        _diagnostics.Clear();
        InitializeDiagnostics();
        Identity = null;
        Revision = 0;
        Lifecycle = RunLifecycle.Empty;
        Changed?.Invoke();
    }

    public RunStatsSnapshot CaptureSnapshot()
    {
        var players = new Dictionary<ulong, PlayerStatsSnapshot>();
        foreach (var pair in _players)
        {
            players.Add(pair.Key, pair.Value.CaptureSnapshot());
        }

        return new RunStatsSnapshot(
            RunStatsSnapshot.CurrentSchemaVersion,
            Lifecycle,
            Identity,
            Revision,
            new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(players),
            new ReadOnlyDictionary<DiagnosticKind, long>(
                new Dictionary<DiagnosticKind, long>(_diagnostics)));
    }

    public SnapshotImportResult TryReplaceFromSnapshot(RunStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Lifecycle != RunLifecycle.Active || Identity is null)
        {
            return SnapshotImportResult.NoActiveRun;
        }

        if (snapshot.SchemaVersion != RunStatsSnapshot.CurrentSchemaVersion)
        {
            return SnapshotImportResult.SchemaMismatch;
        }

        if (!Identity.Equals(snapshot.Identity))
        {
            return SnapshotImportResult.RunIdentityMismatch;
        }

        if (snapshot.Lifecycle is not (RunLifecycle.Active or RunLifecycle.Ended))
        {
            return SnapshotImportResult.InvalidLifecycle;
        }

        if (snapshot.Revision < 0)
        {
            return SnapshotImportResult.InvalidRevision;
        }

        if (snapshot.Players.Count != Identity.PlayerNetIds.Count ||
            snapshot.Players.Count > byte.MaxValue ||
            !snapshot.Players.Keys.OrderBy(id => id).SequenceEqual(Identity.PlayerNetIds))
        {
            return SnapshotImportResult.InvalidPlayers;
        }

        var restoredPlayers = new Dictionary<ulong, PlayerStats>();
        foreach (var pair in snapshot.Players)
        {
            if (pair.Key != pair.Value.PlayerNetId ||
                pair.Value.Revision > snapshot.Revision ||
                !PlayerStats.TryCreateFromSnapshot(pair.Value, out var player))
            {
                return SnapshotImportResult.InvalidPlayers;
            }

            restoredPlayers.Add(pair.Key, player!);
        }

        if (snapshot.Diagnostics.Count != Enum.GetValues<DiagnosticKind>().Length)
        {
            return SnapshotImportResult.InvalidDiagnostics;
        }

        var restoredDiagnostics = new Dictionary<DiagnosticKind, long>();
        foreach (var kind in Enum.GetValues<DiagnosticKind>())
        {
            if (!snapshot.Diagnostics.TryGetValue(kind, out var count) || count < 0)
            {
                return SnapshotImportResult.InvalidDiagnostics;
            }

            restoredDiagnostics.Add(kind, count);
        }

        _players.Clear();
        foreach (var pair in restoredPlayers)
        {
            _players.Add(pair.Key, pair.Value);
        }

        _diagnostics.Clear();
        foreach (var pair in restoredDiagnostics)
        {
            _diagnostics.Add(pair.Key, pair.Value);
        }

        Revision = snapshot.Revision;
        Lifecycle = snapshot.Lifecycle;
        Changed?.Invoke();
        return SnapshotImportResult.Applied;
    }

    private void InitializeDiagnostics()
    {
        foreach (var kind in Enum.GetValues<DiagnosticKind>())
        {
            _diagnostics[kind] = 0;
        }
    }
}
