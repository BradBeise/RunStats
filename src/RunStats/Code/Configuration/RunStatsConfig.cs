namespace RunStats.Configuration;

/// <summary>
/// Runtime switches owned by RunStats. Persistence is added with the save system.
/// </summary>
public static class RunStatsConfig
{
    public static bool EnableDebugLogging { get; set; } = false;
}
