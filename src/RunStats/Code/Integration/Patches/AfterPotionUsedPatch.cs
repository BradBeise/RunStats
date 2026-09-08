using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
internal static class AfterPotionUsedPatch
{
    [HarmonyPrefix]
    private static void Prefix(PotionModel potion)
    {
        RunStatsRuntime.OnPotionUsed(potion);
    }
}
