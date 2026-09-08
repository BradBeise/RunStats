namespace RunStats.Persistence;

public enum SidecarLoadResult
{
    Loaded = 0,
    FileNotFound = 1,
    TooLarge = 2,
    InvalidJson = 3,
    SchemaMismatch = 4,
    IdentityMismatch = 5,
    SaveCheckpointMismatch = 6,
    InvalidSnapshot = 7,
    IoError = 8
}
