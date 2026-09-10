using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
internal static class AfterDamageReceivedPatch
{
    [HarmonyPrefix]
    private static void Prefix(Creature target, DamageResult result, Creature? dealer)
    {
        RunStatsRuntime.OnDamageReceived(target, result, dealer);
    }
}
