using RunStats.Models;

namespace RunStats.Tracking;

public sealed class CoreCombatTracker
{
    private readonly RunStatsState _state;
    private readonly KillCreditLedger _killCredits = new();

    public CoreCombatTracker(RunStatsState state)
    {
        _state = state;
    }

    public void ResetForRun() => _killCredits.Clear();

    public void RecordDamageGiven(
        ulong? sourcePlayerNetId,
        bool targetIsEnemy,
        int unblockedDamage,
        bool targetKilled,
        object targetToken,
        CombatRoomKind roomKind)
    {
        var actualDamage = ActualDelta.ResolvedDamage(unblockedDamage);
        if (!targetIsEnemy || actualDamage == 0)
        {
            return;
        }

        if (!sourcePlayerNetId.HasValue)
        {
            _state.TryRecordDiagnostic(DiagnosticKind.UnsupportedDamageSource);
            return;
        }

        _state.TryApply(StatMutation.Add(sourcePlayerNetId.Value, StatKind.DamageDealt, actualDamage));
        if (!targetKilled)
        {
            return;
        }

        RecordAttributedKill(sourcePlayerNetId.Value, targetToken, roomKind);
    }

    public void RecordAttributedKill(
        ulong sourcePlayerNetId,
        object targetToken,
        CombatRoomKind roomKind)
    {
        if (sourcePlayerNetId == 0 || !_killCredits.TryCredit(targetToken))
        {
            return;
        }

        _state.TryApply(StatMutation.Add(sourcePlayerNetId, StatKind.EnemiesKilled));
        if (roomKind == CombatRoomKind.Elite)
        {
            _state.TryApply(StatMutation.Add(sourcePlayerNetId, StatKind.EliteEnemiesKilled));
        }
        else if (roomKind == CombatRoomKind.Boss)
        {
            _state.TryApply(StatMutation.Add(sourcePlayerNetId, StatKind.BossesKilled));
        }
    }

    public void RecordDamageReceived(
        ulong playerNetId,
        int unblockedDamage,
        int blockedDamage,
        bool sourceIsEnemy)
    {
        var actualDamage = ActualDelta.ResolvedDamage(unblockedDamage);
        if (actualDamage > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, StatKind.DamageTaken, actualDamage));
        }

        if (!sourceIsEnemy)
        {
            return;
        }

        var actualBlockedDamage = ActualDelta.ResolvedDamage(blockedDamage);
        if (actualBlockedDamage > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, StatKind.BlockLost, actualBlockedDamage));
        }
    }

    public void RecordBlockChanged(ulong playerNetId, int previous, int current)
    {
        var delta = SignedDelta.Between(previous, current);
        if (delta.Gained > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, StatKind.BlockGained, delta.Gained));
        }
    }

    public void RecordCurrentHpChanged(
        ulong playerNetId,
        int previous,
        int current,
        bool isInsideMaxHpGain)
    {
        // STS2 initializes a run's creature with a 0 -> starting HP notification.
        // This is construction, not healing. Zero-HP recovery is also excluded so
        // a later revive cannot be confused with ordinary healing.
        if (isInsideMaxHpGain || previous <= 0)
        {
            return;
        }

        var actualHealing = ActualDelta.Positive(previous, current);
        if (actualHealing > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, StatKind.HealingDone, actualHealing));
        }
    }

    public void RecordMaxHpChanged(ulong playerNetId, int previous, int current)
    {
        var actualGain = ActualDelta.Positive(previous, current);
        if (actualGain > 0)
        {
            _state.TryApply(StatMutation.Add(playerNetId, StatKind.MaxHpGained, actualGain));
        }
    }
}
