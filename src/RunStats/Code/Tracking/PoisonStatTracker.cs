using System;
using RunStats.Models;

namespace RunStats.Tracking;

public sealed class PoisonStatTracker
{
    private readonly RunStatsState _state;
    private readonly CoreCombatTracker _combatTracker;

    public PoisonStatTracker(
        RunStatsState state,
        CoreCombatTracker combatTracker,
        PoisonContributionLedger contributionLedger,
        AccelerantSponsorLedger sponsorLedger)
    {
        _state = state;
        _combatTracker = combatTracker;
        Contributions = contributionLedger;
        Sponsors = sponsorLedger;
    }

    public PoisonContributionLedger Contributions { get; }

    public AccelerantSponsorLedger Sponsors { get; }

    public void ObservePoisonAmount(
        object enemyToken,
        int previousAmount,
        int currentAmount,
        ulong? contributorNetId)
    {
        var observation = Contributions.ObserveAmount(
            enemyToken,
            previousAmount,
            currentAmount,
            contributorNetId);
        if (!observation.Accepted)
        {
            _state.TryRecordDiagnostic(DiagnosticKind.UnsupportedPoisonDamage);
            return;
        }

        if (observation.CreditedPoisonApplied > 0 && contributorNetId is > 0)
        {
            _state.TryApply(StatMutation.Add(
                contributorNetId.Value,
                StatKind.PoisonApplied,
                observation.CreditedPoisonApplied));
        }
        else if (observation.UnattributedPoisonApplied > 0)
        {
            _state.TryRecordDiagnostic(DiagnosticKind.UnattributedPoisonApplication);
        }
    }

    public void ObserveAccelerantApplication(ulong? contributorNetId, int positiveDelta)
    {
        if (contributorNetId is not > 0 || !Sponsors.ObserveApplication(contributorNetId.Value, positiveDelta))
        {
            _state.TryRecordDiagnostic(DiagnosticKind.UnsponsoredAccelerantTrigger);
        }
    }

    public bool RecordPoisonTrigger(
        object enemyToken,
        long actualDamage,
        bool isExtraTrigger,
        ulong? sponsorNetId,
        out PoisonDamageAllocation allocation)
    {
        if (!Contributions.TryAllocateDamage(enemyToken, actualDamage, out allocation))
        {
            _state.TryRecordDiagnostic(DiagnosticKind.UnsupportedPoisonDamage);
            return false;
        }

        foreach (var entry in allocation.PlayerDamage)
        {
            if (entry.Value > 0)
            {
                _state.TryApply(StatMutation.Add(entry.Key, StatKind.DamageDealt, entry.Value));
            }
        }

        if (isExtraTrigger)
        {
            if (sponsorNetId is not > 0)
            {
                _state.TryRecordDiagnostic(DiagnosticKind.UnsponsoredAccelerantTrigger);
            }
            else if (AccelerantAssistCalculator.TryCalculate(
                         allocation,
                         sponsorNetId.Value,
                         out var assistedDamage) &&
                     assistedDamage > 0)
            {
                _state.TryApply(StatMutation.Add(
                    sponsorNetId.Value,
                    StatKind.AssistedDamage,
                    assistedDamage));
            }
        }

        return true;
    }

    public void RecordPoisonKill(
        object poisonedEnemyToken,
        object killedTargetToken,
        CombatRoomKind roomKind,
        ulong killEntropy)
    {
        var killRecipient = Contributions.SelectKillRecipient(poisonedEnemyToken, killEntropy);
        if (killRecipient.HasValue)
        {
            _combatTracker.RecordAttributedKill(killRecipient.Value, killedTargetToken, roomKind);
        }
    }

    public void ResetCombat()
    {
        Contributions.Clear();
        Sponsors.Clear();
    }
}
