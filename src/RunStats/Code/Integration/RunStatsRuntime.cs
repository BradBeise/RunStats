using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
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
    private static readonly AssistedContributionTracker AssistedContributionTracker = new();
    private static readonly AssistedStatTracker AssistedStats = new(
        State,
        AssistedContributionTracker);
    private static readonly StrengthImpactLedger StrengthImpactLedger = new();
    private static readonly StrengthContributionTracker StrengthTracker = new(
        StrengthImpactLedger);
    private static readonly StrengthStatTracker StrengthStats = new(State);
    private static readonly PoisonContributionLedger PoisonContributionLedger = new();
    private static readonly AccelerantSponsorLedger AccelerantSponsorLedger = new();
    private static readonly PoisonStatTracker PoisonTracker = new(
        State,
        CombatTracker,
        PoisonContributionLedger,
        AccelerantSponsorLedger);
    private static readonly DoomStatTracker DoomTracker = new(State, CombatTracker);
    private static readonly AsyncLocal<ulong?> CurrentDoomCardContributor = new();
    private static readonly AsyncLocal<DamageAssistScope?> CurrentDamageScope = new();
    private static readonly AsyncLocal<PoisonSequenceScope?> CurrentPoisonSequence = new();
    private static readonly AsyncLocal<PoisonDamageCommandScope?> CurrentPoisonDamageCommand = new();
    private static readonly AsyncLocal<TemporaryStrengthMutationScope?> CurrentTemporaryStrengthMutation = new();
    private static readonly AsyncLocal<OwnedStrengthSourceScope?> CurrentOwnedStrengthSource = new();
    private static readonly HashSet<string> LoggedFailures = new(StringComparer.Ordinal);
    private static readonly MethodInfo DamageAdditiveMethod =
        GetModelMethod(nameof(AbstractModel.ModifyDamageAdditive));
    private static readonly MethodInfo DamageCapMethod = GetModelMethod(nameof(AbstractModel.ModifyDamageCap));
    private static readonly MethodInfo DamageMultiplicativeMethod =
        GetModelMethod(nameof(AbstractModel.ModifyDamageMultiplicative));
    private static readonly MethodInfo HpLostBeforeMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostBeforeOsty));
    private static readonly MethodInfo HpLostBeforeLateMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostBeforeOstyLate));
    private static readonly MethodInfo HpLostAfterMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostAfterOsty));
    private static readonly MethodInfo HpLostAfterLateMethod = GetModelMethod(nameof(AbstractModel.ModifyHpLostAfterOstyLate));
    private static readonly MethodInfo RedirectMethod = GetModelMethod(nameof(AbstractModel.ModifyUnblockedDamageTarget));
    private static AccessTools.FieldRef<RunManager, long>? _startTimeRef;
    private static RunStatsPersistenceCoordinator? _persistence;
    private static LoadedRunCheckpoint? _loadedRunCheckpoint;
    private static RunState? _activeRunState;
    private static ActionExecutor? _subscribedActionExecutor;
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
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        SaveManager.Instance.Saved += OnVanillaSaved;
        _persistence = new RunStatsPersistenceCoordinator(
            State,
            CreateSidecarStore,
            (message, exception) => RunStatsLog.Error(message, exception),
            onLegacyAssistedOwnership: DiscardLegacyAssistedOwnership);
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
            if (CurrentPoisonDamageCommand.Value?.IsPoison == true)
            {
                return;
            }

            RecordAssistedDamage(target, result);
            var player = ResolvePlayer(dealer);
            ulong? sourcePlayerNetId = player?.NetId;
            if (target.IsEnemy)
            {
                DoomTracker.RecordDirectDamage(target, sourcePlayerNetId, result.UnblockedDamage);
            }
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

    internal static TemporaryStrengthMutationScope BeginTemporaryStrengthMutation(
        TemporaryStrengthPower power,
        bool isRestoration)
    {
        var scope = new TemporaryStrengthMutationScope(
            CurrentTemporaryStrengthMutation.Value,
            power,
            isRestoration);
        CurrentTemporaryStrengthMutation.Value = scope;
        return scope;
    }

    internal static async Task CompleteTemporaryStrengthMutationAsync(
        Task original,
        TemporaryStrengthMutationScope scope)
    {
        try
        {
            await original;
        }
        finally
        {
            if (scope.IsRestoration)
            {
                StrengthTracker.ForgetSource(scope.Power);
            }

            if (ReferenceEquals(CurrentTemporaryStrengthMutation.Value, scope))
            {
                CurrentTemporaryStrengthMutation.Value = scope.Parent;
            }
        }
    }

    internal static OwnedStrengthSourceScope BeginOwnedStrengthSource(
        object source,
        ulong contributorNetId)
    {
        var scope = new OwnedStrengthSourceScope(
            CurrentOwnedStrengthSource.Value,
            source,
            contributorNetId);
        CurrentOwnedStrengthSource.Value = scope;
        return scope;
    }

    internal static async Task CompleteOwnedStrengthSourceAsync(
        Task original,
        OwnedStrengthSourceScope scope)
    {
        try
        {
            await original;
        }
        finally
        {
            if (ReferenceEquals(CurrentOwnedStrengthSource.Value, scope))
            {
                CurrentOwnedStrengthSource.Value = scope.Parent;
            }
        }
    }

    internal static async Task<IEnumerable<DamageResult>> CompleteDamageAssistScopeAsync(
        Task<IEnumerable<DamageResult>> original,
        DamageAssistScope scope)
    {
        try
        {
            var results = await original;
            Safely(nameof(CompleteDamageAssistScopeAsync), () =>
                RecordAggregatedWeakPrevention(scope));
            return results;
        }
        finally
        {
            Safely(nameof(CompleteDamageAssistScopeAsync), () =>
            {
                foreach (var owner in scope.DeferredVulnerableResets)
                {
                    AssistedContributionTracker.ResetCycle(
                        AssistedEffectKind.Vulnerable,
                        owner);
                }

                foreach (var owner in scope.DeferredWeakResets)
                {
                    AssistedContributionTracker.ResetCycle(
                        AssistedEffectKind.Weak,
                        owner);
                }
            });
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
        decimal modifiedDamage,
        decimal unmodifiedDamage)
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
                TryObserveStrengthAssistedDamage(
                    scope,
                    runState,
                    combatState,
                    target,
                    dealer!,
                    props,
                    cardSource,
                    modifiedDamage,
                    modifierList,
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
                TryObserveStrengthPreventedDamage(
                    scope,
                    runState,
                    combatState,
                    target,
                    dealer,
                    props,
                    cardSource,
                    modifiedDamage,
                    target.Player.NetId,
                    modifierList,
                    powers,
                    unmodifiedDamage);
            }
        });
    }

    public static void OnPowerAmountChanged(
        PowerModel power,
        Creature? applier,
        int previousAmount,
        int currentAmount)
    {
        Safely(nameof(OnPowerAmountChanged), () =>
        {
            if (!IsRunActive())
            {
                return;
            }

            if (power is StrengthPower strengthPower)
            {
                ObserveStrengthAmount(strengthPower, applier, previousAmount, currentAmount);
                return;
            }

            ObserveStrengthSourceOwner(power, applier, previousAmount, currentAmount);

            if (power is PoisonPower && power.Owner.IsEnemy)
            {
                PoisonTracker.ObservePoisonAmount(
                    power.Owner,
                    previousAmount,
                    currentAmount,
                    ResolvePlayer(applier)?.NetId);
                return;
            }

            if (power is DoomPower && power.Owner.IsEnemy)
            {
                DoomTracker.ObserveAmount(
                    power.Owner,
                    previousAmount,
                    currentAmount,
                    CurrentDoomCardContributor.Value ?? ResolvePlayer(applier)?.NetId);
                return;
            }

            if (power.Owner.IsEnemy && TryGetAssistedEffectKind(power, out var assistedKind))
            {
                if (currentAmount == 0 && TryDeferAssistedCycleReset(power.Owner, assistedKind))
                {
                    return;
                }

                var observation = AssistedContributionTracker.ObserveAmount(
                    assistedKind,
                    power.Owner,
                    previousAmount,
                    currentAmount,
                    ResolvePlayer(applier)?.NetId);
                if (!observation.Accepted)
                {
                    State.TryRecordDiagnostic(
                        assistedKind == AssistedEffectKind.Vulnerable
                            ? DiagnosticKind.AmbiguousAssistedDamage
                            : DiagnosticKind.AmbiguousAssistedDamagePrevented);
                }
                return;
            }

            if (power is AccelerantPower && currentAmount > previousAmount)
            {
                var applierPlayer = ResolvePlayer(applier);
                var ownerPlayer = ResolvePlayer(power.Owner);
                var contributor = applierPlayer is not null && ownerPlayer is not null &&
                                  applierPlayer.NetId == ownerPlayer.NetId
                    ? applierPlayer.NetId
                    : (ulong?)null;
                PoisonTracker.ObserveAccelerantApplication(
                    contributor,
                    checked(currentAmount - previousAmount));
            }
        });
    }

    public static void OnPowerRemoved(PowerModel power)
    {
        Safely(nameof(OnPowerRemoved), () =>
        {
            if (IsRunActive() && power is StrengthPower && power.Amount != 0)
            {
                StrengthTracker.ObserveAmount(
                    CurrentPlayerAction(),
                    power.Owner,
                    power.Owner.Player?.NetId,
                    power.Amount,
                    0,
                    null,
                    null,
                    -1,
                    StrengthImpactExpiryKind.UntilStrengthReset,
                    null);
            }

            if (IsRunActive() && power is TemporaryStrengthPower temporaryStrength)
            {
                var temporaryScope = CurrentTemporaryStrengthMutation.Value;
                if (temporaryScope is null ||
                    !temporaryScope.IsRestoration ||
                    !ReferenceEquals(temporaryScope.Power, temporaryStrength))
                {
                    StrengthTracker.ExpireSource(
                        CurrentPlayerAction(),
                        power.Owner,
                        temporaryStrength);
                }
            }
            else if (power is not StrengthPower)
            {
                StrengthTracker.ForgetSource(power);
            }

            if (IsRunActive() && power is PoisonPower && power.Owner.IsEnemy)
            {
                var damageScope = CurrentPoisonDamageCommand.Value;
                if (damageScope is { IsPoison: true } &&
                    ReferenceEquals(damageScope.PoisonOwner, power.Owner))
                {
                    // CreatureCmd.Damage removes a killed enemy's powers before it
                    // returns the lethal DamageResult. Keep this cycle alive until
                    // the wrapper allocates that final HP loss and attributes the kill.
                    damageScope.DeferPoisonCycleReset();
                    return;
                }

                PoisonTracker.ObservePoisonAmount(power.Owner, power.Amount, 0, null);
            }
            else if (IsRunActive() && power is DoomPower && power.Owner.IsEnemy)
            {
                DoomTracker.Remove(power.Owner);
            }

            if (IsRunActive() && power.Owner.IsEnemy &&
                TryGetAssistedEffectKind(power, out var assistedKind))
            {
                if (TryDeferAssistedCycleReset(power.Owner, assistedKind))
                {
                    return;
                }

                AssistedContributionTracker.ResetCycle(assistedKind, power.Owner);
            }
        });
    }

    internal static ulong? BeginDoomCardApplication(PowerModel power, CardModel? cardSource)
    {
        var previous = CurrentDoomCardContributor.Value;
        if (power is DoomPower)
        {
            CurrentDoomCardContributor.Value = cardSource is Misery
                ? cardSource.Owner.NetId
                : null;
        }
        return previous;
    }

    internal static void EndDoomCardApplication(ulong? previous) =>
        CurrentDoomCardContributor.Value = previous;

    internal static DoomKillCredit CaptureDoomKill(Creature enemy) =>
        DoomTracker.CaptureKill(enemy, enemy.CurrentHp);

    internal static void CompleteDoomKill(DoomKillCredit credit, Creature enemy)
    {
        Safely(nameof(CompleteDoomKill), () =>
        {
            if (!IsRunActive() || !enemy.IsDead || !enemy.IsEnemy)
            {
                return;
            }
            var roomKind = enemy.CombatState?.RunState.CurrentRoom?.RoomType switch
            {
                RoomType.Elite => CombatRoomKind.Elite,
                RoomType.Boss => CombatRoomKind.Boss,
                _ => CombatRoomKind.Normal
            };
            var hpRemoved = Math.Max(0, (long)credit.HpBefore - enemy.CurrentHp);
            DoomTracker.CompleteKill(credit, hpRemoved, roomKind, BuildPoisonKillEntropy(enemy, enemy));
        });
    }

    internal static PoisonSequenceScope BeginPoisonSequence(
        PoisonPower power,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        var parent = CurrentPoisonSequence.Value;
        PoisonSequenceScope? scope = null;
        Safely(nameof(BeginPoisonSequence), () =>
        {
            var active = IsRunActive() && participants.Contains(power.Owner) && power.Amount > 0;
            IReadOnlyList<ulong?> sponsors = Array.Empty<ulong?>();
            if (active)
            {
                var livingPlayers = combatState.GetOpponentsOf(power.Owner)
                    .Where(creature => creature.IsAlive && ResolvePlayer(creature) is not null)
                    .ToArray();
                var liveAmounts = livingPlayers.ToDictionary(
                    creature => ResolvePlayer(creature)!.NetId,
                    creature => (long)creature.GetPowerAmount<AccelerantPower>());
                var livingIds = livingPlayers
                    .Select(creature => ResolvePlayer(creature)!.NetId)
                    .ToHashSet();
                var iterations = Math.Min(
                    power.Amount,
                    checked(1 + livingPlayers.Sum(creature => creature.GetPowerAmount<AccelerantPower>())));
                sponsors = AccelerantSponsorLedger.ResolveSponsors(
                    Math.Max(iterations - 1, 0),
                    liveAmounts,
                    livingIds);
            }

            scope = new PoisonSequenceScope(parent, power, sponsors, active);
            CurrentPoisonSequence.Value = scope;
        });

        scope ??= new PoisonSequenceScope(parent, power, Array.Empty<ulong?>(), false);
        CurrentPoisonSequence.Value = scope;
        return scope;
    }

    internal static void DetachPoisonSequence(PoisonSequenceScope scope)
    {
        if (ReferenceEquals(CurrentPoisonSequence.Value, scope))
        {
            CurrentPoisonSequence.Value = scope.Parent;
        }
    }

    internal static PoisonDamageCommandScope BeginPoisonDamageCommand(
        IEnumerable<Creature> targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        var parent = CurrentPoisonDamageCommand.Value;
        var sequence = CurrentPoisonSequence.Value;
        var targetList = targets as IReadOnlyList<Creature>;
        var isPoison = parent is null &&
                       sequence?.Active == true &&
                       targetList?.Count == 1 &&
                       ReferenceEquals(targetList[0], sequence.Owner) &&
                       amount == sequence.Power.Amount &&
                       props == (ValueProp.Unblockable | ValueProp.Unpowered) &&
                       dealer is null &&
                       cardSource is null;
        var trigger = isPoison ? sequence!.TakeTrigger() : (false, (ulong?)null);
        var scope = new PoisonDamageCommandScope(
            parent,
            isPoison ? sequence!.Owner : null,
            isPoison,
            trigger.Item1,
            trigger.Item2);
        CurrentPoisonDamageCommand.Value = scope;
        return scope;
    }

    internal static async Task<IEnumerable<DamageResult>> CompletePoisonDamageCommandAsync(
        Task<IEnumerable<DamageResult>> original,
        PoisonDamageCommandScope scope)
    {
        try
        {
            var results = (await original).ToList();
            if (scope.IsPoison && scope.PoisonOwner is not null)
            {
                Safely(nameof(CompletePoisonDamageCommandAsync), () =>
                    RecordPoisonDamageResults(scope, results));
            }
            return results;
        }
        finally
        {
            if (scope.PoisonCycleResetDeferred && scope.PoisonOwner is not null)
            {
                Safely(nameof(CompletePoisonDamageCommandAsync), () =>
                    PoisonTracker.ObservePoisonAmount(scope.PoisonOwner, 0, 0, null));
            }

            DetachPoisonDamageCommand(scope);
        }
    }

    internal static void DetachPoisonDamageCommand(PoisonDamageCommandScope scope)
    {
        if (ReferenceEquals(CurrentPoisonDamageCommand.Value, scope))
        {
            CurrentPoisonDamageCommand.Value = scope.Parent;
        }
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

    public static void OnDamageReceived(Creature target, DamageResult result, Creature? dealer)
    {
        Safely(nameof(OnDamageReceived), () =>
        {
            if (target.Player is not null)
            {
                CombatTracker.RecordDamageReceived(
                    target.Player.NetId,
                    result.UnblockedDamage,
                    result.BlockedDamage,
                    dealer?.IsEnemy == true);
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
            DetachActionEvents();
            DetachCreatureEvents();
            DetachPlayerEvents();
            CombatTracker.ResetForRun();
            AssistedContributionTracker.ResetCombat();
            StrengthTracker.ResetCombat();
            PoisonTracker.ResetCombat();
            DoomTracker.Clear();
            CurrentDoomCardContributor.Value = null;
            CurrentPoisonSequence.Value = null;
            CurrentPoisonDamageCommand.Value = null;
            CurrentTemporaryStrengthMutation.Value = null;
            CurrentOwnedStrengthSource.Value = null;
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
            DetachActionEvents();
            DetachCreatureEvents();
            DetachPlayerEvents();
            CombatTracker.ResetForRun();
            AssistedContributionTracker.ResetCombat();
            StrengthTracker.ResetCombat();
            PoisonTracker.ResetCombat();
            DoomTracker.Clear();
            CurrentDoomCardContributor.Value = null;
            CurrentPoisonSequence.Value = null;
            CurrentPoisonDamageCommand.Value = null;
            CurrentTemporaryStrengthMutation.Value = null;
            CurrentOwnedStrengthSource.Value = null;
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
            AttachActionEvents();
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
                DetachActionEvents();
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

    internal static void OnPlayerActionCanceled(GameAction action) =>
        Safely(nameof(OnPlayerActionCanceled), () => StrengthTracker.CancelAction(action));

    private static void OnBeforePlayerAction(GameAction action)
    {
        Safely(nameof(OnBeforePlayerAction), () =>
        {
            if (IsRunActive() && CombatManager.Instance.IsInProgress)
            {
                StrengthTracker.BeginAction(
                    action,
                    action.OwnerId,
                    CaptureStrengthSnapshot());
            }
        });
    }

    private static void OnAfterPlayerAction(GameAction action)
    {
        Safely(nameof(OnAfterPlayerAction), () =>
        {
            var completion = StrengthTracker.CompleteAction(
                action,
                CaptureStrengthSnapshot());
            if (!completion.Accepted && completion.TargetsReset > 0)
            {
                RunStatsLog.Warn(
                    "Strength action reconciliation failed; affected contribution ledgers were reset.");
            }
        });
    }

    private static void AttachActionEvents()
    {
        DetachActionEvents();
        var executor = RunManager.Instance.ActionExecutor;
        executor.BeforeActionExecuted += OnBeforePlayerAction;
        executor.AfterActionExecuted += OnAfterPlayerAction;
        _subscribedActionExecutor = executor;
    }

    private static void DetachActionEvents()
    {
        if (_subscribedActionExecutor is null)
        {
            return;
        }

        _subscribedActionExecutor.BeforeActionExecuted -= OnBeforePlayerAction;
        _subscribedActionExecutor.AfterActionExecuted -= OnAfterPlayerAction;
        _subscribedActionExecutor = null;
    }

    private static IReadOnlyDictionary<object, StrengthTargetSnapshot> CaptureStrengthSnapshot()
    {
        var snapshot = new Dictionary<object, StrengthTargetSnapshot>(
            ReferenceEqualityComparer.Instance);
        var combatState = _activeRunState?.Players
            .Select(player => player.Creature.CombatState)
            .FirstOrDefault(state => state is not null);
        if (combatState is null)
        {
            return snapshot;
        }

        foreach (var creature in combatState.Creatures)
        {
            snapshot.Add(
                creature,
                new StrengthTargetSnapshot(
                    creature.GetPowerAmount<StrengthPower>(),
                    creature.Player?.NetId));
        }

        return snapshot;
    }

    private static void ObserveStrengthAmount(
        StrengthPower power,
        Creature? applier,
        int previousAmount,
        int currentAmount)
    {
        var action = CurrentPlayerAction();
        var temporaryScope = CurrentTemporaryStrengthMutation.Value;
        var delayedSource = CurrentDelayedStrengthSource(action);
        var delayedContributor = delayedSource is null
            ? null
            : StrengthTracker.ResolveSourceOwner(delayedSource);
        delayedContributor ??= CurrentOwnedStrengthSource.Value?.ContributorNetId;
        var result = StrengthTracker.ObserveAmount(
            action,
            power.Owner,
            power.Owner.Player?.NetId,
            previousAmount,
            currentAmount,
            ResolvePlayer(applier)?.NetId,
            delayedContributor,
            temporaryScope is null ? -1 : 1,
            temporaryScope is null
                ? StrengthImpactExpiryKind.CombatPersistent
                : StrengthImpactExpiryKind.FixedTemporary,
            temporaryScope?.Power);
        if (!result.Accepted)
        {
            State.TryRecordDiagnostic(
                power.Owner.IsEnemy
                    ? DiagnosticKind.AmbiguousAssistedDamagePrevented
                    : DiagnosticKind.AmbiguousAssistedDamage);
        }
    }

    private static void ObserveStrengthSourceOwner(
        PowerModel power,
        Creature? applier,
        int previousAmount,
        int currentAmount)
    {
        if (previousAmount != 0 || currentAmount == 0)
        {
            return;
        }

        var actionOwner = CurrentPlayerAction()?.OwnerId;
        var contributor = actionOwner is > 0
            ? actionOwner
            : ResolvePlayer(applier)?.NetId;
        if (contributor is > 0)
        {
            StrengthTracker.ObserveSourceOwner(power, contributor.Value);
        }
    }

    private static GameAction? CurrentPlayerAction()
    {
        try
        {
            return RunManager.Instance.ActionExecutor?.CurrentlyRunningAction;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PowerModel? CurrentDelayedStrengthSource(GameAction? action) =>
        action is GenericHookGameAction hookAction
            ? hookAction.ChoiceContext?.Source as PowerModel
            : null;

    private static Player? ResolvePlayer(Creature? creature) => creature?.Player ?? creature?.PetOwner;

    private static bool TryGetAssistedEffectKind(
        PowerModel power,
        out AssistedEffectKind kind)
    {
        if (power is VulnerablePower)
        {
            kind = AssistedEffectKind.Vulnerable;
            return true;
        }

        if (power is WeakPower)
        {
            kind = AssistedEffectKind.Weak;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryDeferAssistedCycleReset(
        Creature owner,
        AssistedEffectKind kind)
    {
        for (var scope = CurrentDamageScope.Value; scope is not null; scope = scope.Parent)
        {
            if (scope.TryDeferReset(owner, kind))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnCombatEnded(CombatRoom room) => Safely(nameof(OnCombatEnded), () =>
    {
        PoisonTracker.ResetCombat();
        DoomTracker.Clear();
        CurrentDoomCardContributor.Value = null;
        CombatTracker.ResetForRun();
        AssistedContributionTracker.ResetCombat();
        StrengthTracker.ResetCombat();
        CurrentPoisonSequence.Value = null;
        CurrentPoisonDamageCommand.Value = null;
        CurrentTemporaryStrengthMutation.Value = null;
        CurrentOwnedStrengthSource.Value = null;
    });

    private static void RecordPoisonDamageResults(
        PoisonDamageCommandScope scope,
        IReadOnlyList<DamageResult> results)
    {
        var owner = scope.PoisonOwner!;
        long actualDamage = 0;
        foreach (var result in results)
        {
            actualDamage = checked(actualDamage + ActualDelta.ResolvedDamage(result.UnblockedDamage));
        }

        if (!PoisonTracker.RecordPoisonTrigger(
                owner,
                actualDamage,
                scope.IsExtraTrigger,
                scope.SponsorNetId,
                out var allocation))
        {
            return;
        }

        foreach (var (player, damage) in allocation.PlayerDamage)
        {
            if (damage > 0)
            {
                DoomTracker.RecordDirectDamage(owner, player, damage);
            }
        }

        var roomKind = owner.CombatState?.RunState.CurrentRoom?.RoomType switch
        {
            RoomType.Elite => CombatRoomKind.Elite,
            RoomType.Boss => CombatRoomKind.Boss,
            _ => CombatRoomKind.Normal
        };
        foreach (var result in results.Where(result => result.WasTargetKilled && result.Receiver.IsDead))
        {
            PoisonTracker.RecordPoisonKill(
                owner,
                result.Receiver,
                roomKind,
                BuildPoisonKillEntropy(owner, result.Receiver));
        }
    }

    private static ulong BuildPoisonKillEntropy(Creature poisonedEnemy, Creature killedTarget)
    {
        var hash = 14695981039346656037UL;
        var seed = State.CaptureSnapshot().Identity?.Seed ?? string.Empty;
        foreach (var character in seed)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        hash ^= poisonedEnemy.CombatId ?? uint.MaxValue;
        hash *= 1099511628211UL;
        hash ^= killedTarget.CombatId ?? uint.MaxValue;
        hash *= 1099511628211UL;
        hash ^= (ulong)(uint)(poisonedEnemy.CombatState?.RoundNumber ?? 0);
        return hash;
    }

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

    private static void DiscardLegacyAssistedOwnership(
        IReadOnlyList<AssistedOwnershipRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        RunStatsLog.Warn(
            $"Ignored {records.Count} legacy assisted-ownership record(s) while restoring " +
            "compatible accumulated totals; combat-only weighted attribution starts fresh.");
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
        var multiplier = power.ModifyDamageMultiplicative(target, modifiedDamage, props, dealer, cardSource);
        if (multiplier > 1m)
        {
            scope.Track(power.Owner, AssistedEffectKind.Vulnerable);
            scope.Observations[target] = new DamageAssistObservation(
                StatKind.AssistedDamage,
                power.Owner,
                attackerNetId,
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
        var multiplier = power.ModifyDamageMultiplicative(target, modifiedDamage, props, dealer, cardSource);
        if (multiplier > 0m && multiplier < 1m)
        {
            scope.Track(power.Owner, AssistedEffectKind.Weak);
            scope.Observations[target] = new DamageAssistObservation(
                StatKind.AssistedDamagePrevented,
                power.Owner,
                protectedPlayerNetId,
                modifiedDamage,
                multiplier);
        }
    }

    private static void TryObserveStrengthAssistedDamage(
        DamageAssistScope scope,
        IRunState runState,
        ICombatState combatState,
        Creature target,
        Creature dealer,
        ValueProp props,
        CardModel? cardSource,
        decimal modifiedDamage,
        IReadOnlyList<AbstractModel> modifiers,
        IReadOnlyList<VulnerablePower> vulnerablePowers)
    {
        var events = StrengthImpactLedger.GetEvents(dealer);
        if (events.Count == 0 || vulnerablePowers.Count > 1 ||
            HasHpLossOrRedirectionOverride(runState, combatState))
        {
            return;
        }

        var strengthPowers = modifiers.OfType<StrengthPower>().Distinct().ToList();
        var weakPowers = modifiers.OfType<WeakPower>().Distinct().ToList();
        if (strengthPowers.Count != 1 || weakPowers.Count > 1)
        {
            return;
        }

        foreach (var modifier in modifiers.Distinct())
        {
            if (modifier is StrengthPower or WeakPower or VulnerablePower)
            {
                continue;
            }

            // Unknown additive modifiers are already part of the observed
            // baseline. Unknown multiplicative stages cannot be removed exactly.
            if (Overrides(modifier, DamageMultiplicativeMethod))
            {
                return;
            }
        }

        var vulnerableMultiplier = 1m;
        if (vulnerablePowers.Count == 1)
        {
            vulnerableMultiplier = vulnerablePowers[0].ModifyDamageMultiplicative(
                target,
                modifiedDamage,
                props,
                dealer,
                cardSource);
            if (vulnerableMultiplier <= 1m)
            {
                return;
            }
        }

        var strengthDownstreamMultiplier = 1m;
        if (weakPowers.Count == 1)
        {
            strengthDownstreamMultiplier = weakPowers[0].ModifyDamageMultiplicative(
                target,
                modifiedDamage,
                props,
                dealer,
                cardSource);
            if (strengthDownstreamMultiplier <= 0m || strengthDownstreamMultiplier >= 1m)
            {
                return;
            }
        }

        scope.StrengthObservations[target] = new StrengthOutgoingDamageObservation(
            dealer,
            modifiedDamage,
            vulnerableMultiplier,
            strengthDownstreamMultiplier,
            events.ToArray());
    }

    private static void TryObserveStrengthPreventedDamage(
        DamageAssistScope scope,
        IRunState runState,
        ICombatState combatState,
        Creature target,
        Creature dealer,
        ValueProp props,
        CardModel? cardSource,
        decimal modifiedDamage,
        ulong protectedPlayerNetId,
        IReadOnlyList<AbstractModel> modifiers,
        IReadOnlyList<WeakPower> weakPowers,
        decimal unmodifiedDamage)
    {
        var events = StrengthImpactLedger.GetEvents(dealer);
        if (events.Count == 0 || weakPowers.Count > 1 ||
            HasHpLossOrRedirectionOverride(runState, combatState))
        {
            return;
        }

        var strengthPowers = modifiers.OfType<StrengthPower>().Distinct().ToList();
        var vulnerablePowers = modifiers.OfType<VulnerablePower>().Distinct().ToList();
        if (strengthPowers.Count != 1 || vulnerablePowers.Count > 1)
        {
            return;
        }

        decimal? exactUnmodifiedDamage = null;
        if (modifiedDamage == 0m)
        {
            if (unmodifiedDamage < 0m || modifiers
                .Distinct()
                .Any(modifier => modifier is not StrengthPower &&
                                 Overrides(modifier, DamageAdditiveMethod)))
            {
                return;
            }

            exactUnmodifiedDamage = unmodifiedDamage;
        }

        foreach (var modifier in modifiers.Distinct())
        {
            if (modifier is StrengthPower or WeakPower or VulnerablePower)
            {
                continue;
            }

            if (Overrides(modifier, DamageMultiplicativeMethod))
            {
                return;
            }
        }

        var weakMultiplier = 1m;
        if (weakPowers.Count == 1)
        {
            weakMultiplier = weakPowers[0].ModifyDamageMultiplicative(
                target,
                modifiedDamage,
                props,
                dealer,
                cardSource);
            if (weakMultiplier <= 0m || weakMultiplier >= 1m)
            {
                return;
            }
        }

        var strengthDownstreamMultiplier = 1m;
        if (vulnerablePowers.Count == 1)
        {
            strengthDownstreamMultiplier = vulnerablePowers[0].ModifyDamageMultiplicative(
                target,
                modifiedDamage,
                props,
                dealer,
                cardSource);
            if (strengthDownstreamMultiplier <= 1m)
            {
                return;
            }
        }

        scope.StrengthIncomingObservations[target] = new StrengthIncomingDamageObservation(
            dealer,
            modifiedDamage,
            exactUnmodifiedDamage,
            strengthPowers[0].Amount,
            weakMultiplier,
            strengthDownstreamMultiplier,
            protectedPlayerNetId,
            events.ToArray());
    }

    private static void RecordAssistedDamage(Creature target, DamageResult result)
    {
        var scope = CurrentDamageScope.Value;
        if (scope is null)
        {
            return;
        }

        if (!scope.Observations.ContainsKey(target) &&
            !scope.StrengthObservations.ContainsKey(target) &&
            !scope.StrengthIncomingObservations.ContainsKey(target))
        {
            return;
        }

        var preHitBlock = checked(target.Block + result.BlockedDamage);
        var preHitHp = checked(target.CurrentHp + result.UnblockedDamage);
        if (scope.Observations.Remove(target, out var observation))
        {
            RecordWeakOrVulnerableAssist(
                scope,
                observation,
                result,
                preHitBlock,
                preHitHp);
        }

        if (scope.StrengthObservations.Remove(target, out var strengthObservation) &&
            StrengthDamageCalculator.TryCalculateOutgoing(
                strengthObservation.ActualModifiedDamage,
                strengthObservation.VulnerableMultiplier,
                strengthObservation.StrengthDownstreamMultiplier,
                preHitBlock,
                preHitHp,
                result.UnblockedDamage,
                strengthObservation.Events,
                out var calculation))
        {
            if (StrengthImpactLedger.TryAllocate(
                    strengthObservation.Attacker,
                    StrengthAssistDirection.OutgoingDamage,
                    calculation.EligibilityPool,
                    calculation.EventCapacities,
                    out var allocation))
            {
                StrengthStats.Record(allocation, StatKind.AssistedDamage);
            }
        }

        if (scope.StrengthIncomingObservations.Remove(target, out var incomingObservation) &&
            StrengthDamageCalculator.TryCalculateIncoming(
                incomingObservation.ActualModifiedDamage,
                incomingObservation.ExactUnmodifiedDamage,
                incomingObservation.CurrentStrength,
                incomingObservation.WeakMultiplier,
                incomingObservation.StrengthDownstreamMultiplier,
                incomingObservation.ProtectedPlayerNetId,
                incomingObservation.Events,
                out var incomingCalculation))
        {
            if (StrengthImpactLedger.TryAllocate(
                    incomingObservation.Attacker,
                    StrengthAssistDirection.IncomingPrevention,
                    incomingCalculation.EligibilityPool,
                    incomingCalculation.EventCapacities,
                    out var allocation))
            {
                StrengthStats.Record(allocation, StatKind.AssistedDamagePrevented);
            }
        }
    }

    private static void RecordWeakOrVulnerableAssist(
        DamageAssistScope scope,
        DamageAssistObservation observation,
        DamageResult result,
        int preHitBlock,
        int preHitHp)
    {
        long amount;
        if (observation.Stat == StatKind.AssistedDamage)
        {
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
            if (observation.Stat == StatKind.AssistedDamage)
            {
                AssistedStats.RecordVulnerableAssist(
                    observation.EffectOwner,
                    amount,
                    observation.BeneficiaryNetId,
                    out _);
            }
            else
            {
                scope.WeakPreventionRows.Add(new WeakPreventionObservation(
                    observation.EffectOwner,
                    observation.BeneficiaryNetId,
                    amount));
            }
        }
    }

    private static void RecordAggregatedWeakPrevention(DamageAssistScope scope)
    {
        var rowsByEnemy = new Dictionary<Creature, List<WeakPreventionRow>>(
            ReferenceEqualityComparer.Instance);
        foreach (var row in scope.WeakPreventionRows)
        {
            if (!rowsByEnemy.TryGetValue(row.EffectOwner, out var rows))
            {
                rows = new List<WeakPreventionRow>();
                rowsByEnemy.Add(row.EffectOwner, rows);
            }

            rows.Add(new WeakPreventionRow(row.ProtectedPlayerNetId, row.Prevention));
        }

        foreach (var entry in rowsByEnemy)
        {
            AssistedStats.RecordWeakPreventionCommand(
                entry.Key,
                entry.Value,
                out _);
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

    private static bool IsRunActive() => State.CaptureSnapshot().Lifecycle == RunLifecycle.Active;

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
