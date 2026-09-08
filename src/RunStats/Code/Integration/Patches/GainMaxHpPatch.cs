using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp))]
internal static class GainMaxHpPatch
{
    [HarmonyPrefix]
    private static void Prefix(Creature creature)
    {
        RunStatsRuntime.BeginMaxHpGain(creature);
    }

    [HarmonyPostfix]
    private static void Postfix(Creature creature, ref Task __result)
    {
        __result = CompleteAndCloseScope(__result, creature);
    }

    private static async Task CompleteAndCloseScope(Task original, Creature creature)
    {
        try
        {
            await original;
        }
        finally
        {
            RunStatsRuntime.EndMaxHpGain(creature);
        }
    }
}
