using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
internal static class AfterDamageGivenPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        ICombatState combatState,
        Creature? dealer,
        DamageResult results,
        Creature target)
    {
        RunStatsRuntime.OnDamageGiven(combatState, dealer, results, target);
    }
}
