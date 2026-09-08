using System.Collections.Generic;

namespace RunStats.Tracking;

public enum ContributorResolution
{
    Unique = 0,
    Ambiguous = 1,
    Unsupported = 2
}

public readonly record struct ContributorResult(
    ContributorResolution Resolution,
    ulong PlayerNetId)
{
    public static ContributorResult Unique(ulong playerNetId) =>
        new(ContributorResolution.Unique, playerNetId);

    public static ContributorResult Ambiguous =>
        new(ContributorResolution.Ambiguous, 0);

    public static ContributorResult Unsupported =>
        new(ContributorResolution.Unsupported, 0);
}

public sealed class UniqueContributorLedger
{
    private readonly Dictionary<object, ContributorResult> _entries =
        new(ReferenceEqualityComparer.Instance);

    public ContributorResult Resolve(object token, ulong? initialContributor)
    {
        if (_entries.TryGetValue(token, out var existing))
        {
            return existing;
        }

        var created = initialContributor.HasValue
            ? ContributorResult.Unique(initialContributor.Value)
            : ContributorResult.Unsupported;
        _entries.Add(token, created);
        return created;
    }

    public void ObserveContribution(object token, ulong? contributor)
    {
        var current = Resolve(token, contributor);
        if (current.Resolution != ContributorResolution.Unique)
        {
            return;
        }

        if (!contributor.HasValue || contributor.Value != current.PlayerNetId)
        {
            _entries[token] = ContributorResult.Ambiguous;
        }
    }

    public void SetResolution(object token, ContributorResult contributor) =>
        _entries[token] = contributor;

    public void Clear() => _entries.Clear();
}
