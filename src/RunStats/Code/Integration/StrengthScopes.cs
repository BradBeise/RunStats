using MegaCrit.Sts2.Core.Models.Powers;

namespace RunStats.Integration;

internal sealed class TemporaryStrengthMutationScope
{
    public TemporaryStrengthMutationScope(
        TemporaryStrengthMutationScope? parent,
        TemporaryStrengthPower power,
        bool isRestoration)
    {
        Parent = parent;
        Power = power;
        IsRestoration = isRestoration;
    }

    public TemporaryStrengthMutationScope? Parent { get; }

    public TemporaryStrengthPower Power { get; }

    public bool IsRestoration { get; }
}

internal sealed class OwnedStrengthSourceScope
{
    public OwnedStrengthSourceScope(
        OwnedStrengthSourceScope? parent,
        object source,
        ulong contributorNetId)
    {
        Parent = parent;
        Source = source;
        ContributorNetId = contributorNetId;
    }

    public OwnedStrengthSourceScope? Parent { get; }

    public object Source { get; }

    public ulong ContributorNetId { get; }
}
