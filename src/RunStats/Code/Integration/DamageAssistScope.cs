using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using RunStats.Models;
using RunStats.Tracking;

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

    public Dictionary<Creature, StrengthOutgoingDamageObservation> StrengthObservations { get; } =
        new(ReferenceEqualityComparer.Instance);

    public Dictionary<Creature, StrengthIncomingDamageObservation> StrengthIncomingObservations { get; } =
        new(ReferenceEqualityComparer.Instance);

    public HashSet<Creature> VulnerableOwners { get; } =
        new(ReferenceEqualityComparer.Instance);

    public HashSet<Creature> WeakOwners { get; } =
        new(ReferenceEqualityComparer.Instance);

    public HashSet<Creature> DeferredVulnerableResets { get; } =
        new(ReferenceEqualityComparer.Instance);

    public HashSet<Creature> DeferredWeakResets { get; } =
        new(ReferenceEqualityComparer.Instance);

    public List<WeakPreventionObservation> WeakPreventionRows { get; } = new();

    public void Track(Creature owner, AssistedEffectKind kind) =>
        Owners(kind).Add(owner);

    public bool TryDeferReset(Creature owner, AssistedEffectKind kind)
    {
        if (!Owners(kind).Contains(owner))
        {
            return false;
        }

        DeferredResets(kind).Add(owner);
        return true;
    }

    private HashSet<Creature> Owners(AssistedEffectKind kind) =>
        kind == AssistedEffectKind.Vulnerable ? VulnerableOwners : WeakOwners;

    private HashSet<Creature> DeferredResets(AssistedEffectKind kind) =>
        kind == AssistedEffectKind.Vulnerable
            ? DeferredVulnerableResets
            : DeferredWeakResets;
}

internal readonly record struct DamageAssistObservation(
    StatKind Stat,
    Creature EffectOwner,
    ulong BeneficiaryNetId,
    decimal ActualModifiedDamage,
    decimal ContributorMultiplier);

internal readonly record struct WeakPreventionObservation(
    Creature EffectOwner,
    ulong ProtectedPlayerNetId,
    long Prevention);

internal sealed record StrengthOutgoingDamageObservation(
    Creature Attacker,
    decimal ActualModifiedDamage,
    decimal VulnerableMultiplier,
    decimal StrengthDownstreamMultiplier,
    IReadOnlyList<StrengthImpactEvent> Events);

internal sealed record StrengthIncomingDamageObservation(
    Creature Attacker,
    decimal ActualModifiedDamage,
    decimal? ExactUnmodifiedDamage,
    long CurrentStrength,
    decimal WeakMultiplier,
    decimal StrengthDownstreamMultiplier,
    ulong ProtectedPlayerNetId,
    IReadOnlyList<StrengthImpactEvent> Events);
