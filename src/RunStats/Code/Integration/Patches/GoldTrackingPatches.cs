using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold))]
internal static class GainGoldPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player, out int __state)
    {
        __state = player.Gold;
    }

    [HarmonyPostfix]
    private static void Postfix(
        Player player,
        bool wasStolenBack,
        int __state,
        ref Task __result)
    {
        __result = Complete(__result, player, wasStolenBack, __state);
    }

    private static async Task Complete(
        Task original,
        Player player,
        bool wasStolenBack,
        int previousGold)
    {
        await original;
        RunStatsRuntime.OnGoldGained(player, previousGold, player.Gold, wasStolenBack);
    }
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.LoseGold))]
internal static class LoseGoldPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player, out int __state)
    {
        __state = player.Gold;
    }

    [HarmonyPostfix]
    private static void Postfix(
        Player player,
        GoldLossType goldLossType,
        int __state,
        ref Task __result)
    {
        __result = Complete(__result, player, goldLossType, __state);
    }

    private static async Task Complete(
        Task original,
        Player player,
        GoldLossType goldLossType,
        int previousGold)
    {
        await original;
        RunStatsRuntime.OnGoldLost(player, previousGold, player.Gold, goldLossType);
    }
}
