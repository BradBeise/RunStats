using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using RunStats.Infrastructure;
using RunStats.Models;
using RunStats.Multiplayer.Messages;

namespace RunStats.Multiplayer;

public sealed class StatsSync : IDisposable
{
    private readonly RunStatsState _state;
    private readonly Func<IReadOnlyList<AssistedOwnershipRecord>> _captureAssistedOwnership;
    private readonly Action<IReadOnlyList<AssistedOwnershipRecord>> _applyAssistedOwnership;
    private readonly SnapshotReconciler _reconciler = new();
    private readonly Dictionary<ulong, long> _lastRequestByPlayer = new();
    private INetGameService? _netService;
    private long _hostSequence;
    private long _requestSequence;

    public StatsSync(
        RunStatsState state,
        Func<IReadOnlyList<AssistedOwnershipRecord>> captureAssistedOwnership,
        Action<IReadOnlyList<AssistedOwnershipRecord>> applyAssistedOwnership)
    {
        _state = state;
        _captureAssistedOwnership = captureAssistedOwnership;
        _applyAssistedOwnership = applyAssistedOwnership;
    }

    public void Start(INetGameService netService)
    {
        Dispose();
        if (netService.Type is not (NetGameType.Host or NetGameType.Client))
        {
            return;
        }

        _netService = netService;
        _hostSequence = 0;
        _requestSequence = 0;
        _reconciler.Reset();
        _lastRequestByPlayer.Clear();

        if (netService.Type == NetGameType.Host)
        {
            netService.RegisterMessageHandler<StatsSnapshotRequestMessage>(OnSnapshotRequested);
            BroadcastCheckpoint();
        }
        else
        {
            netService.RegisterMessageHandler<StatsSnapshotMessage>(OnSnapshotReceived);
            RequestSnapshot();
        }
    }

    public void BroadcastCheckpoint()
    {
        if (_netService?.Type != NetGameType.Host || !_netService.IsConnected)
        {
            return;
        }

        if (!TryCreateSnapshot(0, out var message))
        {
            return;
        }

        _netService.SendMessage(message!);
    }

    public void RequestSnapshot()
    {
        if (_netService?.Type != NetGameType.Client || !_netService.IsConnected ||
            _requestSequence == long.MaxValue)
        {
            return;
        }

        _requestSequence++;
        _netService.SendMessage(new StatsSnapshotRequestMessage
        {
            ProtocolVersion = StatsSyncSnapshot.CurrentProtocolVersion,
            RequestSequence = _requestSequence,
            ClientRevision = _state.Revision
        });
    }

    public void Dispose()
    {
        if (_netService?.Type == NetGameType.Host)
        {
            _netService.UnregisterMessageHandler<StatsSnapshotRequestMessage>(OnSnapshotRequested);
        }
        else if (_netService?.Type == NetGameType.Client)
        {
            _netService.UnregisterMessageHandler<StatsSnapshotMessage>(OnSnapshotReceived);
        }

        _netService = null;
        _lastRequestByPlayer.Clear();
        _reconciler.Reset();
    }

    private void OnSnapshotRequested(StatsSnapshotRequestMessage request, ulong senderNetId)
    {
        if (_netService?.Type != NetGameType.Host ||
            request.ProtocolVersion != StatsSyncSnapshot.CurrentProtocolVersion ||
            request.RequestSequence <= 0 ||
            _state.Identity is null ||
            !_state.Identity.PlayerNetIds.Contains(senderNetId))
        {
            return;
        }

        _lastRequestByPlayer.TryGetValue(senderNetId, out var lastRequest);
        if (request.RequestSequence <= lastRequest)
        {
            return;
        }

        _lastRequestByPlayer[senderNetId] = request.RequestSequence;
        if (TryCreateSnapshot(request.RequestSequence, out var message))
        {
            _netService.SendMessage(message!, senderNetId);
        }
    }

    private void OnSnapshotReceived(StatsSnapshotMessage message, ulong senderNetId)
    {
        if (_netService is not NetClientGameService client || message.Snapshot is null)
        {
            return;
        }

        var result = _reconciler.TryApply(
            senderNetId,
            client.HostNetId,
            message.Snapshot,
            _state);
        if (result == SnapshotReconcileResult.Applied)
        {
            _applyAssistedOwnership(message.Snapshot.AssistedOwnership);
            RunStatsLog.Debug(
                $"Applied host stats snapshot sequence {message.Snapshot.Sequence} " +
                $"at revision {message.Snapshot.Stats.Revision}.");
        }
        else if (result != SnapshotReconcileResult.StaleOrDuplicate)
        {
            RunStatsLog.Warn($"Rejected host stats snapshot: {result}.");
        }
    }

    private bool TryCreateSnapshot(long requestSequence, out StatsSnapshotMessage? message)
    {
        message = null;
        if (_hostSequence == long.MaxValue)
        {
            RunStatsLog.Warn("Stats snapshot sequence exhausted; synchronization stopped fail-closed.");
            return false;
        }

        var ownership = _captureAssistedOwnership();
        if (ownership.Count > SnapshotReconciler.MaxAssistedOwnershipEntries)
        {
            RunStatsLog.Warn("Too many assisted ownership records; snapshot skipped fail-closed.");
            return false;
        }

        _hostSequence++;
        message = new StatsSnapshotMessage
        {
            Snapshot = new StatsSyncSnapshot(
                StatsSyncSnapshot.CurrentProtocolVersion,
                _hostSequence,
                requestSequence,
                _state.CaptureSnapshot(),
                ownership)
        };
        return true;
    }
}
