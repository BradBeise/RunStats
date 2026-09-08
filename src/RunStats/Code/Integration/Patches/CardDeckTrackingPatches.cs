using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace RunStats.Integration.Patches;

[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Add),
    new Type[]
    {
        typeof(IEnumerable<CardModel>),
        typeof(CardPile),
        typeof(CardPilePosition),
        typeof(AbstractModel),
        typeof(bool)
    })]
internal static class CardsObtainedPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CardPile newPile,
        ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        __result = Complete(__result, newPile);
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> Complete(
        Task<IReadOnlyList<CardPileAddResult>> original,
        CardPile newPile)
    {
        var results = await original;
        if (newPile.Type == PileType.Deck)
        {
            foreach (var result in results.Where(result => result.success))
            {
                RunStatsRuntime.OnCardObtained(result.cardAdded);
            }
        }

        return results;
    }
}

[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.RemoveFromDeck),
    new Type[] { typeof(IReadOnlyList<CardModel>), typeof(bool) })]
internal static class CardsRemovedPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        IReadOnlyList<CardModel> cards,
        ref Task __result)
    {
        __result = Complete(__result, cards);
    }

    private static async Task Complete(Task original, IReadOnlyList<CardModel> cards)
    {
        try
        {
            await original;
        }
        finally
        {
            var removedCards = new HashSet<CardModel>(ReferenceEqualityComparer.Instance);
            foreach (var card in cards.Where(card => card.HasBeenRemovedFromState))
            {
                if (removedCards.Add(card))
                {
                    RunStatsRuntime.OnCardRemoved(card);
                }
            }
        }
    }
}

[HarmonyPatch(
    typeof(CardCmd),
    nameof(CardCmd.Upgrade),
    new Type[] { typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle) })]
internal static class CardsUpgradedPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        ref IEnumerable<CardModel> cards,
        out Dictionary<CardModel, int> __state)
    {
        var materialized = cards.ToList();
        cards = materialized;
        __state = new Dictionary<CardModel, int>(ReferenceEqualityComparer.Instance);
        foreach (var card in materialized)
        {
            if (card.Pile?.Type == PileType.Deck)
            {
                __state.TryAdd(card, card.CurrentUpgradeLevel);
            }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Dictionary<CardModel, int> __state)
    {
        foreach (var pair in __state)
        {
            var levelsGained = pair.Key.CurrentUpgradeLevel - pair.Value;
            if (levelsGained > 0)
            {
                RunStatsRuntime.OnCardUpgraded(pair.Key, levelsGained);
            }
        }
    }
}
