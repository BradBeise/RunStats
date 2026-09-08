namespace RunStats.Models;

public enum SnapshotImportResult
{
    Applied = 0,
    NoActiveRun = 1,
    SchemaMismatch = 2,
    RunIdentityMismatch = 3,
    InvalidLifecycle = 4,
    InvalidRevision = 5,
    InvalidPlayers = 6,
    InvalidDiagnostics = 7
}
