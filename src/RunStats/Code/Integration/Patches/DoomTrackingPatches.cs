using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using RunStats.Tracking;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply), new[]
{
    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal),
    typeof(Creature), typeof(CardModel), typeof(bool)
})]
internal static class DoomPowerApplyPatch
{
    [HarmonyPrefix]
    private static void Prefix(PowerModel power, CardModel? cardSource, out ulong? __state) =>
        __state = RunStatsRuntime.BeginDoomCardApplication(power, cardSource);

    [HarmonyPostfix]
    private static void Postfix(ulong? __state) =>
        RunStatsRuntime.EndDoomCardApplication(__state);
}

[HarmonyPatch(typeof(DoomPower), nameof(DoomPower.DoomKill))]
internal static class DoomKillPatch
{
    [HarmonyPrefix]
    private static void Prefix(IReadOnlyList<Creature> creatures, out DoomKillCredit[] __state) =>
        __state = creatures.Select(RunStatsRuntime.CaptureDoomKill).ToArray();

    [HarmonyPostfix]
    private static void Postfix(IReadOnlyList<Creature> creatures, DoomKillCredit[] __state, ref Task __result) =>
        __result = CompleteAsync(__result, creatures, __state);

    private static async Task CompleteAsync(
        Task original,
        IReadOnlyList<Creature> creatures,
        DoomKillCredit[] credits)
    {
        await original;
        for (var index = 0; index < credits.Length; index++)
        {
            RunStatsRuntime.CompleteDoomKill(credits[index], creatures[index]);
        }
    }
}
