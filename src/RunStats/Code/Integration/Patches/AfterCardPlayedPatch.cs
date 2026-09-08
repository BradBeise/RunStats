using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
internal static class AfterCardPlayedPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPlay cardPlay)
    {
        RunStatsRuntime.OnCardPlayed(cardPlay);
    }
}
