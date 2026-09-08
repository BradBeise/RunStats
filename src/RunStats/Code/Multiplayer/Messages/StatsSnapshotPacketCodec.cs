using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using RunStats.Models;
using RunStats.Tracking;

namespace RunStats.Multiplayer.Messages;

internal static class StatsSnapshotPacketCodec
{
    public static void Write(PacketWriter writer, StatsSyncSnapshot envelope)
    {
        writer.WriteInt(envelope.ProtocolVersion);
        writer.WriteLong(envelope.Sequence);
        writer.WriteLong(envelope.RequestSequence);
        WriteStats(writer, envelope.Stats);
        writer.WriteByte(checked((byte)envelope.AssistedOwnership.Count));
        foreach (var entry in envelope.AssistedOwnership)
        {
            writer.WriteUInt(entry.OwnerCombatId);
            writer.WriteInt((int)entry.PowerKind);
            writer.WriteInt((int)entry.Resolution);
            writer.WriteULong(entry.ContributorNetId);
        }
    }

    public static StatsSyncSnapshot Read(PacketReader reader)
    {
        var protocolVersion = reader.ReadInt();
        var sequence = reader.ReadLong();
        var requestSequence = reader.ReadLong();
        var stats = ReadStats(reader);
        var ownershipCount = reader.ReadByte();
        var ownership = new List<AssistedOwnershipRecord>(ownershipCount);
        for (var index = 0; index < ownershipCount; index++)
        {
            ownership.Add(new AssistedOwnershipRecord(
                reader.ReadUInt(),
                (AssistedPowerKind)reader.ReadInt(),
                (ContributorResolution)reader.ReadInt(),
                reader.ReadULong()));
        }

        return new StatsSyncSnapshot(
            protocolVersion,
            sequence,
            requestSequence,
            stats,
            ownership.AsReadOnly());
    }

    private static void WriteStats(PacketWriter writer, RunStatsSnapshot snapshot)
    {
        writer.WriteInt(snapshot.SchemaVersion);
        writer.WriteInt((int)snapshot.Lifecycle);
        writer.WriteBool(snapshot.Identity is not null);
        if (snapshot.Identity is not null)
        {
            writer.WriteString(snapshot.Identity.Seed);
            writer.WriteInt((int)snapshot.Identity.Mode);
            writer.WriteString(snapshot.Identity.ProfileId);
            writer.WriteLong(snapshot.Identity.StartTimeUtc.ToUnixTimeSeconds());
            writer.WriteByte(checked((byte)snapshot.Identity.PlayerNetIds.Count));
            foreach (var playerNetId in snapshot.Identity.PlayerNetIds)
            {
                writer.WriteULong(playerNetId);
            }
        }

        writer.WriteLong(snapshot.Revision);
        writer.WriteByte(checked((byte)snapshot.Players.Count));
        foreach (var player in snapshot.Players.OrderBy(pair => pair.Key))
        {
            writer.WriteULong(player.Key);
            writer.WriteLong(player.Value.Revision);
            foreach (var kind in Enum.GetValues<StatKind>())
            {
                writer.WriteLong(player.Value.GetTotal(kind));
            }

            writer.WriteUShort(checked((ushort)player.Value.CardPlayCounts.Count));
            foreach (var card in player.Value.CardPlayCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(card.Key);
                writer.WriteLong(card.Value);
            }
        }

        foreach (var kind in Enum.GetValues<DiagnosticKind>())
        {
            writer.WriteLong(snapshot.GetDiagnostic(kind));
        }
    }

    private static RunStatsSnapshot ReadStats(PacketReader reader)
    {
        var schemaVersion = reader.ReadInt();
        var lifecycle = (RunLifecycle)reader.ReadInt();
        RunIdentity? identity = null;
        if (reader.ReadBool())
        {
            var seed = reader.ReadString();
            var mode = (RunMode)reader.ReadInt();
            var profileId = reader.ReadString();
            var startTime = DateTimeOffset.FromUnixTimeSeconds(reader.ReadLong());
            var identityPlayerCount = reader.ReadByte();
            var identityPlayers = new ulong[identityPlayerCount];
            for (var index = 0; index < identityPlayers.Length; index++)
            {
                identityPlayers[index] = reader.ReadULong();
            }

            identity = RunIdentity.Create(seed, mode, profileId, startTime, identityPlayers);
        }

        var revision = reader.ReadLong();
        var playerCount = reader.ReadByte();
        var players = new Dictionary<ulong, PlayerStatsSnapshot>();
        for (var playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            var playerNetId = reader.ReadULong();
            var playerRevision = reader.ReadLong();
            var totals = new Dictionary<StatKind, long>();
            foreach (var kind in Enum.GetValues<StatKind>())
            {
                totals.Add(kind, reader.ReadLong());
            }

            var cardCount = reader.ReadUShort();
            var cards = new Dictionary<string, long>(StringComparer.Ordinal);
            for (var cardIndex = 0; cardIndex < cardCount; cardIndex++)
            {
                cards.Add(reader.ReadString(), reader.ReadLong());
            }

            players.Add(playerNetId, new PlayerStatsSnapshot(
                playerNetId,
                playerRevision,
                new ReadOnlyDictionary<StatKind, long>(totals),
                new ReadOnlyDictionary<string, long>(cards)));
        }

        var diagnostics = new Dictionary<DiagnosticKind, long>();
        foreach (var kind in Enum.GetValues<DiagnosticKind>())
        {
            diagnostics.Add(kind, reader.ReadLong());
        }

        return new RunStatsSnapshot(
            schemaVersion,
            lifecycle,
            identity,
            revision,
            new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(players),
            new ReadOnlyDictionary<DiagnosticKind, long>(diagnostics));
    }
}
