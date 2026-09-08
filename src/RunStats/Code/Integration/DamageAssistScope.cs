using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using RunStats.Models;

namespace RunStats.Integration;

internal sealed class DamageAssistScope
{
    public DamageAssistScope(DamageAssistScope? parent)
    {
        Parent = parent;
    }

    public DamageAssistScope? Parent { get; }

    public Dictionary<Creature, DamageAssistObservation> Observations { get; } =
        new(ReferenceEqualityComparer.Instance);
}

internal readonly record struct DamageAssistObservation(
    StatKind Stat,
    ulong ContributorNetId,
    decimal ActualModifiedDamage,
    decimal ContributorMultiplier);
