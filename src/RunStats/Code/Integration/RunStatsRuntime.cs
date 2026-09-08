using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using RunStats.Infrastructure;
using RunStats.Models;
using RunStats.Multiplayer;
using RunStats.Persistence;
using RunStats.Tracking;

namespace RunStats.Integration;

public static class RunStatsRuntime
{
    private static readonly RunStatsState State = new();
    private static readonly Dictionary<Creature, CreatureSubscription> CreatureSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Player, PlayerSubscription> PlayerSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Creature, int> MaxHpGainScopes =
        new(ReferenceEqualityComparer.Instance);
    private static readonly CoreCombatTracker CombatTracker = new(State);
    private static readonly RunProgressTracker ProgressTracker = new(State);
    private static readonly UniqueContributorLedger ContributorLedger = new();
    private static readonly AsyncLocal<DamageAssistScope?> CurrentDamageScope = new();
    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);
    private static readonly MethodInfo DamageCapMethod = GetModelMethod(nameof(AbstractModel.ModifyDamageCap));
    private static readonly MethodInfo HpLostBeforeMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostBeforeOsty));
    private static readonly MethodInfo HpLostBeforeLateMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostBeforeOstyLate));
    private static readonly MethodInfo HpLostAfterMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostAfterOsty));
    private static readonly MethodInfo HpLostAfterLateMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostAfterOstyLate));
    private static readonly MethodInfo RedirectMethod = GetModelMethod(nameof(AbstractModel.ModifyUnblockedDamageTarget));
    private static AccessTools.FieldRef<RunManager, long>? _startTimeRef;
    private static RunStatsPersistenceCoordinator? _persistence;
    private static LoadedRunCheckpoint? _loadedRunCheckpoint;
    private static RunState? _activeRunState;
    private static RunStatsSnapshot? _lastCompletedSnapshot;
    private static bool _initialized;

    public static RunStatsSnapshot Snapshot => State.CaptureSnapshot();

    public static RunStatsSnapshot DisplaySnapshot
    {
        get
        {
            var current = State.CaptureSnapshot();
            return current.Lifecycle == RunLifecycle.Empty
                ? _lastCompletedSnapshot ?? current
                : current;
        }
    }

    public static event Action? DisplaySnapshotChanged;

    internal static CardModel? FindActiveDeckCard(ulong playerNetId, string serializedCardId)
    {
        try
        {
            var cardId = ModelId.Deserialize(serializedCardId);
            return _activeRunState?
                .GetPlayer(playerNetId)?
                .Deck.Cards
                .FirstOrDefault(card => card.Id == cardId);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static CardModel? FindActiveDeckCard(string serializedCardId)
    {
        try
        {
            var cardId = ModelId.Deserialize(serializedCardId);
            return _activeRunState?.Players
                .SelectMany(player => player.Deck.Cards)
                .FirstOrDefault(card => card.Id == cardId);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static string? GetPlayerDisplayName(ulong playerNetId)
    {
        try
        {
            var netService = RunManager.Instance.NetService;
            var platform = netService.Type == NetGameType.Singleplayer
                ? PlatformUtil.PrimaryPlatform
                : netService.Platform;
            var platformPlayerId = netService.Type == NetGameType.Singleplayer
                ? PlatformUtil.GetLocalPlayerId(platform)
                : playerNetId;
            var name = PlatformUtil.GetPlayerNameRaw(platform, platformPlayerId).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        RunManager.Instance.RunStarted += OnRunStarted;
        SaveManager.Instance.Saved += OnVanillaSaved;
        _persistence = new RunStatsPersistenceCoordinator(
            State,
            CreateSidecarStore,
            (message, exception) => RunStatsLog.Error(message, exception),
            captureAssistedOwnership: CaptureAssistedOwnership,
            applyAssistedOwnership: ApplyAssistedOwnership);
        _initialized = true;
        RunStatsLog.Debug("Core runtime subscribed to run lifecycle events.");
    }

    public static void OnDamageGiven(
        ICombatState combatState,
        Creature? dealer,
        DamageResult result,
        Creature target)
    {
        Safely(nameof(OnDamageGiven), () =>
        {
            RecordAssistedDamage(target, result);
            var player = ResolvePlayer(dealer);
            ulong? sourcePlayerNetId = player?.NetId;
            if (dealer is not null && player is null)
            {
                return;
            }

            var roomKind = combatState.RunState.CurrentRoom?.RoomType switch
            {
                RoomType.Elite => CombatRoomKind.Elite,
                RoomType.Boss => CombatRoomKind.Boss,
                _ => CombatRoomKind.Normal
            };
            CombatTracker.RecordDamageGiven(
                sourcePlayerNetId,
                target.IsEnemy,
                result.UnblockedDamage,
                result.WasTargetKilled,
                target,
                roomKind);
        });
    }

    internal static DamageAssistScope BeginDamageAssistScope()
    {
        var scope = new DamageAssistScope(CurrentDamageScope.Value);
        CurrentDamageScope.Value = scope;
        return scope;
    }

    internal static void DetachDamageAssistScope(DamageAssistScope scope)
    {
        if (ReferenceEquals(CurrentDamageScope.Value, scope))
        {
            CurrentDamageScope.Value = scope.Parent;
        }
    }

    public static void OnDamageModified(
        IRunState runState,
        ICombatState? combatState,
        Creature? target,
        Creature? dealer,
        ValueProp props,
        CardModel? cardSource,
        ModifyDamageHookType hookType,
        IEnumerable<AbstractModel> modifiers,
        decimal modifiedDamage)
    {
        Safely(nameof(OnDamageModified), () =>
        {
            var scope = CurrentDamageScope.Value;
            if (scope is null || combatState is null || target is null ||
                hookType != ModifyDamageHookType.All)
            {
                return;
            }

            var modifierList = modifiers.ToList();
            if (modifierList.Any(model => Overrides(model, DamageCapMethod)))
            {
                return;
            }

            if (target.IsEnemy && ResolvePlayer(dealer) is Player attacker)
            {
                var powers = modifierList.OfType<VulnerablePower>().ToList();
                TryObserveAssistedDamage(
                    scope,
                    runState,
                    combatState,
                    target,
                    dealer,
                    props,
                    cardSource,
                    modifiedDamage,
                    attacker.NetId,
                    powers);
                return;
            }

            if (target.Player is not null && dealer?.IsEnemy == true)
            {
                var powers = modifierList.OfType<WeakPower>().ToList();
                TryObservePreventedDamage(
                    scope,
                    target,
                    dealer,
                    props,
                    cardSource,
                    modifiedDamage,
                    target.Player.NetId,
                    powers);
            }
        });
    }

    public static void OnPowerContribution(PowerModel power, Creature? applier)
    {
        Safely(nameof(OnPowerContribution), () =>
        {
            if (power is VulnerablePower or WeakPower)
            {
                ContributorLedger.Resolve(power, ResolvePlayer(power.Applier)?.NetId);
                ContributorLedger.ObserveContribution(power, ResolvePlayer(applier)?.NetId);
            }
        });
    }

    public static void OnCardPlayed(CardPlay cardPlay)
    {
        Safely(nameof(OnCardPlayed), () =>
            ProgressTracker.RecordCardPlayed(
                cardPlay.Card.Owner.NetId,
                cardPlay.Card.Id.ToString()));
    }

    public static void OnCardObtained(CardModel card)
    {
        Safely(nameof(OnCardObtained), () =>
            ProgressTracker.RecordCount(card.Owner.NetId, StatKind.CardsObtained));
    }

    public static void OnCardUpgraded(CardModel card, int levelsGained)
    {
        Safely(nameof(OnCardUpgraded), () =>
            ProgressTracker.RecordCount(
                card.Owner.NetId,
                StatKind.CardsUpgraded,
                levelsGained));
    }

    public static void OnCardRemoved(CardModel card)
    {
        Safely(nameof(OnCardRemoved), () =>
            ProgressTracker.RecordCount(card.Owner.NetId, StatKind.CardsRemoved));
    }

    public static void OnGoldGained(
        Player player,
        int previousGold,
        int currentGold,
        bool wasStolenBack)
    {
        Safely(nameof(OnGoldGained), () =>
            ProgressTracker.RecordGoldGained(
                player.NetId,
                previousGold,
                currentGold,
                wasStolenBack));
    }

    public static void OnGoldLost(
        Player player,
        int previousGold,
        int currentGold,
        GoldLossType lossType)
    {
        Safely(nameof(OnGoldLost), () =>
            ProgressTracker.RecordGoldLost(
                player.NetId,
                previousGold,
                currentGold,
                lossType == GoldLossType.Spent));
    }

    public static void OnRelicObtained(Player player, RelicModel relic)
    {
        Safely(nameof(OnRelicObtained), () =>
            ProgressTracker.RecordCount(player.NetId, StatKind.RelicsObtained));
    }

    public static void OnPotionObtained(Player player, PotionModel potion)
    {
        Safely(nameof(OnPotionObtained), () =>
            ProgressTracker.RecordCount(player.NetId, StatKind.PotionsObtained));
    }

    public static void OnPotionUsed(PotionModel potion)
    {
        Safely(nameof(OnPotionUsed), () =>
            ProgressTracker.RecordCount(potion.Owner.NetId, StatKind.PotionsUsed));
    }

    public static void OnDamageReceived(Creature target, DamageResult result)
    {
        Safely(nameof(OnDamageReceived), () =>
        {
            if (target.Player is not null)
            {
                CombatTracker.RecordDamageTaken(target.Player.NetId, result.UnblockedDamage);
            }
        });
    }

    public static void OnBlockChanged(Creature creature, int previous, int current)
    {
        Safely(nameof(OnBlockChanged), () =>
        {
            if (creature.Player is null)
            {
                return;
            }

            CombatTracker.RecordBlockChanged(creature.Player.NetId, previous, current);
        });
    }

    public static void OnCurrentHpChanged(Creature creature, int previous, int current)
    {
        Safely(nameof(OnCurrentHpChanged), () =>
        {
            if (creature.Player is null)
            {
                return;
            }

            CombatTracker.RecordCurrentHpChanged(
                creature.Player.NetId,
                previous,
                current,
                IsInsideMaxHpGain(creature));
        });
    }

    public static void OnMaxHpChanged(Creature creature, int previous, int current)
    {
        Safely(nameof(OnMaxHpChanged), () =>
        {
            if (creature.Player is null)
            {
                return;
            }

            CombatTracker.RecordMaxHpChanged(creature.Player.NetId, previous, current);
        });
    }

    public static void BeginMaxHpGain(Creature creature)
    {
        Safely(nameof(BeginMaxHpGain), () =>
        {
            MaxHpGainScopes.TryGetValue(creature, out var depth);
            MaxHpGainScopes[creature] = checked(depth + 1);
        });
    }

    public static void EndMaxHpGain(Creature creature)
    {
        Safely(nameof(EndMaxHpGain), () =>
        {
            if (!MaxHpGainScopes.TryGetValue(creature, out var depth) || depth <= 1)
            {
                MaxHpGainScopes.Remove(creature);
                return;
            }

            MaxHpGainScopes[creature] = depth - 1;
        });
    }

    public static void OnRunEnded(bool isVictory) => Safely(nameof(OnRunEnded), () =>
    {
        if (State.TryEndRun())
        {
            var snapshot = State.CaptureSnapshot();
            _lastCompletedSnapshot = snapshot;
            var reason = RunManager.Instance.IsAbandoned
                ? "abandoned"
                : isVictory ? "victory" : "defeat";
            _persistence?.ArchiveEnded(snapshot, reason);
            DisplaySnapshotChanged?.Invoke();
        }
    });

    public static void OnRunCleanup()
    {
        Safely(nameof(OnRunCleanup), () =>
        {
            _persistence?.ResetRun();
            DetachCreatureEvents();
            DetachPlayerEvents();
            CombatTracker.ResetForRun();
            ContributorLedger.Clear();
            MaxHpGainScopes.Clear();
            State.Clear();
            _activeRunState = null;
            DisplaySnapshotChanged?.Invoke();
            RunStatsLog.Debug("Cleared core runtime state.");
        });
    }

    private static void OnRunStarted(RunState runState)
    {
        Safely(nameof(OnRunStarted), () =>
        {
            DetachCreatureEvents();
            DetachPlayerEvents();
            CombatTracker.ResetForRun();
            ContributorLedger.Clear();
            MaxHpGainScopes.Clear();
            State.Clear();
            _activeRunState = null;
            _lastCompletedSnapshot = null;

            if (RunManager.Instance.NetService.Type == NetGameType.Replay)
            {
                DisplaySnapshotChanged?.Invoke();
                RunStatsLog.Debug("Replay detected; runtime tracking remains inactive.");
                return;
            }

            _startTimeRef ??= AccessTools.FieldRefAccess<RunManager, long>("_startTime");
            var identity = RunIdentity.Create(
                runState.Rng.StringSeed,
                RunManager.Instance.NetService.Type == NetGameType.Singleplayer
                    ? RunMode.Singleplayer
                    : RunMode.Multiplayer,
                "profile" + SaveManager.Instance.CurrentProfileId.ToString(CultureInfo.InvariantCulture),
                DateTimeOffset.FromUnixTimeSeconds(_startTimeRef(RunManager.Instance)),
                runState.Players.Select(player => player.NetId));

            State.StartRun(identity);
            _activeRunState = runState;
            RestorePersistedState(runState, identity);
            try
            {
                foreach (var player in runState.Players)
                {
                    CreatureSubscriptions.Add(player.Creature, new CreatureSubscription(player.Creature));
                    PlayerSubscriptions.Add(player, new PlayerSubscription(player));
                }
            }
            catch
            {
                DetachCreatureEvents();
                DetachPlayerEvents();
                State.Clear();
                throw;
            }

            RunStatsLog.Info(
                $"Tracking run {identity.Seed} in {identity.Mode} mode for " +
                $"{identity.PlayerNetIds.Count} player(s).");
            DisplaySnapshotChanged?.Invoke();
        });
    }

    public static void OnVanillaSavePreparing(long saveTime, RunMode mode) =>
        Safely(nameof(OnVanillaSavePreparing), () =>
        {
            _persistence?.PrepareVanillaSave(saveTime, mode);
        });

    public static void OnVanillaRunLoaded(SerializableRun save, RunMode mode) =>
        Safely(nameof(OnVanillaRunLoaded), () =>
        {
            _loadedRunCheckpoint = new LoadedRunCheckpoint(save.StartTime, save.SaveTime, mode);
        });

    public static void OnVanillaRunDeleted(RunMode mode) =>
        Safely(nameof(OnVanillaRunDeleted), () =>
        {
            _persistence?.ArchiveDeleted(mode, "abandoned");
        });

    private static Player? ResolvePlayer(Creature? creature) => creature?.Player ?? creature?.PetOwner;

    private static void OnVanillaSaved() =>
        Safely(nameof(OnVanillaSaved), () => _persistence?.ConfirmVanillaSave());

    private static RunStatsSidecarStore CreateSidecarStore()
    {
        var scopedPath = SaveManager.Instance.GetProfileScopedPath(SidecarSnapshotCodec.ModId);
        return new RunStatsSidecarStore(ProjectSettings.GlobalizePath(scopedPath));
    }

    private static void RestorePersistedState(RunState runState, RunIdentity identity)
    {
        if (!VanillaHistoryReader.TryCapture(runState, out var baseline))
        {
            RunStatsLog.Warn("Vanilla run history was invalid; history-backed recovery was skipped.");
            return;
        }

        LoadedRunCheckpoint? checkpoint = _loadedRunCheckpoint;
        _loadedRunCheckpoint = null;
        if (checkpoint is null || checkpoint.Mode != identity.Mode ||
            checkpoint.StartTime != identity.StartTimeUtc.ToUnixTimeSeconds())
        {
            if (_persistence?.MergeBaseline(baseline) == false)
            {
                RunStatsLog.Warn("Vanilla history totals could not be merged into the new run state.");
            }
            return;
        }

        var result = _persistence?.Restore(identity, checkpoint.SaveTime, baseline)
            ?? SidecarLoadResult.FileNotFound;
        if (result == SidecarLoadResult.Loaded)
        {
            RunStatsLog.Info($"Restored RunStats sidecar at vanilla checkpoint {checkpoint.SaveTime}.");
        }
        else if (result == SidecarLoadResult.FileNotFound)
        {
            RunStatsLog.Info("No matching RunStats sidecar was found; restored available vanilla history totals.");
        }
        else
        {
            RunStatsLog.Warn(
                $"RunStats sidecar restore was skipped ({result}); restored available vanilla history totals.");
        }
    }

    private static IReadOnlyList<AssistedOwnershipRecord> CaptureAssistedOwnership()
    {
        var records = new List<AssistedOwnershipRecord>();
        var combatState = GetLiveCombatState();
        if (combatState is null)
        {
            return records.AsReadOnly();
        }

        foreach (var creature in combatState.Creatures)
        {
            if (!creature.CombatId.HasValue)
            {
                continue;
            }

            foreach (var power in creature.Powers)
            {
                AssistedPowerKind? kind = power switch
                {
                    VulnerablePower => AssistedPowerKind.Vulnerable,
                    WeakPower => AssistedPowerKind.Weak,
                    _ => null
                };
                if (!kind.HasValue)
                {
                    continue;
                }

                var contributor = ContributorLedger.Resolve(
                    power,
                    ResolvePlayer(power.Applier)?.NetId);
                records.Add(new AssistedOwnershipRecord(
                    creature.CombatId.Value,
                    kind.Value,
                    contributor.Resolution,
                    contributor.PlayerNetId));
            }
        }

        return records.AsReadOnly();
    }

    private static void ApplyAssistedOwnership(IReadOnlyList<AssistedOwnershipRecord> records)
    {
        ContributorLedger.Clear();
        var combatState = GetLiveCombatState();
        if (combatState is null)
        {
            return;
        }

        foreach (var creature in combatState.Creatures)
        {
            foreach (var power in creature.Powers)
            {
                if (power is VulnerablePower or WeakPower)
                {
                    ContributorLedger.SetResolution(power, ContributorResult.Unsupported);
                }
            }
        }

        foreach (var record in records)
        {
            var creature = combatState.GetCreature(record.OwnerCombatId);
            var power = creature?.Powers.FirstOrDefault(candidate =>
                record.PowerKind == AssistedPowerKind.Vulnerable
                    ? candidate is VulnerablePower
                    : candidate is WeakPower);
            if (power is not null)
            {
                ContributorLedger.SetResolution(
                    power,
                    new ContributorResult(record.Resolution, record.ContributorNetId));
            }
        }
    }

    private static ICombatState? GetLiveCombatState()
    {
        var combatState = _activeRunState?.Players
            .Select(player => player.Creature.CombatState)
            .FirstOrDefault(state => state?.IsLiveCombat() == true);
        return combatState;
    }

    private static void TryObserveAssistedDamage(
        DamageAssistScope scope,
        IRunState runState,
        ICombatState combatState,
        Creature target,
        Creature? dealer,
        ValueProp props,
        CardModel? cardSource,
        decimal modifiedDamage,
        ulong attackerNetId,
        IReadOnlyList<VulnerablePower> powers)
    {
        if (powers.Count != 1 || HasHpLossOrRedirectionOverride(runState, combatState))
        {
            if (powers.Count > 1)
            {
                State.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamage);
            }
            return;
        }

        var power = powers[0];
        var contributor = ContributorLedger.Resolve(power, ResolvePlayer(power.Applier)?.NetId);
        if (!TryResolveTeammateContributor(
                contributor,
                attackerNetId,
                DiagnosticKind.AmbiguousAssistedDamage,
                out var contributorNetId))
        {
            return;
        }

        var multiplier = power.ModifyDamageMultiplicative(target, modifiedDamage, props, dealer, cardSource);
        if (multiplier > 1m)
        {
            scope.Observations[target] = new DamageAssistObservation(
                StatKind.AssistedDamage,
                contributorNetId,
                modifiedDamage,
                multiplier);
        }
    }

    private static void TryObservePreventedDamage(
        DamageAssistScope scope,
        Creature target,
        Creature dealer,
        ValueProp props,
        CardModel? cardSource,
        decimal modifiedDamage,
        ulong protectedPlayerNetId,
        IReadOnlyList<WeakPower> powers)
    {
        if (powers.Count != 1)
        {
            if (powers.Count > 1)
            {
                State.TryRecordDiagnostic(DiagnosticKind.AmbiguousAssistedDamagePrevented);
            }
            return;
        }

        var power = powers[0];
        var contributor = ContributorLedger.Resolve(power, ResolvePlayer(power.Applier)?.NetId);
        if (!TryResolveTeammateContributor(
                contributor,
                protectedPlayerNetId,
                DiagnosticKind.AmbiguousAssistedDamagePrevented,
                out var contributorNetId))
        {
            return;
        }

        var multiplier = power.ModifyDamageMultiplicative(target, modifiedDamage, props, dealer, cardSource);
        if (multiplier > 0m && multiplier < 1m)
        {
            scope.Observations[target] = new DamageAssistObservation(
                StatKind.AssistedDamagePrevented,
                contributorNetId,
                modifiedDamage,
                multiplier);
        }
    }

    private static bool TryResolveTeammateContributor(
        ContributorResult contributor,
        ulong beneficiaryNetId,
        DiagnosticKind ambiguousDiagnostic,
        out ulong contributorNetId)
    {
        contributorNetId = 0;
        if (contributor.Resolution == ContributorResolution.Ambiguous)
        {
            State.TryRecordDiagnostic(ambiguousDiagnostic);
            return false;
        }

        if (!AssistedAttribution.TryGetTeammate(
                contributor,
                beneficiaryNetId,
                out contributorNetId))
        {
            return false;
        }
        return true;
    }

    private static void RecordAssistedDamage(Creature target, DamageResult result)
    {
        var scope = CurrentDamageScope.Value;
        if (scope is null || !scope.Observations.Remove(target, out var observation))
        {
            return;
        }

        long amount;
        if (observation.Stat == StatKind.AssistedDamage)
        {
            var preHitBlock = checked(target.Block + result.BlockedDamage);
            var preHitHp = checked(target.CurrentHp + result.UnblockedDamage);
            amount = AssistedDamageCalculator.DamageAdded(
                observation.ActualModifiedDamage,
                observation.ContributorMultiplier,
                preHitBlock,
                preHitHp,
                result.UnblockedDamage);
        }
        else
        {
            amount = AssistedDamageCalculator.DamagePrevented(
                observation.ActualModifiedDamage,
                observation.ContributorMultiplier);
        }

        if (amount > 0)
        {
            State.TryApply(StatMutation.Add(
                observation.ContributorNetId,
                observation.Stat,
                amount));
        }
    }

    private static bool HasHpLossOrRedirectionOverride(IRunState runState, ICombatState combatState) =>
        runState.IterateHookListeners(combatState).Any(model =>
            Overrides(model, HpLostBeforeMethod) ||
            Overrides(model, HpLostBeforeLateMethod) ||
            Overrides(model, HpLostAfterMethod) ||
            Overrides(model, HpLostAfterLateMethod) ||
            Overrides(model, RedirectMethod));

    private static bool Overrides(AbstractModel model, MethodInfo baseMethod)
    {
        var parameters = baseMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var implementation = model.GetType().GetMethod(
            baseMethod.Name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameters,
            modifiers: null);
        return implementation?.DeclaringType != baseMethod.DeclaringType;
    }

    private static MethodInfo GetModelMethod(string name) =>
        typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == name);

    private static bool IsInsideMaxHpGain(Creature creature) =>
        MaxHpGainScopes.TryGetValue(creature, out var depth) && depth > 0;

    private static void DetachCreatureEvents()
    {
        foreach (var subscription in CreatureSubscriptions.Values)
        {
            subscription.Dispose();
        }

        CreatureSubscriptions.Clear();
    }

    private static void DetachPlayerEvents()
    {
        foreach (var subscription in PlayerSubscriptions.Values)
        {
            subscription.Dispose();
        }

        PlayerSubscriptions.Clear();
    }

    private static void Safely(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            if (LoggedFailures.Add(operation))
            {
                RunStatsLog.Error($"{operation} failed; the affected event was skipped.", exception);
            }
        }
    }

    private sealed record LoadedRunCheckpoint(long StartTime, long SaveTime, RunMode Mode);
}
