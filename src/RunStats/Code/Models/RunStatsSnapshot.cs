using System;
using System.Collections.Generic;

namespace RunStats.Models;

public sealed record RunStatsSnapshot(
    int SchemaVersion,
    RunLifecycle Lifecycle,
    RunIdentity? Identity,
    long Revision,
    IReadOnlyDictionary<ulong, PlayerStatsSnapshot> Players,
    IReadOnlyDictionary<DiagnosticKind, long> Diagnostics)
{
    public const int CurrentSchemaVersion = 1;

    public bool TryGetPlayer(ulong playerNetId, out PlayerStatsSnapshot? player) =>
        Players.TryGetValue(playerNetId, out player);

    public long GetTeamTotal(StatKind kind)
    {
        long total = 0;
        foreach (var player in Players.Values)
        {
            total = checked(total + player.GetTotal(kind));
        }

        return total;
    }

    public long GetDiagnostic(DiagnosticKind kind) =>
        Diagnostics.TryGetValue(kind, out var value) ? value : 0;
}
