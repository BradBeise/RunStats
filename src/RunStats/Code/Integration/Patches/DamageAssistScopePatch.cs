using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace RunStats.Integration.Patches;

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
internal static class DamageAssistScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(out DamageAssistScope __state)
    {
        __state = RunStatsRuntime.BeginDamageAssistScope();
    }

    [HarmonyPostfix]
    private static void Postfix(DamageAssistScope __state)
    {
        RunStatsRuntime.DetachDamageAssistScope(__state);
    }
}
