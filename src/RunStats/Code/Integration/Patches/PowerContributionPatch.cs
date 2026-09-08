using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.ModifyAmount))]
internal static class PowerContributionPatch
{
    [HarmonyPrefix]
    private static void Prefix(PowerModel power, out int __state)
    {
        __state = power.Amount;
    }

    [HarmonyPostfix]
    private static void Postfix(
        PowerModel power,
        Creature? applier,
        int __state,
        ref Task<int> __result)
    {
        __result = ObserveCompletedIncrease(__result, power, applier, __state);
    }

    private static async Task<int> ObserveCompletedIncrease(
        Task<int> original,
        PowerModel power,
        Creature? applier,
        int previousAmount)
    {
        var currentAmount = await original;
        if (currentAmount > previousAmount)
        {
            RunStatsRuntime.OnPowerContribution(power, applier);
        }

        return currentAmount;
    }
}
