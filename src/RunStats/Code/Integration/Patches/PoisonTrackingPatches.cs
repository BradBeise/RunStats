using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.AfterSideTurnStart))]
internal static class PoisonSequencePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        PoisonPower __instance,
        IReadOnlyList<Creature> participants,
        ICombatState combatState,
        out PoisonSequenceScope __state)
    {
        __state = RunStatsRuntime.BeginPoisonSequence(__instance, participants, combatState);
    }

    [HarmonyPostfix]
    private static void Postfix(PoisonSequenceScope __state, ref Task __result)
    {
        __result = CompleteAsync(__result, __state);
        RunStatsRuntime.DetachPoisonSequence(__state);
    }

    private static async Task CompleteAsync(Task original, PoisonSequenceScope scope)
    {
        try
        {
            await original;
        }
        finally
        {
            RunStatsRuntime.DetachPoisonSequence(scope);
        }
    }
}

[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Damage),
    new Type[]
    {
        typeof(PlayerChoiceContext),
        typeof(IEnumerable<Creature>),
        typeof(decimal),
        typeof(ValueProp),
        typeof(Creature),
        typeof(CardModel)
    })]
internal static class PoisonDamageCommandPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        IEnumerable<Creature> targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        out PoisonDamageCommandScope __state)
    {
        __state = RunStatsRuntime.BeginPoisonDamageCommand(
            targets,
            amount,
            props,
            dealer,
            cardSource);
    }

    [HarmonyPostfix]
    private static void Postfix(
        PoisonDamageCommandScope __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        __result = RunStatsRuntime.CompletePoisonDamageCommandAsync(__result, __state);
        RunStatsRuntime.DetachPoisonDamageCommand(__state);
    }
}
