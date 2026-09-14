using System.Collections.Generic;
using RunStats.Models;

namespace RunStats.Multiplayer;

public sealed record StatsSyncSnapshot(
    int ProtocolVersion,
    long Sequence,
    long RequestSequence,
    RunStatsSnapshot Stats,
    IReadOnlyList<AssistedOwnershipRecord> AssistedOwnership)
{
    public const int CurrentProtocolVersion = 3;
}
