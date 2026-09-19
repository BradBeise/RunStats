using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RunStats.Models;

namespace RunStats.UI;

public readonly record struct StatsPanelSize(float Width, float Height);

public static class StatsLayout
{
    public static StatsPanelSize CalculatePanelSize(float viewportWidth, float viewportHeight) =>
        new(
            Math.Clamp(viewportWidth * 0.88f, 940f, 1560f),
            Math.Clamp(viewportHeight * 0.86f, 620f, 980f));
}

public sealed record StatsRowViewModel(
    string Label,
    IReadOnlyList<string> PlayerValues,
    string? TeamValue,
    StatKind? Kind = null);

public sealed record StatsSectionViewModel(
    string Title,
    IReadOnlyList<StatsRowViewModel> Rows);

public sealed record StatsViewModel(
    string Subtitle,
    IReadOnlyList<string> ColumnHeaders,
    IReadOnlyList<StatsRowViewModel> Rows,
    IReadOnlyList<StatsSectionViewModel> Sections,
    IReadOnlyList<ulong> PlayerNetIds,
    IReadOnlyList<string?> PlayerMostPlayedCardIds,
    string? TeamMostPlayedCardId)
{
    private const string Unavailable = "—";

    private static readonly ReadOnlyCollection<(StatKind Kind, string Label)> StatRows =
        Array.AsReadOnly(new[]
        {
            (StatKind.DamageDealt, "Damage Dealt"),
            (StatKind.PoisonApplied, "Poison Applied"),
            (StatKind.DoomApplied, "Doom Applied"),
            (StatKind.DamageTaken, "Damage Taken"),
            (StatKind.AssistedDamage, "Assisted Damage"),
            (StatKind.AssistedDamagePrevented, "Damage Prevented"),
            (StatKind.HealingDone, "Healing Done"),
            (StatKind.MaxHpGained, "Max HP Gained"),
            (StatKind.BlockGained, "Block Gained"),
            (StatKind.BlockLost, "Block Lost"),
            (StatKind.EnemiesKilled, "Enemies Killed"),
            (StatKind.EliteEnemiesKilled, "Elite Enemies Killed"),
            (StatKind.BossesKilled, "Bosses Killed"),
            (StatKind.CardsPlayed, "Cards Played"),
            (StatKind.CardsObtained, "Cards Obtained"),
            (StatKind.CardsUpgraded, "Cards Upgraded"),
            (StatKind.CardsRemoved, "Cards Removed"),
            (StatKind.GoldEarned, "Gold Earned"),
            (StatKind.GoldSpent, "Gold Spent"),
            (StatKind.RelicsObtained, "Relics Obtained"),
            (StatKind.PotionsObtained, "Potions Obtained"),
            (StatKind.PotionsUsed, "Potions Used")
        });

    private static readonly ReadOnlyCollection<(string Title, StatKind[] Kinds)> SectionDefinitions =
        Array.AsReadOnly(new[]
        {
            ("DAMAGE", new[] { StatKind.DamageDealt, StatKind.PoisonApplied, StatKind.DoomApplied, StatKind.DamageTaken, StatKind.AssistedDamage, StatKind.AssistedDamagePrevented }),
            ("HEALING / BLOCK", new[] { StatKind.HealingDone, StatKind.MaxHpGained, StatKind.BlockGained, StatKind.BlockLost }),
            ("KILLS", new[] { StatKind.EnemiesKilled, StatKind.EliteEnemiesKilled, StatKind.BossesKilled }),
            ("CARDS", new[] { StatKind.CardsPlayed, StatKind.CardsObtained, StatKind.CardsUpgraded, StatKind.CardsRemoved }),
            ("ECONOMY", new[] { StatKind.GoldEarned, StatKind.GoldSpent }),
            ("RELICS", new[] { StatKind.RelicsObtained }),
            ("POTIONS", new[] { StatKind.PotionsObtained, StatKind.PotionsUsed })
        });

    public static bool TryCreate(RunStatsSnapshot snapshot, out StatsViewModel? viewModel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        viewModel = null;
        if (snapshot.Lifecycle is not (RunLifecycle.Active or RunLifecycle.Ended) ||
            snapshot.Identity is null)
        {
            return false;
        }

        var players = snapshot.Identity.PlayerNetIds
            .Select(playerNetId => snapshot.Players.TryGetValue(playerNetId, out var player)
                ? player
                : null)
            .ToArray();
        if (players.Length == 0 || players.Any(player => player is null))
        {
            return false;
        }

        var completePlayers = players.Select(player => player!).ToArray();
        var includeTeam = completePlayers.Length > 1;
        var headers = Enumerable.Range(1, completePlayers.Length)
            .Select(index => $"PLAYER {index}")
            .ToList();
        if (includeTeam)
        {
            headers.Add("TEAM");
        }

        var rows = new List<StatsRowViewModel>(StatRows.Count + 1);
        foreach (var definition in StatRows)
        {
            var values = completePlayers
                .Select(player => FormatNumber(player.GetTotal(definition.Kind)))
                .ToArray();
            rows.Add(new StatsRowViewModel(
                definition.Label,
                Array.AsReadOnly(values),
                includeTeam ? FormatTeamTotal(completePlayers, definition.Kind) : null,
                definition.Kind));

            if (definition.Kind == StatKind.CardsPlayed)
            {
                var cardValues = completePlayers
                    .Select(player => player.GetMostPlayedCardId() ?? Unavailable)
                    .ToArray();
                rows.Add(new StatsRowViewModel(
                    "Most Played Card",
                    Array.AsReadOnly(cardValues),
                    includeTeam ? GetTeamMostPlayedCard(completePlayers) : null));
            }
        }

        var playerCardIds = completePlayers
            .Select(player => player.GetMostPlayedCardId())
            .ToArray();
        var teamCardId = includeTeam ? GetTeamMostPlayedCardIdOrNull(completePlayers) : null;
        var sections = SectionDefinitions
            .Select(section => new StatsSectionViewModel(
                section.Title,
                Array.AsReadOnly(section.Kinds
                    .Select(kind => rows.Single(row => row.Kind == kind))
                    .ToArray())))
            .ToArray();

        viewModel = new StatsViewModel(
            $"SEED {snapshot.Identity.Seed}  •  REVISION {snapshot.Revision.ToString(CultureInfo.InvariantCulture)}",
            Array.AsReadOnly(headers.ToArray()),
            Array.AsReadOnly(rows.ToArray()),
            Array.AsReadOnly(sections),
            snapshot.Identity.PlayerNetIds,
            Array.AsReadOnly(playerCardIds),
            teamCardId);
        return true;
    }

    private static string FormatTeamTotal(
        IReadOnlyList<PlayerStatsSnapshot> players,
        StatKind kind)
    {
        try
        {
            long total = 0;
            foreach (var player in players)
            {
                total = checked(total + player.GetTotal(kind));
            }

            return FormatNumber(total);
        }
        catch (OverflowException)
        {
            return Unavailable;
        }
    }

    private static string GetTeamMostPlayedCard(IReadOnlyList<PlayerStatsSnapshot> players) =>
        GetTeamMostPlayedCardIdOrNull(players) ?? Unavailable;

    private static string? GetTeamMostPlayedCardIdOrNull(IReadOnlyList<PlayerStatsSnapshot> players)
    {
        try
        {
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var player in players)
            {
                foreach (var pair in player.CardPlayCounts)
                {
                    totals.TryGetValue(pair.Key, out var current);
                    totals[pair.Key] = checked(current + pair.Value);
                }
            }

            return totals
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key)
                .FirstOrDefault();
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string FormatNumber(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
