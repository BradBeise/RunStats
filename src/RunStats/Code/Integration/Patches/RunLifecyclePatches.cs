using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class RunEndedPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool isVictory)
    {
        RunStatsRuntime.OnRunEnded(isVictory);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        RunStatsRuntime.OnRunCleanup();
    }
}
