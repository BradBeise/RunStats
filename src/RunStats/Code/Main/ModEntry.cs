using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using RunStats.Infrastructure;
using RunStats.Integration;

namespace RunStats.Main;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string HarmonyId = "com.bradbeise.runstats";
    private static Harmony? _harmony;

    public static void Initialize()
    {
        try
        {
            RunStatsLog.Info(
                $"Initializing v{BuildCompatibility.ModVersion} for STS2 " +
                $"v{BuildCompatibility.TargetGameVersion} ({BuildCompatibility.TargetGameCommit}).");

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();
            RunStatsRuntime.Initialize();

            RunStatsLog.Info("Initialization complete.");
        }
        catch (Exception exception)
        {
            RunStatsLog.Error("Initialization failed; RunStats will remain inactive.", exception);
        }
    }
}
