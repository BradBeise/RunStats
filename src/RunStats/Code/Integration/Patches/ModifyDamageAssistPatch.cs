using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class ModifyDamageAssistPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        IRunState runState,
        ICombatState? combatState,
        Creature? target,
        Creature? dealer,
        ValueProp props,
        CardModel? cardSource,
        ModifyDamageHookType modifyDamageHookType,
        IEnumerable<AbstractModel> modifiers,
        decimal __result)
    {
        RunStatsRuntime.OnDamageModified(
            runState,
            combatState,
            target,
            dealer,
            props,
            cardSource,
            modifyDamageHookType,
            modifiers,
            __result);
    }
}
