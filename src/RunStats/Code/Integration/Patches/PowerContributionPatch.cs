using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using RunStats.Integration;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.ModifyAmount))]
internal static class PowerContributionPatch
{
    [HarmonyPrefix]
    private static void Prefix(PowerModel power, CardModel? cardSource, out (int Amount, ulong? Contributor) __state)
    {
        __state = (power.Amount, RunStatsRuntime.BeginDoomCardApplication(power, cardSource));
    }

    [HarmonyPostfix]
    private static void Postfix(
        PowerModel power,
        Creature? applier,
        (int Amount, ulong? Contributor) __state,
        ref Task<int> __result)
    {
        __result = ObserveCompletedChange(__result, power, applier, __state.Amount);
        RunStatsRuntime.EndDoomCardApplication(__state.Contributor);
    }

    private static async Task<int> ObserveCompletedChange(
        Task<int> original,
        PowerModel power,
        Creature? applier,
        int previousAmount)
    {
        var currentAmount = await original;
        RunStatsRuntime.OnPowerAmountChanged(power, applier, previousAmount, currentAmount);
        return currentAmount;
    }
}
