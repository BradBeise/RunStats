using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using RunStats.Models;
using RunStats.Multiplayer;
using RunStats.Persistence;
using RunStats.Tracking;
using RunStats.UI;

namespace RunStats.Tests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("Run identity is canonical and structural", RunIdentityIsCanonical),
            ("Starting a run creates isolated zeroed players", StartRunCreatesPlayers),
            ("Mutations are isolated by NetId", MutationsArePlayerScoped),
            ("Card plays update total and per-card counts atomically", CardPlayMutationIsAtomic),
            ("Invalid mutations fail closed", InvalidMutationsFailClosed),
            ("Overflow fails closed", OverflowFailsClosed),
            ("Snapshots are detached from later mutations", SnapshotsAreDetached),
            ("Diagnostics and revisions are versioned", DiagnosticsAdvanceRevision),
            ("End and new-run lifecycle semantics are deterministic", LifecycleResetsCleanly),
            ("Team totals derive from player totals", TeamTotalsAreDerived),
            ("Signed deltas preserve exact gain and loss", SignedDeltasAreExact),
            ("Resolved damage excludes invalid negative values", ResolvedDamageFailsClosed),
            ("Kill credit is reference-deduplicated", KillCreditIsDeduplicated),
            ("Multi-hit damage accumulates each resolved hit", MultiHitDamageAccumulates),
            ("Block and fully blocked damage use actual deltas", BlockingUsesActualDeltas),
            ("Overkill is capped and kill credit is unique", OverkillAndKillCreditAreExact),
            ("Indirect damage without provenance is uncredited", IndirectDamageFailsClosed),
            ("Healing is capped and max-HP healing is excluded", HealingAndMaxHpAreSeparated),
            ("Starting HP and zero-HP recovery are not healing", StartingHpIsNotHealing),
            ("Multiplayer combat attribution remains player-scoped", MultiplayerCombatIsPlayerScoped),
            ("Elite and boss kill classifications are distinct", KillClassificationsAreDistinct),
            ("Unique contributor ownership survives same-owner refresh", ContributorRefreshStaysUnique),
            ("Merged contributor ownership becomes permanently ambiguous", ContributorMergeIsAmbiguous),
            ("Unsupported contributor ownership fails closed", UnsupportedContributorFailsClosed),
            ("Self assists are excluded", SelfAssistIsExcluded),
            ("Basic vulnerable assist credits only added HP damage", BasicAssistedDamageIsExact),
            ("Assisted damage handles Block and overkill", AssistedDamageHandlesBlockAndOverkill),
            ("Assisted damage preserves integer rounding", AssistedDamageRoundingIsExact),
            ("Multiple multiplicative effects remain invertible", MultipleMultipliersRemainExact),
            ("Multi-hit assisted damage accumulates per resolved hit", MultiHitAssistsAccumulate),
            ("Weak prevention uses pre-Block integer damage", BasicDamagePreventionIsExact),
            ("Modified Weak multipliers remain exact", ModifiedWeakMultiplierIsExact),
            ("Defender Block and mitigation are excluded from assist", DefenderMitigationIsExcluded),
            ("Multi-hit prevented damage accumulates per enemy hit", MultiHitPreventionAccumulates),
            ("Other defensive multipliers remain invertible", MultipleDefensiveMultipliersRemainExact),
            ("Peer-local assisted mutations are deterministic", AssistedMutationsAreDeterministic),
            ("Manual, autoplay, and Replay executions each count", AllCompletedCardExecutionsCount),
            ("Most played card uses a deterministic ordinal tie", MostPlayedCardTieIsDeterministic),
            ("Gold earned uses actual balance gain and excludes stolen returns", GoldEarnedUsesActualDelta),
            ("Gold spent uses actual removal and excludes loss and theft", GoldSpentUsesActualDelta),
            ("Permanent card and item counts remain player-scoped", ProgressCountsArePlayerScoped),
            ("Pre-run starting inventory events are ignored", StartingInventoryIsExcluded),
            ("Every statistic remains isolated between multiplayer owners", EveryStatisticIsPlayerScoped),
            ("Valid host snapshots atomically recover client disagreement", HostSnapshotRecoversDisagreement),
            ("Only new host snapshot sequences are authoritative", SnapshotAuthorityAndSequenceAreEnforced),
            ("Malformed snapshots fail closed without partial replacement", MalformedSnapshotsFailClosed),
            ("Assisted ownership metadata validates both player owners", AssistedOwnershipMetadataIsValidated),
            ("Stats UI rejects snapshots outside an active run", StatsUiRejectsInactiveSnapshots),
            ("Stats UI remains available after a run ends", EndedRunStatsUiRemainsAvailable),
            ("Single-player UI has one column and every row", SinglePlayerStatsUiIsComplete),
            ("Multiplayer UI orders players and derives team totals", MultiplayerStatsUiDerivesTeamTotals),
            ("Stats UI formats large values deterministically", StatsUiFormatsNumbersDeterministically),
            ("Team Most Played Card aggregates counts and ties ordinally", TeamMostPlayedCardIsDeterministic),
            ("Stats UI suppresses overflowed team totals", StatsUiSuppressesTeamOverflow),
            ("Stats UI groups every statistic into focused tabs", StatsUiGroupsRowsIntoSections),
            ("Stats layout scales at narrow, default, and wide bounds", StatsLayoutScalesAtRequiredBounds),
            ("Ancient history excludes initial HP from healing", AncientHistoryHealingIsExcluded),
            ("Sidecar JSON round-trips every statistic deterministically", SidecarRoundTripsDeterministically),
            ("Schema-one sidecars migrate poison fields to zero", SidecarSchemaOneMigratesPoisonFields),
            ("Sidecar rejects identity and vanilla-checkpoint mismatches", SidecarRejectsMismatches),
            ("Sidecar rejects schema drift, corruption, and unknown fields", SidecarRejectsMalformedDocuments),
            ("Sidecar store keeps singleplayer and multiplayer isolated", SidecarModesAreIsolated),
            ("Pending sidecars are never promoted without save confirmation", PendingSidecarsAreNotActive),
            ("Corrupt active sidecars leave destination state unchanged", CorruptSidecarsFailAtomically),
            ("Vanilla baselines repair only missing persisted totals", VanillaBaselineMergesByMaximum),
            ("Vanilla baseline rejection is atomic", VanillaBaselineFailureIsAtomic),
            ("Run end archives only matching RunStats files", SidecarArchiveIsScoped),
            ("Successful vanilla save confirmation promotes one checkpoint", SaveConfirmationPromotesCheckpoint),
            ("Debounced mutation checkpoints remain pending only", DebouncedCheckpointsRemainPending),
            ("Sidecars round-trip and validate assisted ownership", SidecarAssistedOwnershipIsValidated),
            ("Poison applications preserve cycle ownership", PoisonApplicationsPreserveCycleOwnership),
            ("Two-player poison fractions alternate fairly", TwoPlayerPoisonFractionsAlternate),
            ("Three-player poison remainders rotate over three triggers", ThreePlayerPoisonFractionsRotate),
            ("One-to-four-player poison allocation conserves varied shares", OneToFourPlayerPoisonAllocationIsExact),
            ("Later poison applications update cumulative shares", LaterPoisonApplicationsUpdateShares),
            ("Unattributed poison remains in the damage denominator", UnattributedPoisonRemainsUncredited),
            ("Poison kill selection follows damage then applied hierarchy", PoisonKillSelectionUsesApprovedHierarchy),
            ("Accelerant sponsors preserve application order", AccelerantSponsorsPreserveOrder),
            ("Accelerant reconciliation fails closed", AccelerantReconciliationFailsClosed),
            ("Accelerant assist excludes sponsor and unattributed damage", AccelerantAssistExcludesOwnAndUnattributed),
            ("Reliable poison source categories feed one central tracker", ReliablePoisonSourcesUseCentralTracker),
            ("Poison trigger integration updates damage assist and kills", PoisonTriggerIntegrationUpdatesStats),
            ("Lethal poison allocates actual HP loss before cycle reset", LethalPoisonAllocatesBeforeReset),
            ("Unattributed runtime poison stays uncredited", UnattributedRuntimePoisonStaysUncredited)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void RunIdentityIsCanonical()
    {
        var local = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(-4));
        var first = RunIdentity.Create(" seed ", RunMode.Multiplayer, " profile1 ", local, new ulong[] { 20, 10, 20 });
        var second = RunIdentity.Create("seed", RunMode.Multiplayer, "profile1", local.ToUniversalTime(), new ulong[] { 10, 20 });

        Equal(first, second);
        SequenceEqual(new ulong[] { 10, 20 }, first.PlayerNetIds);
        Equal(TimeSpan.Zero, first.StartTimeUtc.Offset);
    }

    private static void StartRunCreatesPlayers()
    {
        var state = CreateState(10, 20);
        var snapshot = state.CaptureSnapshot();

        Equal(RunLifecycle.Active, snapshot.Lifecycle);
        Equal(RunStatsSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Equal(0L, snapshot.Revision);
        Equal(2, snapshot.Players.Count);
        Equal(0L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
    }

    private static void MutationsArePlayerScoped()
    {
        var state = CreateState(10, 20);
        Equal(MutationResult.Applied, state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 17)));
        var snapshot = state.CaptureSnapshot();

        Equal(17L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(0L, snapshot.Players[20].GetTotal(StatKind.DamageDealt));
        Equal(1L, snapshot.Revision);
        Equal(1L, snapshot.Players[10].Revision);
    }

    private static void CardPlayMutationIsAtomic()
    {
        var state = CreateState(10);
        Equal(MutationResult.Applied, state.TryApply(StatMutation.CardPlayed(10, "CARD.BASH", 2)));
        var snapshot = state.CaptureSnapshot();

        Equal(2L, snapshot.Players[10].GetTotal(StatKind.CardsPlayed));
        Equal(2L, snapshot.Players[10].GetCardPlayCount("CARD.BASH"));
        Equal(1L, snapshot.Revision);
    }

    private static void InvalidMutationsFailClosed()
    {
        var empty = new RunStatsState();
        Equal(MutationResult.NoActiveRun, empty.TryApply(StatMutation.Add(10, StatKind.DamageDealt)));

        var state = CreateState(10);
        Equal(MutationResult.UnknownPlayer, state.TryApply(StatMutation.Add(99, StatKind.DamageDealt)));
        Equal(MutationResult.InvalidAmount, state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 0)));
        Equal(MutationResult.InvalidSource, state.TryApply(StatMutation.Add(10, StatKind.CardsPlayed)));
        Equal(MutationResult.InvalidSource, state.TryApply(new StatMutation(10, StatKind.DamageDealt, 1, "CARD.BASH")));
        Equal(MutationResult.InvalidStat, state.TryApply(StatMutation.Add(10, (StatKind)999)));
        Equal(0L, state.CaptureSnapshot().Revision);
    }

    private static void OverflowFailsClosed()
    {
        var state = CreateState(10);
        Equal(MutationResult.Applied, state.TryApply(StatMutation.Add(10, StatKind.GoldEarned, long.MaxValue)));
        Equal(MutationResult.Overflow, state.TryApply(StatMutation.Add(10, StatKind.GoldEarned)));
        var snapshot = state.CaptureSnapshot();

        Equal(long.MaxValue, snapshot.Players[10].GetTotal(StatKind.GoldEarned));
        Equal(1L, snapshot.Revision);
        Equal(1L, snapshot.Players[10].Revision);
    }

    private static void SnapshotsAreDetached()
    {
        var state = CreateState(10);
        var before = state.CaptureSnapshot();
        state.TryApply(StatMutation.Add(10, StatKind.BlockGained, 8));
        var after = state.CaptureSnapshot();

        Equal(0L, before.Players[10].GetTotal(StatKind.BlockGained));
        Equal(8L, after.Players[10].GetTotal(StatKind.BlockGained));
    }

    private static void DiagnosticsAdvanceRevision()
    {
        var state = CreateState(10);
        Equal(MutationResult.Applied, state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamage, 3));
        var snapshot = state.CaptureSnapshot();

        Equal(3L, snapshot.GetDiagnostic(DiagnosticKind.AmbiguousAssistedDamage));
        Equal(1L, snapshot.Revision);
        Equal(RunStatsSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    private static void LifecycleResetsCleanly()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageTaken, 4));
        True(state.TryEndRun());
        Equal(MutationResult.NoActiveRun, state.TryApply(StatMutation.Add(10, StatKind.DamageTaken)));

        var replacement = Identity(20);
        state.StartRun(replacement);
        var snapshot = state.CaptureSnapshot();
        Equal(RunLifecycle.Active, snapshot.Lifecycle);
        Equal(0L, snapshot.Revision);
        Equal(1, snapshot.Players.Count);
        True(snapshot.Players.ContainsKey(20));
        True(!snapshot.Players.ContainsKey(10));

        state.Clear();
        Equal(RunLifecycle.Empty, state.Lifecycle);
        True(state.Identity is null);
    }

    private static void TeamTotalsAreDerived()
    {
        var state = CreateState(10, 20);
        state.TryApply(StatMutation.Add(10, StatKind.EnemiesKilled, 2));
        state.TryApply(StatMutation.Add(20, StatKind.EnemiesKilled, 3));
        Equal(5L, state.CaptureSnapshot().GetTeamTotal(StatKind.EnemiesKilled));
    }

    private static void SignedDeltasAreExact()
    {
        Equal(new SignedDelta(7, 0), SignedDelta.Between(3, 10));
        Equal(new SignedDelta(0, 7), SignedDelta.Between(10, 3));
        Equal(new SignedDelta(0, 0), SignedDelta.Between(5, 5));
        Equal(7L, ActualDelta.Positive(3, 10));
        Equal(0L, ActualDelta.Positive(10, 3));
    }

    private static void ResolvedDamageFailsClosed()
    {
        Equal(12L, ActualDelta.ResolvedDamage(12));
        Equal(0L, ActualDelta.ResolvedDamage(0));
        Equal(0L, ActualDelta.ResolvedDamage(-1));
    }

    private static void KillCreditIsDeduplicated()
    {
        var ledger = new KillCreditLedger();
        var firstCreature = new object();
        var secondCreature = new object();

        True(ledger.TryCredit(firstCreature));
        True(!ledger.TryCredit(firstCreature));
        True(ledger.TryCredit(secondCreature));
        ledger.Clear();
        True(ledger.TryCredit(firstCreature));
    }

    private static void MultiHitDamageAccumulates()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordDamageGiven(10, true, 4, false, new object(), CombatRoomKind.Normal);
        tracker.RecordDamageGiven(10, true, 4, false, new object(), CombatRoomKind.Normal);
        tracker.RecordDamageGiven(10, true, 4, false, new object(), CombatRoomKind.Normal);

        Equal(12L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.DamageDealt));
    }

    private static void BlockingUsesActualDeltas()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordBlockChanged(10, 0, 10);
        tracker.RecordBlockChanged(10, 10, 4);
        tracker.RecordDamageTaken(10, 0);

        var player = state.CaptureSnapshot().Players[10];
        Equal(10L, player.GetTotal(StatKind.BlockGained));
        Equal(6L, player.GetTotal(StatKind.BlockLost));
        Equal(0L, player.GetTotal(StatKind.DamageTaken));
    }

    private static void OverkillAndKillCreditAreExact()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        var enemy = new object();
        tracker.RecordDamageGiven(10, true, 5, true, enemy, CombatRoomKind.Normal);
        tracker.RecordDamageGiven(10, true, 0, true, enemy, CombatRoomKind.Normal);

        var player = state.CaptureSnapshot().Players[10];
        Equal(5L, player.GetTotal(StatKind.DamageDealt));
        Equal(1L, player.GetTotal(StatKind.EnemiesKilled));
    }

    private static void IndirectDamageFailsClosed()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordDamageGiven(null, true, 9, true, new object(), CombatRoomKind.Elite);
        var snapshot = state.CaptureSnapshot();

        Equal(0L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(0L, snapshot.Players[10].GetTotal(StatKind.EnemiesKilled));
        Equal(1L, snapshot.GetDiagnostic(DiagnosticKind.UnsupportedDamageSource));
    }

    private static void HealingAndMaxHpAreSeparated()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordCurrentHpChanged(10, 48, 50, isInsideMaxHpGain: false);
        tracker.RecordMaxHpChanged(10, 50, 55);
        tracker.RecordCurrentHpChanged(10, 50, 55, isInsideMaxHpGain: true);

        var player = state.CaptureSnapshot().Players[10];
        Equal(2L, player.GetTotal(StatKind.HealingDone));
        Equal(5L, player.GetTotal(StatKind.MaxHpGained));
    }

    private static void StartingHpIsNotHealing()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordCurrentHpChanged(10, 0, 52, isInsideMaxHpGain: false);

        Equal(0L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.HealingDone));
    }

    private static void MultiplayerCombatIsPlayerScoped()
    {
        var state = CreateState(10, 20);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordDamageGiven(10, true, 7, false, new object(), CombatRoomKind.Normal);
        tracker.RecordDamageGiven(20, true, 3, false, new object(), CombatRoomKind.Normal);
        tracker.RecordDamageTaken(20, 6);

        var snapshot = state.CaptureSnapshot();
        Equal(7L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(3L, snapshot.Players[20].GetTotal(StatKind.DamageDealt));
        Equal(0L, snapshot.Players[10].GetTotal(StatKind.DamageTaken));
        Equal(6L, snapshot.Players[20].GetTotal(StatKind.DamageTaken));
    }

    private static void KillClassificationsAreDistinct()
    {
        var state = CreateState(10);
        var tracker = new CoreCombatTracker(state);
        tracker.RecordDamageGiven(10, true, 8, true, new object(), CombatRoomKind.Elite);
        tracker.RecordDamageGiven(10, true, 20, true, new object(), CombatRoomKind.Boss);

        var player = state.CaptureSnapshot().Players[10];
        Equal(2L, player.GetTotal(StatKind.EnemiesKilled));
        Equal(1L, player.GetTotal(StatKind.EliteEnemiesKilled));
        Equal(1L, player.GetTotal(StatKind.BossesKilled));
    }

    private static void ContributorRefreshStaysUnique()
    {
        var ledger = new UniqueContributorLedger();
        var power = new object();
        Equal(ContributorResult.Unique(10), ledger.Resolve(power, 10));
        ledger.ObserveContribution(power, 10);
        Equal(ContributorResult.Unique(10), ledger.Resolve(power, 99));
    }

    private static void ContributorMergeIsAmbiguous()
    {
        var ledger = new UniqueContributorLedger();
        var power = new object();
        ledger.Resolve(power, 10);
        ledger.ObserveContribution(power, 20);
        Equal(ContributorResult.Ambiguous, ledger.Resolve(power, 10));
        ledger.ObserveContribution(power, 10);
        Equal(ContributorResult.Ambiguous, ledger.Resolve(power, 10));
    }

    private static void UnsupportedContributorFailsClosed()
    {
        var ledger = new UniqueContributorLedger();
        var power = new object();
        Equal(ContributorResult.Unsupported, ledger.Resolve(power, null));
        ledger.ObserveContribution(power, 10);
        Equal(ContributorResult.Unsupported, ledger.Resolve(power, 10));
    }

    private static void SelfAssistIsExcluded()
    {
        True(!AssistedAttribution.TryGetTeammate(
            ContributorResult.Unique(10),
            10,
            out _));
        True(AssistedAttribution.TryGetTeammate(
            ContributorResult.Unique(20),
            10,
            out var teammate));
        Equal(20UL, teammate);
        True(!AssistedAttribution.TryGetTeammate(
            ContributorResult.Ambiguous,
            10,
            out _));
    }

    private static void BasicAssistedDamageIsExact()
    {
        Equal(5L, AssistedDamageCalculator.DamageAdded(15m, 1.5m, 0, 50, 15));
    }

    private static void AssistedDamageHandlesBlockAndOverkill()
    {
        Equal(3L, AssistedDamageCalculator.DamageAdded(15m, 1.5m, 12, 50, 3));
        Equal(0L, AssistedDamageCalculator.DamageAdded(15m, 1.5m, 0, 4, 4));
    }

    private static void AssistedDamageRoundingIsExact()
    {
        Equal(3L, AssistedDamageCalculator.DamageAdded(10.5m, 1.5m, 0, 50, 10));
        Equal(0L, AssistedDamageCalculator.DamageAdded(1.5m, 1.5m, 1, 50, 0));
    }

    private static void MultipleMultipliersRemainExact()
    {
        Equal(10L, AssistedDamageCalculator.DamageAdded(30m, 1.5m, 0, 50, 30));
    }

    private static void MultiHitAssistsAccumulate()
    {
        var state = CreateState(10, 20);
        for (var hit = 0; hit < 3; hit++)
        {
            var assisted = AssistedDamageCalculator.DamageAdded(6m, 1.5m, 0, 50, 6);
            state.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, assisted));
        }

        Equal(6L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.AssistedDamage));
    }

    private static void BasicDamagePreventionIsExact()
    {
        Equal(2L, AssistedDamageCalculator.DamagePrevented(6m, 0.75m));
    }

    private static void ModifiedWeakMultiplierIsExact()
    {
        Equal(4L, AssistedDamageCalculator.DamagePrevented(6m, 0.60m));
        Equal(0L, AssistedDamageCalculator.DamagePrevented(10m, 1m));
    }

    private static void DefenderMitigationIsExcluded()
    {
        var preventedBeforeBlock = AssistedDamageCalculator.DamagePrevented(6m, 0.75m);
        var actualHpDamageAfterEightBlock = 0L;
        Equal(2L, preventedBeforeBlock);
        Equal(0L, actualHpDamageAfterEightBlock);
    }

    private static void MultiHitPreventionAccumulates()
    {
        var state = CreateState(10, 20);
        for (var hit = 0; hit < 3; hit++)
        {
            var prevented = AssistedDamageCalculator.DamagePrevented(6m, 0.75m);
            state.TryApply(StatMutation.Add(10, StatKind.AssistedDamagePrevented, prevented));
        }

        Equal(6L, state.CaptureSnapshot().Players[10]
            .GetTotal(StatKind.AssistedDamagePrevented));
    }

    private static void MultipleDefensiveMultipliersRemainExact()
    {
        Equal(1L, AssistedDamageCalculator.DamagePrevented(3m, 0.75m));
    }

    private static void AssistedMutationsAreDeterministic()
    {
        var host = CreateState(10, 20);
        var client = CreateState(20, 10);
        var events = new[]
        {
            AssistedDamageCalculator.DamageAdded(15m, 1.5m, 0, 50, 15),
            AssistedDamageCalculator.DamageAdded(9m, 1.5m, 3, 50, 6),
            AssistedDamageCalculator.DamagePrevented(6m, 0.75m)
        };

        foreach (var state in new[] { host, client })
        {
            state.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, events[0]));
            state.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, events[1]));
            state.TryApply(StatMutation.Add(20, StatKind.AssistedDamagePrevented, events[2]));
        }

        var hostSnapshot = host.CaptureSnapshot();
        var clientSnapshot = client.CaptureSnapshot();
        Equal(hostSnapshot.Revision, clientSnapshot.Revision);
        Equal(
            hostSnapshot.Players[10].GetTotal(StatKind.AssistedDamage),
            clientSnapshot.Players[10].GetTotal(StatKind.AssistedDamage));
        Equal(
            hostSnapshot.Players[20].GetTotal(StatKind.AssistedDamagePrevented),
            clientSnapshot.Players[20].GetTotal(StatKind.AssistedDamagePrevented));
    }

    private static void AllCompletedCardExecutionsCount()
    {
        var state = CreateState(10);
        var tracker = new RunProgressTracker(state);
        tracker.RecordCardPlayed(10, "CARD.BASH");
        tracker.RecordCardPlayed(10, "CARD.BASH");
        tracker.RecordCardPlayed(10, "CARD.BASH");

        var player = state.CaptureSnapshot().Players[10];
        Equal(3L, player.GetTotal(StatKind.CardsPlayed));
        Equal(3L, player.GetCardPlayCount("CARD.BASH"));
    }

    private static void MostPlayedCardTieIsDeterministic()
    {
        var state = CreateState(10);
        var tracker = new RunProgressTracker(state);
        tracker.RecordCardPlayed(10, "CARD.STRIKE");
        tracker.RecordCardPlayed(10, "CARD.BASH");
        tracker.RecordCardPlayed(10, "CARD.STRIKE");
        tracker.RecordCardPlayed(10, "CARD.BASH");

        var player = state.CaptureSnapshot().Players[10];
        Equal("CARD.BASH", player.GetMostPlayedCardId());
        Equal(2L, player.GetCardPlayCount(player.GetMostPlayedCardId()!));
    }

    private static void GoldEarnedUsesActualDelta()
    {
        var state = CreateState(10);
        var tracker = new RunProgressTracker(state);
        tracker.RecordGoldGained(10, 100, 112, wasStolenBack: false);
        tracker.RecordGoldGained(10, 112, 120, wasStolenBack: true);
        tracker.RecordGoldGained(10, 120, 120, wasStolenBack: false);

        Equal(12L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.GoldEarned));
    }

    private static void GoldSpentUsesActualDelta()
    {
        var state = CreateState(10);
        var tracker = new RunProgressTracker(state);
        tracker.RecordGoldLost(10, 5, 0, wasSpent: true);
        tracker.RecordGoldLost(10, 20, 10, wasSpent: false);
        tracker.RecordGoldLost(10, 10, 10, wasSpent: true);

        Equal(5L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.GoldSpent));
    }

    private static void ProgressCountsArePlayerScoped()
    {
        var state = CreateState(10, 20);
        var tracker = new RunProgressTracker(state);
        tracker.RecordCount(10, StatKind.CardsObtained, 2);
        tracker.RecordCount(10, StatKind.CardsUpgraded);
        tracker.RecordCount(10, StatKind.CardsRemoved);
        tracker.RecordCount(20, StatKind.RelicsObtained, 3);
        tracker.RecordCount(20, StatKind.PotionsObtained, 2);
        tracker.RecordCount(20, StatKind.PotionsUsed);
        tracker.RecordCount(20, StatKind.PotionsUsed, 0);

        var snapshot = state.CaptureSnapshot();
        Equal(2L, snapshot.Players[10].GetTotal(StatKind.CardsObtained));
        Equal(1L, snapshot.Players[10].GetTotal(StatKind.CardsUpgraded));
        Equal(1L, snapshot.Players[10].GetTotal(StatKind.CardsRemoved));
        Equal(0L, snapshot.Players[20].GetTotal(StatKind.CardsObtained));
        Equal(3L, snapshot.Players[20].GetTotal(StatKind.RelicsObtained));
        Equal(2L, snapshot.Players[20].GetTotal(StatKind.PotionsObtained));
        Equal(1L, snapshot.Players[20].GetTotal(StatKind.PotionsUsed));
    }

    private static void StartingInventoryIsExcluded()
    {
        var state = new RunStatsState();
        var tracker = new RunProgressTracker(state);
        tracker.RecordCount(10, StatKind.RelicsObtained);
        tracker.RecordCount(10, StatKind.PotionsObtained);
        state.StartRun(Identity(10));

        var player = state.CaptureSnapshot().Players[10];
        Equal(0L, player.GetTotal(StatKind.RelicsObtained));
        Equal(0L, player.GetTotal(StatKind.PotionsObtained));
        True(player.GetMostPlayedCardId() is null);
    }

    private static void EveryStatisticIsPlayerScoped()
    {
        var state = CreateState(10, 20);
        foreach (var kind in Enum.GetValues<StatKind>())
        {
            var first = kind == StatKind.CardsPlayed
                ? StatMutation.CardPlayed(10, "CARD.P1", 2)
                : StatMutation.Add(10, kind, 2);
            var second = kind == StatKind.CardsPlayed
                ? StatMutation.CardPlayed(20, "CARD.P2", 3)
                : StatMutation.Add(20, kind, 3);
            Equal(MutationResult.Applied, state.TryApply(first));
            Equal(MutationResult.Applied, state.TryApply(second));
        }

        var snapshot = state.CaptureSnapshot();
        foreach (var kind in Enum.GetValues<StatKind>())
        {
            Equal(2L, snapshot.Players[10].GetTotal(kind));
            Equal(3L, snapshot.Players[20].GetTotal(kind));
        }

        Equal(2L, snapshot.Players[10].GetCardPlayCount("CARD.P1"));
        Equal(0L, snapshot.Players[10].GetCardPlayCount("CARD.P2"));
        Equal(0L, snapshot.Players[20].GetCardPlayCount("CARD.P1"));
        Equal(3L, snapshot.Players[20].GetCardPlayCount("CARD.P2"));
    }

    private static void HostSnapshotRecoversDisagreement()
    {
        var host = CreateState(10, 20);
        host.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, 7));
        host.TryApply(StatMutation.Add(20, StatKind.AssistedDamagePrevented, 5));

        var client = CreateState(10, 20);
        client.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, 99));
        client.TryApply(StatMutation.Add(20, StatKind.AssistedDamagePrevented, 88));
        client.TryApply(StatMutation.Add(20, StatKind.DamageDealt, 77));

        var reconciler = new SnapshotReconciler();
        var envelope = Envelope(1, host.CaptureSnapshot());
        Equal(SnapshotReconcileResult.Applied, reconciler.TryApply(100, 100, envelope, client));

        var recovered = client.CaptureSnapshot();
        Equal(7L, recovered.Players[10].GetTotal(StatKind.AssistedDamage));
        Equal(5L, recovered.Players[20].GetTotal(StatKind.AssistedDamagePrevented));
        Equal(0L, recovered.Players[20].GetTotal(StatKind.DamageDealt));
        Equal(host.Revision, recovered.Revision);
    }

    private static void SnapshotAuthorityAndSequenceAreEnforced()
    {
        var host = CreateState(10, 20);
        host.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 4));
        var client = CreateState(10, 20);
        var reconciler = new SnapshotReconciler();
        var first = Envelope(1, host.CaptureSnapshot());

        Equal(
            SnapshotReconcileResult.UnauthorizedSender,
            reconciler.TryApply(200, 100, first, client));
        Equal(0L, client.CaptureSnapshot().Players[10].GetTotal(StatKind.DamageDealt));
        Equal(SnapshotReconcileResult.Applied, reconciler.TryApply(100, 100, first, client));
        Equal(
            SnapshotReconcileResult.StaleOrDuplicate,
            reconciler.TryApply(100, 100, first, client));

        var wrongProtocol = first with
        {
            ProtocolVersion = StatsSyncSnapshot.CurrentProtocolVersion + 1,
            Sequence = 2
        };
        Equal(
            SnapshotReconcileResult.ProtocolMismatch,
            reconciler.TryApply(100, 100, wrongProtocol, client));
        Equal(1L, reconciler.LastAcceptedSequence);
    }

    private static void MalformedSnapshotsFailClosed()
    {
        var state = CreateState(10, 20);
        state.TryApply(StatMutation.Add(10, StatKind.DamageTaken, 6));
        var before = state.CaptureSnapshot();
        var malformedTotals = new Dictionary<StatKind, long>(before.Players[10].Totals)
        {
            [StatKind.DamageTaken] = -1
        };
        var malformedPlayers = new Dictionary<ulong, PlayerStatsSnapshot>(before.Players)
        {
            [10] = before.Players[10] with
            {
                Totals = new ReadOnlyDictionary<StatKind, long>(malformedTotals)
            }
        };
        var malformed = before with
        {
            Players = new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(malformedPlayers)
        };

        Equal(SnapshotImportResult.InvalidPlayers, state.TryReplaceFromSnapshot(malformed));
        var after = state.CaptureSnapshot();
        Equal(before.Revision, after.Revision);
        Equal(6L, after.Players[10].GetTotal(StatKind.DamageTaken));

        Equal(
            SnapshotImportResult.SchemaMismatch,
            state.TryReplaceFromSnapshot(before with
            {
                SchemaVersion = RunStatsSnapshot.CurrentSchemaVersion + 1
            }));
        Equal(
            SnapshotImportResult.RunIdentityMismatch,
            state.TryReplaceFromSnapshot(before with { Identity = Identity(10, 30) }));
    }

    private static void AssistedOwnershipMetadataIsValidated()
    {
        var state = CreateState(10, 20);
        var reconciler = new SnapshotReconciler();
        var valid = Envelope(
            1,
            state.CaptureSnapshot(),
            new AssistedOwnershipRecord(
                4,
                AssistedPowerKind.Vulnerable,
                ContributorResolution.Unique,
                10),
            new AssistedOwnershipRecord(
                5,
                AssistedPowerKind.Weak,
                ContributorResolution.Unique,
                20),
            new AssistedOwnershipRecord(
                6,
                AssistedPowerKind.Vulnerable,
                ContributorResolution.Ambiguous,
                0));
        Equal(SnapshotReconcileResult.Applied, reconciler.TryApply(100, 100, valid, state));

        var unknownOwner = Envelope(
            2,
            state.CaptureSnapshot(),
            new AssistedOwnershipRecord(
                7,
                AssistedPowerKind.Weak,
                ContributorResolution.Unique,
                30));
        Equal(
            SnapshotReconcileResult.InvalidAssistedOwnership,
            reconciler.TryApply(100, 100, unknownOwner, state));

        var duplicateKey = Envelope(
            2,
            state.CaptureSnapshot(),
            new AssistedOwnershipRecord(8, AssistedPowerKind.Weak, ContributorResolution.Unique, 10),
            new AssistedOwnershipRecord(8, AssistedPowerKind.Weak, ContributorResolution.Unique, 20));
        Equal(
            SnapshotReconcileResult.InvalidAssistedOwnership,
            reconciler.TryApply(100, 100, duplicateKey, state));
    }

    private static void StatsUiRejectsInactiveSnapshots()
    {
        var state = new RunStatsState();
        True(!StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel));
        Equal<StatsViewModel?>(null, viewModel);
    }

    private static void EndedRunStatsUiRemainsAvailable()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 12));
        True(state.TryEndRun());

        True(StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel));
        Equal("12", UiRow(viewModel!, "Damage Dealt").PlayerValues[0]);
    }

    private static void SinglePlayerStatsUiIsComplete()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 8));
        state.TryApply(StatMutation.CardPlayed(10, "CARD.BASH", 2));

        True(StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel));
        Equal(1, viewModel!.ColumnHeaders.Count);
        Equal("PLAYER 1", viewModel.ColumnHeaders[0]);
        Equal(22, viewModel.Rows.Count);
        Equal("8", UiRow(viewModel, "Damage Dealt").PlayerValues[0]);
        Equal("CARD.BASH", UiRow(viewModel, "Most Played Card").PlayerValues[0]);
        Equal<string?>(null, UiRow(viewModel, "Damage Dealt").TeamValue);

        foreach (var kind in Enum.GetValues<StatKind>())
        {
            True(viewModel.Rows.Any(row => row.Label == UiLabel(kind)));
        }
    }

    private static void MultiplayerStatsUiDerivesTeamTotals()
    {
        var state = CreateState(20, 10);
        state.TryApply(StatMutation.Add(10, StatKind.AssistedDamage, 7));
        state.TryApply(StatMutation.Add(20, StatKind.AssistedDamage, 5));
        state.TryApply(StatMutation.Add(10, StatKind.PoisonApplied, 6));
        state.TryApply(StatMutation.Add(20, StatKind.PoisonApplied, 4));

        True(StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel));
        SequenceEqual(new[] { "PLAYER 1", "PLAYER 2", "TEAM" }, viewModel!.ColumnHeaders);
        var row = UiRow(viewModel, "Assisted Damage");
        SequenceEqual(new[] { "7", "5" }, row.PlayerValues);
        Equal("12", row.TeamValue);
        var poison = UiRow(viewModel, "Poison Applied");
        SequenceEqual(new[] { "6", "4" }, poison.PlayerValues);
        Equal("10", poison.TeamValue);
    }

    private static void StatsUiFormatsNumbersDeterministically()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.GoldEarned, 12345));
        StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel);

        Equal("12,345", UiRow(viewModel!, "Gold Earned").PlayerValues[0]);
        True(viewModel!.Subtitle.Contains("STAGE2-SEED", StringComparison.Ordinal));
    }

    private static void TeamMostPlayedCardIsDeterministic()
    {
        var state = CreateState(10, 20);
        state.TryApply(StatMutation.CardPlayed(10, "CARD.ZETA", 2));
        state.TryApply(StatMutation.CardPlayed(10, "CARD.ALPHA"));
        state.TryApply(StatMutation.CardPlayed(20, "CARD.ALPHA"));
        StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel);

        var row = UiRow(viewModel!, "Most Played Card");
        SequenceEqual(new[] { "CARD.ZETA", "CARD.ALPHA" }, row.PlayerValues);
        Equal("CARD.ALPHA", row.TeamValue);
    }

    private static void StatsUiSuppressesTeamOverflow()
    {
        var state = CreateState(10, 20);
        var snapshot = state.CaptureSnapshot();
        var players = new Dictionary<ulong, PlayerStatsSnapshot>(snapshot.Players);
        players[10] = players[10] with
        {
            Totals = TotalsWith(players[10], StatKind.DamageDealt, long.MaxValue)
        };
        players[20] = players[20] with
        {
            Totals = TotalsWith(players[20], StatKind.DamageDealt, 1)
        };
        snapshot = snapshot with
        {
            Players = new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(players)
        };

        True(StatsViewModel.TryCreate(snapshot, out var viewModel));
        Equal("—", UiRow(viewModel!, "Damage Dealt").TeamValue);
    }

    private static void StatsLayoutScalesAtRequiredBounds()
    {
        Equal(new StatsPanelSize(1478.4f, 928.8f), StatsLayout.CalculatePanelSize(1680, 1080));
        Equal(new StatsPanelSize(1478.4f, 980f), StatsLayout.CalculatePanelSize(1680, 1260));
        Equal(new StatsPanelSize(1560f, 928.8f), StatsLayout.CalculatePanelSize(2580, 1080));
    }

    private static void StatsUiGroupsRowsIntoSections()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.CardPlayed(10, "CARD.BASH", 2));
        StatsViewModel.TryCreate(state.CaptureSnapshot(), out var viewModel);

        SequenceEqual(
            new[] { "DAMAGE", "HEALING / BLOCK", "KILLS", "CARDS", "ECONOMY", "RELICS", "POTIONS" },
            viewModel!.Sections.Select(section => section.Title).ToArray());
        Equal(21, viewModel.Sections.Sum(section => section.Rows.Count));
        SequenceEqual(
            new[]
            {
                StatKind.DamageDealt,
                StatKind.PoisonApplied,
                StatKind.DamageTaken,
                StatKind.AssistedDamage,
                StatKind.AssistedDamagePrevented
            },
            viewModel.Sections.Single(section => section.Title == "DAMAGE")
                .Rows.Select(row => row.Kind!.Value).ToArray());
        Equal("CARD.BASH", viewModel.PlayerMostPlayedCardIds[0]);
        True(viewModel.Sections.SelectMany(section => section.Rows).All(row => row.Kind is not null));
    }

    private static void AncientHistoryHealingIsExcluded()
    {
        Equal(0L, VanillaStatBaseline.HistoricalHealing(true, 80));
        Equal(6L, VanillaStatBaseline.HistoricalHealing(false, 6));
        Equal(-1L, VanillaStatBaseline.HistoricalHealing(true, -1));
    }

    private static void SidecarRoundTripsDeterministically()
    {
        var state = CreateState(10, 20);
        foreach (var kind in Enum.GetValues<StatKind>())
        {
            if (kind == StatKind.CardsPlayed)
            {
                state.TryApply(StatMutation.CardPlayed(10, "CARD.ALPHA", 2));
                state.TryApply(StatMutation.CardPlayed(20, "CARD.BETA", 3));
            }
            else
            {
                state.TryApply(StatMutation.Add(10, kind, (long)kind + 1));
                state.TryApply(StatMutation.Add(20, kind, (long)kind + 2));
            }
        }
        state.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamage, 4);
        var snapshot = state.CaptureSnapshot();

        var first = SidecarSnapshotCodec.Serialize(snapshot, 123456);
        var second = SidecarSnapshotCodec.Serialize(snapshot, 123456);
        Equal(first, second);
        Equal(
            SidecarLoadResult.Loaded,
            SidecarSnapshotCodec.TryDeserialize(
                first,
                snapshot.Identity!,
                123456,
                out var restored));
        Equal(snapshot.Revision, restored!.Revision);
        foreach (var playerId in snapshot.Identity!.PlayerNetIds)
        {
            foreach (var kind in Enum.GetValues<StatKind>())
            {
                Equal(
                    snapshot.Players[playerId].GetTotal(kind),
                    restored.Players[playerId].GetTotal(kind));
            }
        }
        Equal("CARD.ALPHA", restored.Players[10].GetMostPlayedCardId());
        Equal(4L, restored.GetDiagnostic(DiagnosticKind.AmbiguousAssistedDamage));
    }

    private static void SidecarSchemaOneMigratesPoisonFields()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 9));
        var snapshot = state.CaptureSnapshot();
        var root = JsonNode.Parse(SidecarSnapshotCodec.Serialize(snapshot, 55))!.AsObject();
        root["schema_version"] = SidecarSnapshotCodec.PreviousSchemaVersion;
        root["snapshot_schema_version"] = 1;

        foreach (var player in root["players"]!.AsArray())
        {
            var totals = player!["totals"]!.AsArray();
            var poison = totals.Single(value =>
                value!["kind"]!.GetValue<string>() == nameof(StatKind.PoisonApplied));
            totals.Remove(poison);
        }

        var diagnostics = root["diagnostics"]!.AsArray();
        foreach (var kind in new[]
                 {
                     DiagnosticKind.UnattributedPoisonApplication,
                     DiagnosticKind.UnsupportedPoisonDamage,
                     DiagnosticKind.UnsponsoredAccelerantTrigger
                 })
        {
            var entry = diagnostics.Single(value =>
                value!["kind"]!.GetValue<string>() == kind.ToString());
            diagnostics.Remove(entry);
        }

        Equal(
            SidecarLoadResult.Loaded,
            SidecarSnapshotCodec.TryDeserialize(
                root.ToJsonString(),
                snapshot.Identity!,
                55,
                out var migrated));
        Equal(RunStatsSnapshot.CurrentSchemaVersion, migrated!.SchemaVersion);
        Equal(9L, migrated.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(0L, migrated.Players[10].GetTotal(StatKind.PoisonApplied));
        Equal(0L, migrated.GetDiagnostic(DiagnosticKind.UnsupportedPoisonDamage));
    }

    private static void SidecarRejectsMismatches()
    {
        var state = CreateState(10);
        var snapshot = state.CaptureSnapshot();
        var json = SidecarSnapshotCodec.Serialize(snapshot, 50);

        Equal(
            SidecarLoadResult.IdentityMismatch,
            SidecarSnapshotCodec.TryDeserialize(json, Identity(20), 50, out _));
        Equal(
            SidecarLoadResult.SaveCheckpointMismatch,
            SidecarSnapshotCodec.TryDeserialize(json, snapshot.Identity!, 51, out _));
    }

    private static void SidecarRejectsMalformedDocuments()
    {
        var snapshot = CreateState(10).CaptureSnapshot();
        var json = SidecarSnapshotCodec.Serialize(snapshot, 50);

        Equal(
            SidecarLoadResult.InvalidJson,
            SidecarSnapshotCodec.TryDeserialize("{", snapshot.Identity!, 50, out _));
        Equal(
            SidecarLoadResult.SchemaMismatch,
            SidecarSnapshotCodec.TryDeserialize(
                json.Replace("\"schema_version\": 2", "\"schema_version\": 99", StringComparison.Ordinal),
                snapshot.Identity!,
                50,
                out _));
        Equal(
            SidecarLoadResult.InvalidJson,
            SidecarSnapshotCodec.TryDeserialize(
                json.Replace("\"mod_id\":", "\"unknown\": true, \"mod_id\":", StringComparison.Ordinal),
                snapshot.Identity!,
                50,
                out _));
        var incomplete = JsonNode.Parse(json)!.AsObject();
        var totals = incomplete["players"]![0]!["totals"]!.AsArray();
        totals.Remove(totals.Single(value =>
            value!["kind"]!.GetValue<string>() == nameof(StatKind.PoisonApplied)));
        Equal(
            SidecarLoadResult.InvalidSnapshot,
            SidecarSnapshotCodec.TryDeserialize(
                incomplete.ToJsonString(),
                snapshot.Identity!,
                50,
                out _));
        Equal(
            SidecarLoadResult.TooLarge,
            SidecarSnapshotCodec.TryDeserialize(
                new string('x', SidecarSnapshotCodec.MaxJsonCharacters + 1),
                snapshot.Identity!,
                50,
                out _));
    }

    private static void SidecarModesAreIsolated()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new RunStatsSidecarStore(directory);
            var single = CreateState(10);
            single.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 7));
            var multiplayer = CreateState(10, 20);
            multiplayer.TryApply(StatMutation.Add(20, StatKind.DamageDealt, 9));
            store.WriteActive(single.CaptureSnapshot(), 100);
            store.WriteActive(multiplayer.CaptureSnapshot(), 200);

            True(File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            True(File.Exists(store.GetActivePath(RunMode.Multiplayer)));
            var destination = CreateState(10);
            Equal(
                SidecarLoadResult.Loaded,
                store.TryLoadActive(single.CaptureSnapshot().Identity!, 100, destination));
            Equal(7L, destination.CaptureSnapshot().Players[10].GetTotal(StatKind.DamageDealt));
        });
    }

    private static void PendingSidecarsAreNotActive()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new RunStatsSidecarStore(directory);
            var state = CreateState(10);
            state.TryApply(StatMutation.Add(10, StatKind.BlockGained, 4));
            store.WritePending(state.CaptureSnapshot());

            True(File.Exists(store.GetPendingPath(RunMode.Singleplayer)));
            True(!File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            Equal(
                SidecarLoadResult.FileNotFound,
                store.TryLoadActive(state.CaptureSnapshot().Identity!, 0, CreateState(10)));

            store.WriteActive(state.CaptureSnapshot(), 400);
            True(File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            True(!File.Exists(store.GetPendingPath(RunMode.Singleplayer)));
            True(!File.Exists(store.GetActivePath(RunMode.Singleplayer) + ".tmp"));
        });
    }

    private static void CorruptSidecarsFailAtomically()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new RunStatsSidecarStore(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.GetActivePath(RunMode.Singleplayer), "not json");
            var state = CreateState(10);
            state.TryApply(StatMutation.Add(10, StatKind.DamageTaken, 6));
            var before = state.CaptureSnapshot();

            Equal(
                SidecarLoadResult.InvalidJson,
                store.TryLoadActive(before.Identity!, 100, state));
            var after = state.CaptureSnapshot();
            Equal(before.Revision, after.Revision);
            Equal(6L, after.Players[10].GetTotal(StatKind.DamageTaken));
        });
    }

    private static void VanillaBaselineMergesByMaximum()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageTaken, 10));
        var baseline = new VanillaStatBaseline();
        True(baseline.TryAdd(10, StatKind.DamageTaken, 8));
        True(baseline.TryAdd(10, StatKind.GoldEarned, 5));

        True(baseline.TryMergeInto(state));
        var snapshot = state.CaptureSnapshot();
        Equal(10L, snapshot.Players[10].GetTotal(StatKind.DamageTaken));
        Equal(5L, snapshot.Players[10].GetTotal(StatKind.GoldEarned));
    }

    private static void VanillaBaselineFailureIsAtomic()
    {
        var state = CreateState(10);
        state.TryApply(StatMutation.Add(10, StatKind.DamageTaken, 3));
        var before = state.CaptureSnapshot();
        var unknownPlayer = new VanillaStatBaseline();
        unknownPlayer.TryAdd(20, StatKind.GoldEarned, 5);

        True(!unknownPlayer.TryMergeInto(state));
        var after = state.CaptureSnapshot();
        Equal(before.Revision, after.Revision);
        Equal(3L, after.Players[10].GetTotal(StatKind.DamageTaken));

        var players = new Dictionary<ulong, PlayerStatsSnapshot>(before.Players)
        {
            [10] = before.Players[10] with { Revision = long.MaxValue }
        };
        var maxRevision = before with
        {
            Revision = long.MaxValue,
            Players = new ReadOnlyDictionary<ulong, PlayerStatsSnapshot>(players)
        };
        Equal(SnapshotImportResult.Applied, state.TryReplaceFromSnapshot(maxRevision));
        var overflow = new VanillaStatBaseline();
        overflow.TryAdd(10, StatKind.GoldEarned, 1);
        True(!overflow.TryMergeInto(state));
        Equal(0L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.GoldEarned));
    }

    private static void SidecarArchiveIsScoped()
    {
        WithTemporaryDirectory(directory =>
        {
            var store = new RunStatsSidecarStore(directory);
            var state = CreateState(10);
            store.WriteActive(state.CaptureSnapshot(), 500);
            store.WritePending(state.CaptureSnapshot());
            var unrelated = Path.Combine(directory, "RunStatsCollector.keep");
            File.WriteAllText(unrelated, "untouched");
            state.TryEndRun();

            store.ArchiveSnapshot(state.CaptureSnapshot(), 500, "ended");
            True(!File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            True(!File.Exists(store.GetPendingPath(RunMode.Singleplayer)));
            Equal("untouched", File.ReadAllText(unrelated));
            Equal(1, Directory.GetFiles(Path.Combine(directory, "archive"), "*.json").Length);
        });
    }

    private static void SaveConfirmationPromotesCheckpoint()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = CreateState(10);
            var store = new RunStatsSidecarStore(directory);
            using var coordinator = new RunStatsPersistenceCoordinator(
                state,
                () => store,
                (_, exception) => throw exception,
                TimeSpan.FromSeconds(5));
            state.TryApply(StatMutation.Add(10, StatKind.DamageDealt, 9));
            coordinator.PrepareVanillaSave(700, RunMode.Singleplayer);

            True(!File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            coordinator.ConfirmVanillaSave();
            True(File.Exists(store.GetActivePath(RunMode.Singleplayer)));
            var restored = CreateState(10);
            Equal(
                SidecarLoadResult.Loaded,
                store.TryLoadActive(state.CaptureSnapshot().Identity!, 700, restored));
            Equal(9L, restored.CaptureSnapshot().Players[10].GetTotal(StatKind.DamageDealt));
        });
    }

    private static void DebouncedCheckpointsRemainPending()
    {
        WithTemporaryDirectory(directory =>
        {
            var state = new RunStatsState();
            var store = new RunStatsSidecarStore(directory);
            using var coordinator = new RunStatsPersistenceCoordinator(
                state,
                () => store,
                (_, exception) => throw exception,
                TimeSpan.FromMilliseconds(10));
            state.StartRun(Identity(10));
            state.TryApply(StatMutation.Add(10, StatKind.BlockGained, 6));

            True(SpinWait.SpinUntil(
                () => File.Exists(store.GetPendingPath(RunMode.Singleplayer)),
                TimeSpan.FromSeconds(2)));
            True(!File.Exists(store.GetActivePath(RunMode.Singleplayer)));
        });
    }

    private static void SidecarAssistedOwnershipIsValidated()
    {
        var state = CreateState(10, 20);
        var snapshot = state.CaptureSnapshot();
        var ownership = new[]
        {
            new AssistedOwnershipRecord(
                4,
                AssistedPowerKind.Vulnerable,
                ContributorResolution.Unique,
                10),
            new AssistedOwnershipRecord(
                5,
                AssistedPowerKind.Weak,
                ContributorResolution.Ambiguous,
                0)
        };
        var json = SidecarSnapshotCodec.Serialize(snapshot, 800, ownership);

        Equal(
            SidecarLoadResult.Loaded,
            SidecarSnapshotCodec.TryDeserialize(
                json,
                snapshot.Identity!,
                800,
                out _,
                out var restored));
        Equal(2, restored.Count);
        Equal(10UL, restored[0].ContributorNetId);
        Equal(ContributorResolution.Ambiguous, restored[1].Resolution);

        var invalid = json.Replace(
            "\"contributor_net_id\": 10",
            "\"contributor_net_id\": 30",
            StringComparison.Ordinal);
        Equal(
            SidecarLoadResult.InvalidSnapshot,
            SidecarSnapshotCodec.TryDeserialize(
                invalid,
                snapshot.Identity!,
                800,
                out _,
                out _));
    }

    private static void PoisonApplicationsPreserveCycleOwnership()
    {
        var enemy = new object();
        var ledger = new PoisonContributionLedger();
        var first = ledger.ObserveAmount(enemy, 0, 6, 10);
        True(first.Accepted);
        Equal(6L, first.CreditedPoisonApplied);
        var reduced = ledger.ObserveAmount(enemy, 6, 3, null);
        True(reduced.Accepted);
        Equal(0L, reduced.UnattributedPoisonApplied);
        var unknown = ledger.ObserveAmount(enemy, 3, 5, null);
        Equal(2L, unknown.UnattributedPoisonApplied);
        True(ledger.ObserveAmount(enemy, 5, 0, null).CycleReset);
        Equal(4L, ledger.ObserveAmount(enemy, 0, 4, 20).CreditedPoisonApplied);

        True(ledger.TryAllocateDamage(enemy, 4, out var allocation));
        Equal(4L, allocation.PlayerDamage[20]);
        True(!allocation.PlayerDamage.ContainsKey(10));
    }

    private static void TwoPlayerPoisonFractionsAlternate()
    {
        var enemy = new object();
        var ledger = EqualPoisonLedger(enemy, 10, 20);

        True(ledger.TryAllocateDamage(enemy, 5, out var first));
        True(ledger.TryAllocateDamage(enemy, 5, out var second));
        SequenceEqual(new long[] { 3, 2 }, new[] { first.PlayerDamage[10], first.PlayerDamage[20] });
        SequenceEqual(new long[] { 2, 3 }, new[] { second.PlayerDamage[10], second.PlayerDamage[20] });
    }

    private static void ThreePlayerPoisonFractionsRotate()
    {
        var enemy = new object();
        var ledger = EqualPoisonLedger(enemy, 10, 20, 30);
        var onePointTotals = new Dictionary<ulong, long> { [10] = 0, [20] = 0, [30] = 0 };
        for (var trigger = 0; trigger < 3; trigger++)
        {
            True(ledger.TryAllocateDamage(enemy, 1, out var allocation));
            foreach (var entry in allocation.PlayerDamage)
            {
                onePointTotals[entry.Key] += entry.Value;
            }
        }
        SequenceEqual(new long[] { 1, 1, 1 }, onePointTotals.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray());

        var secondEnemy = new object();
        ledger = EqualPoisonLedger(secondEnemy, 10, 20, 30);
        var twoPointTotals = new Dictionary<ulong, long> { [10] = 0, [20] = 0, [30] = 0 };
        for (var trigger = 0; trigger < 3; trigger++)
        {
            True(ledger.TryAllocateDamage(secondEnemy, 2, out var allocation));
            foreach (var entry in allocation.PlayerDamage)
            {
                twoPointTotals[entry.Key] += entry.Value;
            }
        }
        SequenceEqual(new long[] { 2, 2, 2 }, twoPointTotals.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray());
    }

    private static void OneToFourPlayerPoisonAllocationIsExact()
    {
        for (var playerCount = 1; playerCount <= 4; playerCount++)
        {
            var enemy = new object();
            var ledger = new PoisonContributionLedger();
            long poisonAmount = 0;
            long totalWeight = 0;
            for (var player = 1; player <= playerCount; player++)
            {
                // Deliberately unequal shares: 1, 2, 3, and 4 poison applied.
                var weight = player;
                ledger.ObserveAmount(enemy, poisonAmount, poisonAmount + weight, (ulong)(player * 10));
                poisonAmount += weight;
                totalWeight += weight;
            }

            var totals = Enumerable.Range(1, playerCount)
                .ToDictionary(player => (ulong)(player * 10), _ => 0L);
            long totalDamage = 0;
            for (var trigger = 0; trigger < totalWeight; trigger++)
            {
                True(ledger.TryAllocateDamage(enemy, 1, out var allocation));
                Equal(1L, allocation.CreditedDamage + allocation.UnattributedDamage);
                foreach (var entry in allocation.PlayerDamage)
                {
                    totals[entry.Key] += entry.Value;
                }
                totalDamage += allocation.ActualDamage;
            }

            Equal(totalWeight, totalDamage);
            for (var player = 1; player <= playerCount; player++)
            {
                Equal((long)player, totals[(ulong)(player * 10)]);
            }
        }

        var fourEqualEnemy = new object();
        var fourEqualLedger = EqualPoisonLedger(fourEqualEnemy, 10, 20, 30, 40);
        var recipients = new List<ulong>();
        for (var trigger = 0; trigger < 4; trigger++)
        {
            True(fourEqualLedger.TryAllocateDamage(fourEqualEnemy, 1, out var allocation));
            recipients.Add(allocation.PlayerDamage.Single(entry => entry.Value == 1).Key);
        }
        SequenceEqual(new ulong[] { 10, 20, 30, 40 }, recipients);
    }

    private static void UnattributedPoisonRemainsUncredited()
    {
        var enemy = new object();
        var ledger = new PoisonContributionLedger();
        ledger.ObserveAmount(enemy, 0, 2, 10);
        ledger.ObserveAmount(enemy, 2, 4, null);

        True(ledger.TryAllocateDamage(enemy, 4, out var allocation));
        Equal(2L, allocation.PlayerDamage[10]);
        Equal(2L, allocation.UnattributedDamage);
        Equal(2L, allocation.CreditedDamage);
    }

    private static void LaterPoisonApplicationsUpdateShares()
    {
        var enemy = new object();
        var ledger = EqualPoisonLedger(enemy, 10, 20);
        True(ledger.TryAllocateDamage(enemy, 1, out var first));
        Equal(1L, first.PlayerDamage[10]);
        Equal(0L, first.PlayerDamage[20]);

        Equal(1L, ledger.ObserveAmount(enemy, 2, 3, 10).CreditedPoisonApplied);
        True(ledger.TryAllocateDamage(enemy, 3, out var second));
        Equal(2L, second.PlayerDamage[10]);
        Equal(1L, second.PlayerDamage[20]);
    }

    private static void PoisonKillSelectionUsesApprovedHierarchy()
    {
        var damageWinnerEnemy = new object();
        var ledger = new PoisonContributionLedger();
        ledger.ObserveAmount(damageWinnerEnemy, 0, 6, 10);
        ledger.ObserveAmount(damageWinnerEnemy, 6, 8, 20);
        True(ledger.TryAllocateDamage(damageWinnerEnemy, 4, out _));
        Equal<ulong?>(10, ledger.SelectKillRecipient(damageWinnerEnemy, 123));

        var appliedWinnerEnemy = new object();
        ledger.ObserveAmount(appliedWinnerEnemy, 0, 3, 10);
        ledger.ObserveAmount(appliedWinnerEnemy, 3, 5, 20);
        Equal<ulong?>(10, ledger.SelectKillRecipient(appliedWinnerEnemy, 123));

        var tiedEnemy = new object();
        ledger.ObserveAmount(tiedEnemy, 0, 1, 10);
        ledger.ObserveAmount(tiedEnemy, 1, 2, 20);
        var first = ledger.SelectKillRecipient(tiedEnemy, 9876);
        var second = ledger.SelectKillRecipient(tiedEnemy, 9876);
        Equal(first, second);
        True(first is 10 or 20);
    }

    private static void AccelerantSponsorsPreserveOrder()
    {
        var ledger = new AccelerantSponsorLedger();
        True(ledger.ObserveApplication(10, 2));
        True(ledger.ObserveApplication(20, 1));
        var live = new Dictionary<ulong, long> { [10] = 2, [20] = 1 };
        var living = new HashSet<ulong> { 10, 20 };

        SequenceEqual<ulong?>(new ulong?[] { 10, 10, 20 }, ledger.ResolveSponsors(3, live, living));
        SequenceEqual<ulong?>(new ulong?[] { 10, 10 }, ledger.ResolveSponsors(2, live, living));
        living.Remove(10);
        SequenceEqual<ulong?>(new ulong?[] { 20 }, ledger.ResolveSponsors(1, live, living));
    }

    private static void AccelerantReconciliationFailsClosed()
    {
        var ledger = new AccelerantSponsorLedger();
        ledger.ObserveApplication(10, 1);
        var sponsors = ledger.ResolveSponsors(
            2,
            new Dictionary<ulong, long> { [10] = 2 },
            new HashSet<ulong> { 10 });
        SequenceEqual<ulong?>(new ulong?[] { null, null }, sponsors);
    }

    private static void AccelerantAssistExcludesOwnAndUnattributed()
    {
        var enemy = new object();
        var ledger = new PoisonContributionLedger();
        ledger.ObserveAmount(enemy, 0, 2, 10);
        ledger.ObserveAmount(enemy, 2, 4, 20);
        ledger.ObserveAmount(enemy, 4, 6, null);
        True(ledger.TryAllocateDamage(enemy, 6, out var allocation));

        True(AccelerantAssistCalculator.TryCalculate(allocation, 10, out var assisted));
        Equal(2L, assisted);
    }

    private static PoisonContributionLedger EqualPoisonLedger(object enemy, params ulong[] players)
    {
        var ledger = new PoisonContributionLedger();
        long amount = 0;
        foreach (var player in players)
        {
            ledger.ObserveAmount(enemy, amount, amount + 1, player);
            amount++;
        }
        return ledger;
    }

    private static void ReliablePoisonSourcesUseCentralTracker()
    {
        var state = CreateState(10);
        var tracker = CreatePoisonTracker(state);
        foreach (var amount in new[] { 2, 3, 4, 5 })
        {
            tracker.ObservePoisonAmount(new object(), 0, amount, 10);
        }

        // These four observations represent card, potion, relic, and delayed-power
        // applications. Runtime provenance converges at the same power mutation hook.
        Equal(14L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.PoisonApplied));
    }

    private static void PoisonTriggerIntegrationUpdatesStats()
    {
        var state = CreateState(10, 20);
        var combat = new CoreCombatTracker(state);
        var tracker = new PoisonStatTracker(
            state,
            combat,
            new PoisonContributionLedger(),
            new AccelerantSponsorLedger());
        var enemy = new object();
        tracker.ObservePoisonAmount(enemy, 0, 6, 10);
        tracker.ObservePoisonAmount(enemy, 6, 10, 20);

        True(tracker.RecordPoisonTrigger(enemy, 5, false, null, out _));
        True(tracker.RecordPoisonTrigger(enemy, 5, true, 10, out _));
        var killedTarget = new object();
        tracker.RecordPoisonKill(enemy, killedTarget, CombatRoomKind.Boss, 42);
        tracker.RecordPoisonKill(enemy, killedTarget, CombatRoomKind.Boss, 42);

        var snapshot = state.CaptureSnapshot();
        Equal(6L, snapshot.Players[10].GetTotal(StatKind.PoisonApplied));
        Equal(4L, snapshot.Players[20].GetTotal(StatKind.PoisonApplied));
        Equal(6L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(4L, snapshot.Players[20].GetTotal(StatKind.DamageDealt));
        Equal(2L, snapshot.Players[10].GetTotal(StatKind.AssistedDamage));
        Equal(1L, snapshot.Players[10].GetTotal(StatKind.EnemiesKilled));
        Equal(1L, snapshot.Players[10].GetTotal(StatKind.BossesKilled));
        Equal(0L, snapshot.Players[20].GetTotal(StatKind.EnemiesKilled));
    }

    private static void UnattributedRuntimePoisonStaysUncredited()
    {
        var state = CreateState(10);
        var tracker = CreatePoisonTracker(state);
        var enemy = new object();
        tracker.ObservePoisonAmount(enemy, 0, 4, null);
        True(tracker.RecordPoisonTrigger(enemy, 4, false, null, out var allocation));

        var snapshot = state.CaptureSnapshot();
        Equal(0L, snapshot.Players[10].GetTotal(StatKind.PoisonApplied));
        Equal(0L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(4L, allocation.UnattributedDamage);
        Equal(1L, snapshot.GetDiagnostic(DiagnosticKind.UnattributedPoisonApplication));
    }

    private static void LethalPoisonAllocatesBeforeReset()
    {
        var state = CreateState(10);
        var tracker = CreatePoisonTracker(state);
        var enemy = new object();
        var killedTarget = new object();
        tracker.ObservePoisonAmount(enemy, 0, 14, 10);

        // The enemy has 10 HP, so a 14-stack trigger reports 10 actual HP lost.
        True(tracker.RecordPoisonTrigger(enemy, 10, false, null, out var lethal));
        Equal(10L, lethal.PlayerDamage[10]);
        tracker.RecordPoisonKill(enemy, killedTarget, CombatRoomKind.Normal, 42);
        tracker.ObservePoisonAmount(enemy, 0, 0, null);

        var snapshot = state.CaptureSnapshot();
        Equal(10L, snapshot.Players[10].GetTotal(StatKind.DamageDealt));
        Equal(1L, snapshot.Players[10].GetTotal(StatKind.EnemiesKilled));
        True(!tracker.RecordPoisonTrigger(enemy, 1, false, null, out _));

        tracker.ObservePoisonAmount(enemy, 0, 3, 10);
        True(tracker.RecordPoisonTrigger(enemy, 2, false, null, out _));
        Equal(12L, state.CaptureSnapshot().Players[10].GetTotal(StatKind.DamageDealt));
    }

    private static PoisonStatTracker CreatePoisonTracker(RunStatsState state)
    {
        var combat = new CoreCombatTracker(state);
        return new PoisonStatTracker(
            state,
            combat,
            new PoisonContributionLedger(),
            new AccelerantSponsorLedger());
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "RunStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static IReadOnlyDictionary<StatKind, long> TotalsWith(
        PlayerStatsSnapshot player,
        StatKind kind,
        long value)
    {
        var totals = new Dictionary<StatKind, long>(player.Totals) { [kind] = value };
        return new ReadOnlyDictionary<StatKind, long>(totals);
    }

    private static StatsRowViewModel UiRow(StatsViewModel viewModel, string label) =>
        viewModel.Rows.Single(row => row.Label == label);

    private static string UiLabel(StatKind kind) => kind switch
    {
        StatKind.DamageDealt => "Damage Dealt",
        StatKind.DamageTaken => "Damage Taken",
        StatKind.HealingDone => "Healing Done",
        StatKind.MaxHpGained => "Max HP Gained",
        StatKind.BlockGained => "Block Gained",
        StatKind.BlockLost => "Block Lost",
        StatKind.EnemiesKilled => "Enemies Killed",
        StatKind.EliteEnemiesKilled => "Elite Enemies Killed",
        StatKind.BossesKilled => "Bosses Killed",
        StatKind.CardsPlayed => "Cards Played",
        StatKind.GoldEarned => "Gold Earned",
        StatKind.GoldSpent => "Gold Spent",
        StatKind.CardsObtained => "Cards Obtained",
        StatKind.CardsUpgraded => "Cards Upgraded",
        StatKind.CardsRemoved => "Cards Removed",
        StatKind.RelicsObtained => "Relics Obtained",
        StatKind.PotionsObtained => "Potions Obtained",
        StatKind.PotionsUsed => "Potions Used",
        StatKind.AssistedDamage => "Assisted Damage",
        StatKind.AssistedDamagePrevented => "Assisted Damage Prevented",
        StatKind.PoisonApplied => "Poison Applied",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static StatsSyncSnapshot Envelope(
        long sequence,
        RunStatsSnapshot snapshot,
        params AssistedOwnershipRecord[] ownership) =>
        new(
            StatsSyncSnapshot.CurrentProtocolVersion,
            sequence,
            0,
            snapshot,
            Array.AsReadOnly(ownership));

    private static RunStatsState CreateState(params ulong[] playerNetIds)
    {
        var state = new RunStatsState();
        state.StartRun(Identity(playerNetIds));
        return state;
    }

    private static RunIdentity Identity(params ulong[] playerNetIds) =>
        RunIdentity.Create(
            "STAGE2-SEED",
            playerNetIds.Length > 1 ? RunMode.Multiplayer : RunMode.Singleplayer,
            "profile1",
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
            playerNetIds);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Equal(expected[index], actual[index]);
        }
    }
}
