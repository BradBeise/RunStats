using System;
using System.Linq;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;
using RunStats.Models;
using RunStats.Persistence;

namespace RunStats.Integration;

internal static class VanillaHistoryReader
{
    public static bool TryCapture(RunState runState, out VanillaStatBaseline baseline)
    {
        baseline = new VanillaStatBaseline();
        try
        {
            foreach (var mapPoint in runState.MapPointHistory.SelectMany(act => act))
            {
                foreach (var player in mapPoint.PlayerStats)
                {
                    if (!TryAdd(baseline, player.PlayerId, StatKind.DamageTaken, player.DamageTaken) ||
                        !TryAdd(
                            baseline,
                            player.PlayerId,
                            StatKind.HealingDone,
                            VanillaStatBaseline.HistoricalHealing(
                                mapPoint.MapPointType == MapPointType.Ancient,
                                player.HpHealed)) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.MaxHpGained, player.MaxHpGained) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.GoldEarned, player.GoldGained) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.GoldSpent, player.GoldSpent) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.CardsObtained, player.CardsGained.Count) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.CardsRemoved, player.CardsRemoved.Count) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.CardsUpgraded, player.UpgradedCards.Count) ||
                        !TryAdd(
                            baseline,
                            player.PlayerId,
                            StatKind.RelicsObtained,
                            player.RelicChoices.LongCount(choice => choice.wasPicked)) ||
                        !TryAdd(
                            baseline,
                            player.PlayerId,
                            StatKind.PotionsObtained,
                            player.PotionChoices.LongCount(choice => choice.wasPicked)) ||
                        !TryAdd(baseline, player.PlayerId, StatKind.PotionsUsed, player.PotionUsed.Count))
                    {
                        baseline = new VanillaStatBaseline();
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        {
            baseline = new VanillaStatBaseline();
            return false;
        }
    }

    private static bool TryAdd(
        VanillaStatBaseline baseline,
        ulong playerNetId,
        StatKind kind,
        long amount) => baseline.TryAdd(playerNetId, kind, amount);
}
