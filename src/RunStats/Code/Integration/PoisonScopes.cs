using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace RunStats.Integration;

internal sealed class PoisonSequenceScope
{
    private int _triggerIndex;

    public PoisonSequenceScope(
        PoisonSequenceScope? parent,
        PoisonPower power,
        IReadOnlyList<ulong?> sponsors,
        bool active)
    {
        Parent = parent;
        Power = power;
        Sponsors = sponsors;
        Active = active;
    }

    public PoisonSequenceScope? Parent { get; }
    public PoisonPower Power { get; }
    public Creature Owner => Power.Owner;
    public IReadOnlyList<ulong?> Sponsors { get; }
    public bool Active { get; }

    public (bool IsExtra, ulong? Sponsor) TakeTrigger()
    {
        var index = _triggerIndex++;
        return index == 0
            ? (false, null)
            : (true, index - 1 < Sponsors.Count ? Sponsors[index - 1] : null);
    }
}

internal sealed class PoisonDamageCommandScope
{
    public PoisonDamageCommandScope(
        PoisonDamageCommandScope? parent,
        Creature? poisonOwner,
        bool isPoison,
        bool isExtraTrigger,
        ulong? sponsorNetId)
    {
        Parent = parent;
        PoisonOwner = poisonOwner;
        IsPoison = isPoison;
        IsExtraTrigger = isExtraTrigger;
        SponsorNetId = sponsorNetId;
    }

    public PoisonDamageCommandScope? Parent { get; }
    public Creature? PoisonOwner { get; }
    public bool IsPoison { get; }
    public bool IsExtraTrigger { get; }
    public ulong? SponsorNetId { get; }
    public bool PoisonCycleResetDeferred { get; private set; }

    public void DeferPoisonCycleReset() => PoisonCycleResetDeferred = true;
}
