namespace RunStats.Multiplayer;

public enum SnapshotReconcileResult
{
    Applied = 0,
    UnauthorizedSender = 1,
    ProtocolMismatch = 2,
    InvalidSequence = 3,
    StaleOrDuplicate = 4,
    InvalidAssistedOwnership = 5,
    SnapshotRejected = 6
}
