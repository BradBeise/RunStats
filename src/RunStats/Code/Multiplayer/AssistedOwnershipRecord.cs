using RunStats.Tracking;

namespace RunStats.Multiplayer;

public enum AssistedPowerKind
{
    Vulnerable = 0,
    Weak = 1
}

public sealed record AssistedOwnershipRecord(
    uint OwnerCombatId,
    AssistedPowerKind PowerKind,
    ContributorResolution Resolution,
    ulong ContributorNetId);
