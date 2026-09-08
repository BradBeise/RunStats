using System;
using System.Collections.Generic;
using System.Threading;
using RunStats.Models;
using RunStats.Multiplayer;

namespace RunStats.Persistence;

public sealed class RunStatsPersistenceCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly RunStatsState _state;
    private readonly Func<RunStatsSidecarStore> _storeFactory;
    private readonly Action<string, Exception> _onError;
    private readonly TimeSpan _pendingDebounce;
    private readonly Func<IReadOnlyList<AssistedOwnershipRecord>> _captureAssistedOwnership;
    private readonly Action<IReadOnlyList<AssistedOwnershipRecord>> _applyAssistedOwnership;
    private readonly Timer _pendingTimer;
    private PreparedSave? _preparedSave;
    private PendingSave? _pendingSave;
    private RunStatsSidecarStore? _pendingStore;
    private long _lastVanillaSaveTime;
    private bool _disposed;

    public RunStatsPersistenceCoordinator(
        RunStatsState state,
        Func<RunStatsSidecarStore> storeFactory,
        Action<string, Exception> onError,
        TimeSpan? pendingDebounce = null,
        Func<IReadOnlyList<AssistedOwnershipRecord>>? captureAssistedOwnership = null,
        Action<IReadOnlyList<AssistedOwnershipRecord>>? applyAssistedOwnership = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
        _captureAssistedOwnership = captureAssistedOwnership ??
            (() => Array.Empty<AssistedOwnershipRecord>());
        _applyAssistedOwnership = applyAssistedOwnership ?? (_ => { });
        _pendingDebounce = pendingDebounce ?? TimeSpan.FromSeconds(1);
        if (_pendingDebounce <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingDebounce));
        }
        _pendingTimer = new Timer(WritePending, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _state.Changed += OnStateChanged;
    }

    public SidecarLoadResult Restore(
        RunIdentity identity,
        long vanillaSaveTime,
        VanillaStatBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(baseline);
        lock (_gate)
        {
            _lastVanillaSaveTime = vanillaSaveTime;
        }

        var result = _storeFactory().TryLoadActive(
            identity,
            vanillaSaveTime,
            _state,
            out var ownership);
        if (result == SidecarLoadResult.Loaded)
        {
            _applyAssistedOwnership(ownership);
        }
        if (!baseline.TryMergeInto(_state))
        {
            return SidecarLoadResult.InvalidSnapshot;
        }

        return result;
    }

    public bool MergeBaseline(VanillaStatBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return baseline.TryMergeInto(_state);
    }

    public void PrepareVanillaSave(long vanillaSaveTime, RunMode mode)
    {
        var snapshot = _state.CaptureSnapshot();
        if (snapshot.Lifecycle != RunLifecycle.Active || snapshot.Identity?.Mode != mode ||
            vanillaSaveTime <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _preparedSave = new PreparedSave(
                snapshot,
                vanillaSaveTime,
                _captureAssistedOwnership());
        }
    }

    public void ConfirmVanillaSave()
    {
        PreparedSave? prepared;
        lock (_gate)
        {
            prepared = _preparedSave;
            _preparedSave = null;
            if (prepared is not null)
            {
                _lastVanillaSaveTime = prepared.VanillaSaveTime;
            }
        }

        if (prepared is not null)
        {
            _storeFactory().WriteActive(
                prepared.Snapshot,
                prepared.VanillaSaveTime,
                prepared.AssistedOwnership);
        }
    }

    public void ArchiveEnded(RunStatsSnapshot snapshot, string reason)
    {
        CancelPending();
        long saveTime;
        lock (_gate)
        {
            saveTime = _lastVanillaSaveTime;
        }

        _storeFactory().ArchiveSnapshot(
            snapshot,
            saveTime,
            reason,
            _captureAssistedOwnership());
    }

    public void ArchiveDeleted(RunMode mode, string reason)
    {
        CancelPending();
        _storeFactory().ArchiveActive(mode, reason);
    }

    public void ResetRun()
    {
        CancelPending();
        lock (_gate)
        {
            _preparedSave = null;
            _lastVanillaSaveTime = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state.Changed -= OnStateChanged;
        _pendingTimer.Dispose();
    }

    private void OnStateChanged()
    {
        var snapshot = _state.CaptureSnapshot();
        if (snapshot.Lifecycle != RunLifecycle.Active || snapshot.Identity is null)
        {
            return;
        }

        RunStatsSidecarStore store;
        try
        {
            // Resolve game/profile paths on the game thread, never from the timer callback.
            store = _storeFactory();
        }
        catch (Exception exception)
        {
            _onError("The pending sidecar path could not be resolved.", exception);
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingSave = new PendingSave(snapshot, _captureAssistedOwnership());
            _pendingStore = store;
            _pendingTimer.Change(_pendingDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void WritePending(object? _)
    {
        PendingSave? pending;
        RunStatsSidecarStore? store;
        lock (_gate)
        {
            pending = _pendingSave;
            store = _pendingStore;
            _pendingSave = null;
            _pendingStore = null;
        }

        if (pending is null || store is null)
        {
            return;
        }

        try
        {
            store.WritePending(pending.Snapshot, pending.AssistedOwnership);
        }
        catch (Exception exception)
        {
            _onError("The debounced pending sidecar checkpoint failed.", exception);
        }
    }

    private void CancelPending()
    {
        lock (_gate)
        {
            _pendingTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pendingSave = null;
            _pendingStore = null;
        }
    }

    private sealed record PreparedSave(
        RunStatsSnapshot Snapshot,
        long VanillaSaveTime,
        IReadOnlyList<AssistedOwnershipRecord> AssistedOwnership);

    private sealed record PendingSave(
        RunStatsSnapshot Snapshot,
        IReadOnlyList<AssistedOwnershipRecord> AssistedOwnership);
}
