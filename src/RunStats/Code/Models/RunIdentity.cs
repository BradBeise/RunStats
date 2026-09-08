using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunStats.Models;

public sealed class RunIdentity : IEquatable<RunIdentity>
{
    private readonly ReadOnlyCollection<ulong> _playerNetIds;

    private RunIdentity(
        string seed,
        RunMode mode,
        string profileId,
        DateTimeOffset startTimeUtc,
        ulong[] playerNetIds)
    {
        Seed = seed;
        Mode = mode;
        ProfileId = profileId;
        StartTimeUtc = startTimeUtc;
        _playerNetIds = Array.AsReadOnly(playerNetIds);
    }

    public string Seed { get; }

    public RunMode Mode { get; }

    public string ProfileId { get; }

    public DateTimeOffset StartTimeUtc { get; }

    public IReadOnlyList<ulong> PlayerNetIds => _playerNetIds;

    public static RunIdentity Create(
        string seed,
        RunMode mode,
        string profileId,
        DateTimeOffset startTime,
        IEnumerable<ulong> playerNetIds)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            throw new ArgumentException("A run seed is required.", nameof(seed));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("A profile ID is required.", nameof(profileId));
        }

        ArgumentNullException.ThrowIfNull(playerNetIds);

        var canonicalPlayerIds = playerNetIds.Distinct().OrderBy(id => id).ToArray();
        if (canonicalPlayerIds.Length == 0)
        {
            throw new ArgumentException("At least one player NetId is required.", nameof(playerNetIds));
        }

        return new RunIdentity(
            seed.Trim(),
            mode,
            profileId.Trim(),
            startTime.ToUniversalTime(),
            canonicalPlayerIds);
    }

    public bool Equals(RunIdentity? other)
    {
        return other is not null
            && string.Equals(Seed, other.Seed, StringComparison.Ordinal)
            && Mode == other.Mode
            && string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
            && StartTimeUtc.Equals(other.StartTimeUtc)
            && _playerNetIds.SequenceEqual(other._playerNetIds);
    }

    public override bool Equals(object? obj) => Equals(obj as RunIdentity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Seed, StringComparer.Ordinal);
        hash.Add(Mode);
        hash.Add(ProfileId, StringComparer.Ordinal);
        hash.Add(StartTimeUtc);
        foreach (var playerNetId in _playerNetIds)
        {
            hash.Add(playerNetId);
        }

        return hash.ToHashCode();
    }
}
