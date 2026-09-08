using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RunStats.Models;
using RunStats.Multiplayer;
using RunStats.Tracking;

namespace RunStats.Persistence;

public static class SidecarSnapshotCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string ModId = "com.bradbeise.runstats";
    public const int MaxJsonCharacters = 1_048_576;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(RunStatsSnapshot snapshot, long vanillaSaveTime) =>
        Serialize(snapshot, vanillaSaveTime, Array.Empty<AssistedOwnershipRecord>());

    public static string Serialize(
        RunStatsSnapshot snapshot,
        long vanillaSaveTime,
        IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(assistedOwnership);
        if (snapshot.Identity is null || vanillaSaveTime < 0 ||
            !SnapshotReconciler.ValidateAssistedOwnership(assistedOwnership, snapshot.Identity))
        {
            throw new ArgumentException("A sidecar requires valid run identity, checkpoint, and ownership data.");
        }

        return JsonSerializer.Serialize(
            ToDocument(snapshot, vanillaSaveTime, assistedOwnership),
            JsonOptions);
    }

    public static SidecarLoadResult TryDeserialize(
        string json,
        RunIdentity expectedIdentity,
        long expectedVanillaSaveTime,
        out RunStatsSnapshot? snapshot)
    {
        return TryDeserialize(
            json,
            expectedIdentity,
            expectedVanillaSaveTime,
            out snapshot,
            out _);
    }

    public static SidecarLoadResult TryDeserialize(
        string json,
        RunIdentity expectedIdentity,
        long expectedVanillaSaveTime,
        out RunStatsSnapshot? snapshot,
        out IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        snapshot = null;
        assistedOwnership = Array.Empty<AssistedOwnershipRecord>();
        if (json.Length > MaxJsonCharacters)
        {
            return SidecarLoadResult.TooLarge;
        }

        SidecarDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SidecarDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return SidecarLoadResult.InvalidJson;
        }

        if (document is null || document.Identity is null || document.Players is null ||
            document.Diagnostics is null || document.AssistedOwnership is null ||
            document.SchemaVersion != CurrentSchemaVersion ||
            !string.Equals(document.ModId, ModId, StringComparison.Ordinal))
        {
            return SidecarLoadResult.SchemaMismatch;
        }

        if (document.VanillaSaveTime != expectedVanillaSaveTime)
        {
            return SidecarLoadResult.SaveCheckpointMismatch;
        }

        RunIdentity identity;
        try
        {
            identity = RunIdentity.Create(
                document.Identity.Seed,
                document.Identity.Mode,
                document.Identity.ProfileId,
                DateTimeOffset.FromUnixTimeSeconds(document.Identity.StartTimeUnixSeconds),
                document.Identity.PlayerNetIds);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return SidecarLoadResult.InvalidSnapshot;
        }

        if (!identity.Equals(expectedIdentity))
        {
            return SidecarLoadResult.IdentityMismatch;
        }

        try
        {
            var players = new Dictionary<ulong, PlayerStatsSnapshot>();
            foreach (var playerDocument in document.Players)
            {
                if (playerDocument is null || playerDocument.Totals is null ||
                    playerDocument.CardPlayCounts is null)
                {
                    return SidecarLoadResult.InvalidSnapshot;
                }

                var totals = new Dictionary<StatKind, long>();
                foreach (var value in playerDocument.Totals)
                {
                    if (!Enum.TryParse(value.Kind, false, out StatKind kind) ||
                        !Enum.IsDefined(kind) || !totals.TryAdd(kind, value.Value))
                    {
                        return SidecarLoadResult.InvalidSnapshot;
                    }
                }

                var cards = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var card in playerDocument.CardPlayCounts)
                {
                    if (!cards.TryAdd(card.CardId, card.Value))
                    {
                        return SidecarLoadResult.InvalidSnapshot;
                    }
                }

                if (!players.TryAdd(
                        playerDocument.PlayerNetId,
                        new PlayerStatsSnapshot(
                            playerDocument.PlayerNetId,
                            playerDocument.Revision,
                            new ReadOnlyDictionary<StatKind, long>(totals),
                            new ReadOnlyDictionary<string, long>(cards))))
                {
                    return SidecarLoadResult.InvalidSnapshot;
                }
            }

            var diagnostics = new Dictionary<DiagnosticKind, long>();
            foreach (var value in document.Diagnostics)
            {
                if (value is null)
                {
                    return SidecarLoadResult.InvalidSnapshot;
                }

                if (!Enum.TryParse(value.Kind, false, out DiagnosticKind kind) ||
                    !Enum.IsDefined(kind) || !diagnostics.TryAdd(kind, value.Value))
                {
                    return SidecarLoadResult.InvalidSnapshot;
                }
            }

            snapshot = new RunStatsSnapshot(
                document.SnapshotSchemaVersion,
                document.Lifecycle,
                identity,
                document.Revision,
                new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(players),
                new ReadOnlyDictionary<DiagnosticKind, long>(diagnostics));
            var ownership = document.AssistedOwnership
                .Select(entry => new AssistedOwnershipRecord(
                    entry.OwnerCombatId,
                    entry.PowerKind,
                    entry.Resolution,
                    entry.ContributorNetId))
                .ToArray();
            if (!SnapshotReconciler.ValidateAssistedOwnership(ownership, identity))
            {
                snapshot = null;
                return SidecarLoadResult.InvalidSnapshot;
            }

            var result = ValidateSnapshot(snapshot, expectedIdentity);
            if (result == SidecarLoadResult.Loaded)
            {
                assistedOwnership = Array.AsReadOnly(ownership);
            }
            return result;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            snapshot = null;
            return SidecarLoadResult.InvalidSnapshot;
        }
    }

    private static SidecarLoadResult ValidateSnapshot(
        RunStatsSnapshot candidate,
        RunIdentity expectedIdentity)
    {
        var validator = new RunStatsState();
        validator.StartRun(expectedIdentity);
        if (validator.TryReplaceFromSnapshot(candidate) != SnapshotImportResult.Applied ||
            candidate.Lifecycle != RunLifecycle.Active)
        {
            return SidecarLoadResult.InvalidSnapshot;
        }

        return SidecarLoadResult.Loaded;
    }

    private static SidecarDocument ToDocument(
        RunStatsSnapshot snapshot,
        long vanillaSaveTime,
        IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        var identity = snapshot.Identity!;
        return new SidecarDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            ModId = ModId,
            VanillaSaveTime = vanillaSaveTime,
            SnapshotSchemaVersion = snapshot.SchemaVersion,
            Lifecycle = snapshot.Lifecycle,
            Revision = snapshot.Revision,
            Identity = new IdentityDocument
            {
                Seed = identity.Seed,
                Mode = identity.Mode,
                ProfileId = identity.ProfileId,
                StartTimeUnixSeconds = identity.StartTimeUtc.ToUnixTimeSeconds(),
                PlayerNetIds = identity.PlayerNetIds.ToArray()
            },
            Players = snapshot.Players.Values
                .OrderBy(player => player.PlayerNetId)
                .Select(player => new PlayerDocument
                {
                    PlayerNetId = player.PlayerNetId,
                    Revision = player.Revision,
                    Totals = player.Totals
                        .OrderBy(pair => pair.Key)
                        .Select(pair => new NamedValueDocument
                        {
                            Kind = pair.Key.ToString(),
                            Value = pair.Value
                        })
                        .ToArray(),
                    CardPlayCounts = player.CardPlayCounts
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new CardValueDocument
                        {
                            CardId = pair.Key,
                            Value = pair.Value
                        })
                        .ToArray()
                })
                .ToArray(),
            Diagnostics = snapshot.Diagnostics
                .OrderBy(pair => pair.Key)
                .Select(pair => new NamedValueDocument
                {
                    Kind = pair.Key.ToString(),
                    Value = pair.Value
                })
                .ToArray(),
            AssistedOwnership = assistedOwnership
                .OrderBy(entry => entry.OwnerCombatId)
                .ThenBy(entry => entry.PowerKind)
                .Select(entry => new OwnershipDocument
                {
                    OwnerCombatId = entry.OwnerCombatId,
                    PowerKind = entry.PowerKind,
                    Resolution = entry.Resolution,
                    ContributorNetId = entry.ContributorNetId
                })
                .ToArray()
        };
    }

    private sealed class SidecarDocument
    {
        public int SchemaVersion { get; set; }
        public string ModId { get; set; } = string.Empty;
        public long VanillaSaveTime { get; set; }
        public int SnapshotSchemaVersion { get; set; }
        public RunLifecycle Lifecycle { get; set; }
        public long Revision { get; set; }
        public IdentityDocument Identity { get; set; } = new();
        public PlayerDocument[] Players { get; set; } = Array.Empty<PlayerDocument>();
        public NamedValueDocument[] Diagnostics { get; set; } = Array.Empty<NamedValueDocument>();
        public OwnershipDocument[] AssistedOwnership { get; set; } = Array.Empty<OwnershipDocument>();
    }

    private sealed class IdentityDocument
    {
        public string Seed { get; set; } = string.Empty;
        public RunMode Mode { get; set; }
        public string ProfileId { get; set; } = string.Empty;
        public long StartTimeUnixSeconds { get; set; }
        public ulong[] PlayerNetIds { get; set; } = Array.Empty<ulong>();
    }

    private sealed class PlayerDocument
    {
        public ulong PlayerNetId { get; set; }
        public long Revision { get; set; }
        public NamedValueDocument[] Totals { get; set; } = Array.Empty<NamedValueDocument>();
        public CardValueDocument[] CardPlayCounts { get; set; } = Array.Empty<CardValueDocument>();
    }

    private sealed class NamedValueDocument
    {
        public string Kind { get; set; } = string.Empty;
        public long Value { get; set; }
    }

    private sealed class CardValueDocument
    {
        public string CardId { get; set; } = string.Empty;
        public long Value { get; set; }
    }

    private sealed class OwnershipDocument
    {
        public uint OwnerCombatId { get; set; }
        public AssistedPowerKind PowerKind { get; set; }
        public ContributorResolution Resolution { get; set; }
        public ulong ContributorNetId { get; set; }
    }
}
