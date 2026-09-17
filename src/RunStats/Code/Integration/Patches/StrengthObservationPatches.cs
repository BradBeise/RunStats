using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(TemporaryStrengthPower), nameof(TemporaryStrengthPower.BeforeApplied))]
internal static class TemporaryStrengthBeforeAppliedPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        TemporaryStrengthPower __instance,
        out TemporaryStrengthMutationScope __state)
    {
        __state = RunStatsRuntime.BeginTemporaryStrengthMutation(__instance, false);
    }

    [HarmonyPostfix]
    private static void Postfix(
        TemporaryStrengthMutationScope __state,
        ref Task __result)
    {
        __result = RunStatsRuntime.CompleteTemporaryStrengthMutationAsync(__result, __state);
    }
}

[HarmonyPatch(
    typeof(TemporaryStrengthPower),
    nameof(TemporaryStrengthPower.AfterPowerAmountChanged))]
internal static class TemporaryStrengthAmountChangedPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        TemporaryStrengthPower __instance,
        out TemporaryStrengthMutationScope __state)
    {
        __state = RunStatsRuntime.BeginTemporaryStrengthMutation(__instance, false);
    }

    [HarmonyPostfix]
    private static void Postfix(
        TemporaryStrengthMutationScope __state,
        ref Task __result)
    {
        __result = RunStatsRuntime.CompleteTemporaryStrengthMutationAsync(__result, __state);
    }
}

[HarmonyPatch(typeof(TemporaryStrengthPower), nameof(TemporaryStrengthPower.AfterSideTurnEnd))]
internal static class TemporaryStrengthTurnEndPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        TemporaryStrengthPower __instance,
        out TemporaryStrengthMutationScope __state)
    {
        __state = RunStatsRuntime.BeginTemporaryStrengthMutation(__instance, true);
    }

    [HarmonyPostfix]
    private static void Postfix(
        TemporaryStrengthMutationScope __state,
        ref Task __result)
    {
        __result = RunStatsRuntime.CompleteTemporaryStrengthMutationAsync(__result, __state);
    }
}

[HarmonyPatch(typeof(GameAction), nameof(GameAction.Cancel))]
internal static class StrengthCanceledActionPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameAction __instance)
    {
        RunStatsRuntime.OnPlayerActionCanceled(__instance);
    }
}

[HarmonyPatch(typeof(Brimstone), nameof(Brimstone.AfterSideTurnStart))]
internal static class BrimstoneStrengthSourcePatch
{
    [HarmonyPrefix]
    private static void Prefix(Brimstone __instance, out OwnedStrengthSourceScope __state)
    {
        __state = RunStatsRuntime.BeginOwnedStrengthSource(__instance, __instance.Owner.NetId);
    }

    [HarmonyPostfix]
    private static void Postfix(OwnedStrengthSourceScope __state, ref Task __result)
    {
        __result = RunStatsRuntime.CompleteOwnedStrengthSourceAsync(__result, __state);
    }
}

[HarmonyPatch(typeof(PhilosophersStone), nameof(PhilosophersStone.AfterCreatureAddedToCombat))]
internal static class PhilosophersStoneCreatureStrengthSourcePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        PhilosophersStone __instance,
        out OwnedStrengthSourceScope __state)
    {
        __state = RunStatsRuntime.BeginOwnedStrengthSource(__instance, __instance.Owner.NetId);
    }

    [HarmonyPostfix]
    private static void Postfix(OwnedStrengthSourceScope __state, ref Task __result)
    {
        __result = RunStatsRuntime.CompleteOwnedStrengthSourceAsync(__result, __state);
    }
}

[HarmonyPatch(typeof(PhilosophersStone), nameof(PhilosophersStone.AfterRoomEntered))]
internal static class PhilosophersStoneRoomStrengthSourcePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        PhilosophersStone __instance,
        out OwnedStrengthSourceScope __state)
    {
        __state = RunStatsRuntime.BeginOwnedStrengthSource(__instance, __instance.Owner.NetId);
    }

    [HarmonyPostfix]
    private static void Postfix(OwnedStrengthSourceScope __state, ref Task __result)
    {
        __result = RunStatsRuntime.CompleteOwnedStrengthSourceAsync(__result, __state);
    }
}
