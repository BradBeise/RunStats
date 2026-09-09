using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ApplyInternal))]
internal static class PowerApplyInternalPatch
{
    [HarmonyPostfix]
    private static void Postfix(PowerModel __instance, Creature owner)
    {
        RunStatsRuntime.OnPowerAmountChanged(
            __instance,
            __instance.Applier,
            0,
            __instance.Amount);
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.RemoveInternal))]
internal static class PowerRemoveInternalPatch
{
    [HarmonyPrefix]
    private static void Prefix(PowerModel __instance)
    {
        RunStatsRuntime.OnPowerRemoved(__instance);
    }
}
