using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RunStats.Models;
using RunStats.Multiplayer;

namespace RunStats.Persistence;

public sealed class RunStatsSidecarStore
{
    private readonly string _rootDirectory;

    public RunStatsSidecarStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A sidecar root directory is required.", nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string GetActivePath(RunMode mode) =>
        Path.Combine(_rootDirectory, $"active_{ModeName(mode)}.json");

    public string GetPendingPath(RunMode mode) =>
        Path.Combine(_rootDirectory, $"pending_{ModeName(mode)}.json");

    public void WriteActive(RunStatsSnapshot snapshot, long vanillaSaveTime)
    {
        WriteActive(snapshot, vanillaSaveTime, Array.Empty<AssistedOwnershipRecord>());
    }

    public void WriteActive(
        RunStatsSnapshot snapshot,
        long vanillaSaveTime,
        IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        AtomicWrite(
            GetActivePath(GetMode(snapshot)),
            SidecarSnapshotCodec.Serialize(snapshot, vanillaSaveTime, assistedOwnership));
        DeleteIfExists(GetPendingPath(GetMode(snapshot)));
    }

    public void WritePending(RunStatsSnapshot snapshot)
    {
        WritePending(snapshot, Array.Empty<AssistedOwnershipRecord>());
    }

    public void WritePending(
        RunStatsSnapshot snapshot,
        IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        AtomicWrite(
            GetPendingPath(GetMode(snapshot)),
            SidecarSnapshotCodec.Serialize(snapshot, 0, assistedOwnership));
    }

    public SidecarLoadResult TryLoadActive(
        RunIdentity identity,
        long vanillaSaveTime,
        RunStatsState destination)
    {
        return TryLoadActive(identity, vanillaSaveTime, destination, out _);
    }

    public SidecarLoadResult TryLoadActive(
        RunIdentity identity,
        long vanillaSaveTime,
        RunStatsState destination,
        out IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(destination);
        assistedOwnership = Array.Empty<AssistedOwnershipRecord>();
        var path = GetActivePath(identity.Mode);
        if (!File.Exists(path))
        {
            return SidecarLoadResult.FileNotFound;
        }

        try
        {
            if (new FileInfo(path).Length > SidecarSnapshotCodec.MaxJsonCharacters * 4L)
            {
                return SidecarLoadResult.TooLarge;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var result = SidecarSnapshotCodec.TryDeserialize(
                json,
                identity,
                vanillaSaveTime,
                out var snapshot,
                out var ownership);
            if (result != SidecarLoadResult.Loaded || snapshot is null)
            {
                return result;
            }

            if (destination.TryReplaceFromSnapshot(snapshot) != SnapshotImportResult.Applied)
            {
                return SidecarLoadResult.InvalidSnapshot;
            }

            assistedOwnership = ownership;
            return SidecarLoadResult.Loaded;
        }
        catch (IOException)
        {
            return SidecarLoadResult.IoError;
        }
        catch (UnauthorizedAccessException)
        {
            return SidecarLoadResult.IoError;
        }
    }

    public void ArchiveSnapshot(RunStatsSnapshot snapshot, long vanillaSaveTime, string reason)
    {
        ArchiveSnapshot(
            snapshot,
            vanillaSaveTime,
            reason,
            Array.Empty<AssistedOwnershipRecord>());
    }

    public void ArchiveSnapshot(
        RunStatsSnapshot snapshot,
        long vanillaSaveTime,
        string reason,
        IReadOnlyList<AssistedOwnershipRecord> assistedOwnership)
    {
        var identity = snapshot.Identity ?? throw new ArgumentException("Snapshot identity is required.");
        var archivePath = GetArchivePath(identity, reason);
        AtomicWrite(
            archivePath,
            SidecarSnapshotCodec.Serialize(
                snapshot,
                Math.Max(0, vanillaSaveTime),
                assistedOwnership));
        DeleteIfExists(GetActivePath(identity.Mode));
        DeleteIfExists(GetPendingPath(identity.Mode));
    }

    public bool ArchiveActive(RunMode mode, string reason)
    {
        var activePath = GetActivePath(mode);
        if (!File.Exists(activePath))
        {
            DeleteIfExists(GetPendingPath(mode));
            return false;
        }

        Directory.CreateDirectory(Path.Combine(_rootDirectory, "archive"));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var destination = Path.Combine(
            _rootDirectory,
            "archive",
            $"{timestamp}-{ModeName(mode)}-{SafeReason(reason)}.json");
        destination = MakeUnique(destination);
        File.Move(activePath, destination);
        DeleteIfExists(GetPendingPath(mode));
        return true;
    }

    private string GetArchivePath(RunIdentity identity, string reason)
    {
        var fingerprintInput = string.Join(
            "|",
            identity.Seed,
            identity.ProfileId,
            identity.StartTimeUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            string.Join(",", identity.PlayerNetIds));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)))[..12];
        var name = $"{identity.StartTimeUtc.ToUnixTimeSeconds()}-{ModeName(identity.Mode)}-{hash}-{SafeReason(reason)}.json";
        return MakeUnique(Path.Combine(_rootDirectory, "archive", name));
    }

    private static RunMode GetMode(RunStatsSnapshot snapshot) =>
        snapshot.Identity?.Mode ?? throw new ArgumentException("Snapshot identity is required.");

    private static string ModeName(RunMode mode) => mode switch
    {
        RunMode.Singleplayer => "singleplayer",
        RunMode.Multiplayer => "multiplayer",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string SafeReason(string reason)
    {
        var builder = new StringBuilder();
        foreach (var character in reason.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '-')
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? "ended" : builder.ToString();
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate a unique RunStats archive name.");
    }

    private static void AtomicWrite(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
