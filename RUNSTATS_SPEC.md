# RunStats Persistent Project Specification

## Project and status

- **Project:** RunStats, a local Slay the Spire 2 statistics mod.
- **Purpose:** Track meaningful per-player statistics for the complete active run in single-player and co-op, expose them through a native-feeling top-bar UI, and preserve them through save/quit/continue.
- **Target:** Installed Steam public/default branch, app 2868840, Steam build ID `23811903`; STS2 `v0.107.1`, commit `59260271`, release date 2026-06-18, assembly hash `-1555940892`.
- **Engine/runtime:** Godot 4.5.1 C#, game target .NET 9.0. Local SDK 9.0.317 and runtime 9.0.19 are installed system-wide.
- **Current stage:** The verified v0.3.1 Weak/Vulnerable and Strength update is public as Workshop item `3797791393`. Strength Phases 1-9 are complete; the published package is preserved in `workshop/RunStats/content`.
- **Completed stages:** Stage 0 Research and Feasibility; Stage 1 Project Scaffold; Stage 2 Run and Player Model; Stage 3 Core Combat Tracking; Stage 4 Assisted Statistics; Stage 5 Cards, Economy, and Items; Stage 6 Multiplayer Hardening; Stage 7 UI; Stage 8 Save/Load and Edge Cases; Stage 9 Final Local Playtest.
- **Overall feasibility:** **PARTIAL.** Ordinary statistics remain direct observations. Standard Weak/Vulnerable assistance uses the approved cumulative-weight convention because STS2 merges those durations. Strength uses separate signed, source/lifetime-aware events because its final counter changes and temporary restorations are observable. Unsupported or ambiguous damage-pipeline cases still fail closed.
- **Next-release authority:** `WEAK_VULNERABLE_ATTRIBUTION_SPEC.md` defines the approved proportional convention, and `STRENGTH_ATTRIBUTION_SPEC.md` defines signed Strength attribution. The combined branch uses schema 4 so v0.3.0 schema-3 Doom data remains distinguishable from signed assisted totals.
- **Pending release work:** Wait for Steam to refresh the local subscribed copy and verify its files before using it. Deferred live multiplayer cases remain labeled as such, including the zero-clamped multi-hit correction that the user accepted from its exact automated reproduction. Live two-peer rejoin reconciliation remains an explicitly documented limitation rather than a release claim.

## Scope and approved requirements

- Local development, installation, and playtesting were required before release and are complete. The user explicitly authorized Steam Workshop preparation and publishing on 2026-09-07. The initial upload must remain private until the listing and Steam Workshop legal agreement are reviewed.
- Support single-player and multiplayer. Each player is keyed by stable `Player.NetId` (`ulong`) and has independent statistics; team totals are derived from player totals.
- Statistics last for the entire current run and must survive Continue Run where technically supported.
- A `STATS` button appears near the top of the run UI and opens a native-feeling, responsive screen. Hide it outside an active run or where interaction is inappropriate.
- Use event-driven tracking; no per-frame polling/reflection. Optional metrics must fail closed without affecting gameplay.
- Never modify vanilla game files or unrelated mods. Build in this workspace. Deploy only RunStats-owned artifacts after the required first-deployment approval.
- Every stage must update this file, report results, stop, and wait for the exact user approval phrase `Approved. Continue.`

## Statistic definitions

All totals are per `Player.NetId` unless noted.

- **Damage Dealt:** actual enemy HP removed by player-attributable damage (`DamageResult.UnblockedDamage`), excluding blocked damage and overkill. Multi-hit counts each resolved hit. A pet/summon resolves to its owning player where `Creature.PetOwner` is present. Damage with no reliable player provenance remains uncredited rather than guessed.
- **Poison Applied:** actual positive change to an enemy's Poison amount after modifiers, credited to the reliable applying player's `NetId`. Unattributable increases receive no player credit but remain in the damage-allocation denominator. Contributor weights are cumulative only for the enemy's current continuous nonzero Poison cycle.
- **Doom Applied:** actual positive change to an enemy's Doom amount after modifiers, credited to the applying player's `NetId` across cards, relics, potions, and other sources. When Misery copies Doom, the copied amount is credited to Misery's player, even though the game passes the original applier.
- **Damage Taken:** actual player HP removed (`DamageResult.UnblockedDamage`), after Block and HP-loss modifiers; excludes blocked damage and overkill.
- **Healing Done:** actual player HP restored, capped by missing HP; max-HP gain is not healing. Unless later approved otherwise, this means healing received by that player's creature (STS2's ordinary per-player history has recipient attribution, not a general healer source).
- **Max HP Gained:** actual permanent positive change to the player's maximum HP, separate from healing.
- **Block Gained:** actual positive change in player Block after modifiers and integer truncation.
- **Block Lost:** actual player Block absorbed by enemy damage, measured from `DamageResult.BlockedDamage`. End-of-turn clearing and other non-enemy Block reductions are excluded.
- **Enemies Killed:** enemy deaths caused by reliably player-attributed lethal damage, counted once. Forced/environmental/deferred deaths without a reliable player source are uncredited.
- **Elite Enemies Killed / Bosses Killed:** credited enemy kills in a `CombatRoom` whose `Encounter.RoomType` is `RoomType.Elite` / `RoomType.Boss`. This is kill credit, not merely room completion.
- **Cards Played:** every completed `CardPlay`, including autoplay and each Replay execution. `CardPlay.PlayIndex` distinguishes repeated executions. Most Played Card uses these same counts.
- **Gold Earned:** positive gold actually added after modifiers; stolen gold returned is excluded, matching `PlayerMapPointHistoryEntry.GoldGained`.
- **Gold Spent:** gold removed with `GoldLossType.Spent`; ordinary loss/theft is excluded.
- **Cards Obtained:** cards actually added to the permanent deck; generated combat-only cards are excluded.
- **Cards Upgraded:** successful permanent upgrade operations; each card upgrade is one.
- **Cards Removed:** cards actually removed from the permanent deck.
- **Relics Obtained:** successful non-starting relic acquisitions. Starting inventory population is excluded.
- **Potions Obtained:** successful potion procurement into a slot. Failed procurement and discarded choices are excluded.
- **Potions Used:** completed potion uses, not discards.
- **Damage/Healing by Card:** optional; only credit when `cardSource` is present and reliable.
- **Relic/Potion contributions:** optional; implement only if source provenance is reliable.

### Assisted Damage

Additional actual enemy HP damage caused when players' Vulnerable benefits an attacker. The attacker retains full Damage Dealt. Compute the existing non-mutating live-multiplier counterfactual, compare actual HP loss after Block, truncation, HP-loss boundaries, and overkill, then split the integer assist pool by cumulative current-cycle Vulnerable applications. Attacker self-shares and unattributed shares are discarded without redistribution.

Signed external Strength contribution to a player's powered attack is also included. For every resolved target/hit, remove Vulnerable's already attributed layer, retain self/native Strength as baseline, and compare actual versus no-external-Strength HP loss after Block, HP, truncation, and overkill. Harmful negative Strength events subtract; helpful events add. Harmful events are processed first, then earliest application order restarts on every hit.

Accelerant is a separately approved assisted case for v0.2.0. Each extra Poison trigger is assigned in Accelerant application order, with upgraded Accelerant contributing two adjacent sponsor positions. The sponsor receives Assisted Damage equal to the integer Poison damage credited to other players on that trigger, never their own or the unattributed share.

### Poison damage and kill attribution

Each top-level Poison damage command aggregates its returned resolved HP loss, including redirects, then divides that actual damage by cumulative current-cycle contribution weights. Exact rational remainders carry forward; stable NetId ordering resolves the first indivisible point and carried residuals rotate later extras fairly, including three-player one-extra and two-extra cycles.

The first trigger is standard. Accelerant creates only the later sponsored triggers and does not add Poison Applied. If sponsor state cannot be reconciled with living players' Accelerant amounts, the affected extra trigger grants normal Poison Damage Dealt but no assist.

For a Poison kill, compare credited Poison damage to that enemy within the current nonzero cycle, then Poison Applied in that cycle. A remaining tie selects exactly one player using deterministic run seed, combat/round/enemy information, and tied NetIds without consuming the game RNG. Reaching zero or explicit removal resets contributions, fractions, and kill comparisons.

### Doom damage and kill attribution

Doom Applied appears immediately below Poison Applied. Doom adds to Damage Dealt only when it kills the enemy. The HP Doom actually removes is divided by each player's share of Doom applied to that enemy in the current Doom cycle. For example, 70 and 30 Doom applied to an enemy with 10 HP remaining grants 7 and 3 Damage Dealt. Unattributed Doom remains in the denominator. The largest Doom contributor receives the kill; a Doom tie is broken by actual damage to that enemy, then by deterministic run/enemy tie selection. A prevented Doom death grants neither Doom damage nor kill credit. Removing Doom without a kill ends its contribution cycle.

### Damage Prevented

Damage a Weak enemy attack would additionally have dealt before defender-owned mitigation. Calculate the live Weak counterfactual separately for every player target, sum all target prevention into one command pool, and split that pool once by cumulative current-cycle Weak applications. Split each contributor's gross award between self-protection and teammate protection; discard self-protection and carry indivisible split fractions across attacks. Player Block, powers, relics, and other defender mitigation remain outside this statistic.

Signed player-caused Strength changes on an attacking enemy are included separately for every player target/hit. Remove Weak's attributed layer, keep the protected player's own event in that target's baseline, and compare no-external-Strength versus actual pre-Block damage. Enemy positive Strength is harmful and subtracts from prevention before helpful negative Strength events add credit; earliest event order restarts for every target/hit.

### Merged assisted cases

- `PowerModel` has `Owner`, `Applier`, and `Target`, but standard `WeakPower` and `VulnerablePower` use `PowerInstanceType.None` (default). `PowerCmd.FindExistingInstanceForStacking` merges subsequent applications into the existing target power. `PowerCmd.ModifyAmount` receives the new applier but does not update `PowerModel.Applier`.
- RunStats can ledger applications by patching `PowerCmd.Apply`/`ModifyAmount`, but when durations from several players overlap the game retains only a shared integer duration. There is no intrinsic rule identifying whose duration is currently causal; FIFO, proportional, or Shapley allocation would be a RunStats convention, not exact game evidence.
- Version 0.2.0 conservatively suppressed mixed ownership. The approved v0.2.1 convention supersedes that behavior by recording each final positive application as a cumulative known-player or unattributed weight until the corresponding enemy power reaches zero.
- Natural decay and nonzero reductions do not alter weights. Each positive application restarts deterministic reverse-application remainder order. Unknown shares remain denominator participants but are never credited.
- This is an explicit RunStats allocation convention, not a claim that STS2 preserves intrinsic stack ownership. Damage caps, unprovable Vulnerable HP-loss/redirection overrides, malformed observations, and unsupported duplicate/modded arrangements continue to fail closed.

## Confirmed environment and mod system

- Game: `D:\SteamLibrary\steamapps\common\Slay the Spire 2`.
- Game assembly: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll`.
- Configured local mods path: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods`.
- The mods directory did not exist during Stage 0. MCP `list_installed_mods` returned `[]`. The game installation top level contained only the normal game directories/binaries and no local mod directory. No game/mod files were created, modified, moved, renamed, disabled, or deleted.
- The active Steam user-data root is `C:\Users\Mike Major\AppData\Roaming\SlayTheSpire2\steam\76561198122724722`. The v0.2.0 work treats this as protected save/profile state, not a deployment directory; existing RunStats sidecars were observed but not opened or changed during Phase 1.
- Steam Workshop content exists separately under `D:\Steam\steamapps\workshop\content\2868840` and currently contains BaseLib v3.4.5, Minty Spire 2 v1.2.0, and Import Vanilla Saves v0.2.1. The empty MCP local-mod inventory therefore does not mean the overall installation is unmodded.
- Steam manifest has no beta branch key, so the installed build is on the public/default branch.
- `ModManager.Initialize` scans `<game executable directory>/mods` recursively for `mod_manifest.json`, then loads the declared DLL and optional PCK. A `[ModInitializer("MethodName")]` entry point is supported; otherwise the loader calls Harmony `PatchAll`.
- Current built-in modding libraries: Harmony 2.4.2 and MonoMod. The mod manifest supports `id`, `name`, `author`, `description`, `version`, `has_pck`, `has_dll`, dependency records, `affects_gameplay`, and `min_game_version`.
- MCP decompilation/index is ready (3,424 C# files); GDRE Tools are absent, so PCK asset extraction/scene text inspection was unavailable. MCP `search_game_assets` also failed with its own `UnboundLocalError`. This did not block code/API feasibility.
- STS2 was not running. Bridge and GodotExplorer live calls confirmed their services were unavailable. The game was not launched because doing so would cause MCP companion mods to be installed into the currently absent mods directory, an external game-directory mutation not needed for Stage 0. Live visual inspection remains for later approved stages.

## Confirmed hooks and tracking map

Reliability assumes the exact target build above. All peer-local hooks execute against the same synchronized game actions; RunStats must deduplicate by event/action identity where a hook can repeat.

| Statistic | Exact game class/method/event | Tracking strategy | Multiplayer implications | Reliability |
|---|---|---|---|---|
| Damage Dealt | `Hook.AfterDamageGiven(... Creature? dealer, DamageResult results, ... Creature target, CardModel? cardSource)`; central `CreatureCmd.Damage(...)` | Add `results.UnblockedDamage` for enemy targets; resolve dealer player/pet owner; do not count overkill/Block | `dealer.Player.NetId`; deterministic hook on all peers | High for direct/player-pet damage; partial for null-source indirect damage |
| Damage Taken | `Hook.AfterDamageReceived(... Creature target, DamageResult result, ... Creature? dealer, ...)`; `PlayerMapPointHistoryEntry.DamageTaken` | Add `result.UnblockedDamage` for player receiver; vanilla history can validate/rebuild | Receiver `target.Player.NetId` | High |
| Healing Done | `CreatureCmd.Heal(Creature, decimal, bool)`; `Creature.CurrentHpChanged`; vanilla `HpHealed` | Harmony prefix/postfix captures actual HP delta, excluding max-HP path; validate against saved history | Recipient player's NetId | High for healing received; healer-source attribution is not generally available |
| Max HP Gained | `CreatureCmd.GainMaxHp(Creature, decimal)`; `Creature.MaxHpChanged`; vanilla `MaxHpGained` | Capture actual positive max-HP delta; validate against saved history | Receiver player's NetId | High |
| Block Gained | `Creature.BlockChanged(old,new)`; native `Hook.AfterBlockGained` | Subscribe for positive actual delta; use AfterBlockGained for source metadata | Creature's player NetId | High |
| Block Lost | `Hook.AfterDamageReceived(... Creature target, DamageResult result, ... Creature? dealer, ...)` | Add `result.BlockedDamage` only when the receiver is a player and the dealer is an enemy | Receiver `target.Player.NetId` | High for enemy-sourced damage |
| Enemies Killed | `Hook.AfterDamageGiven` + `DamageResult.WasTargetKilled`; `Hook.AfterDeath` | Count first lethal result per creature when dealer resolves to player; AfterDeath is a validation/cleanup signal | Killer NetId from dealer/pet owner | High for damage kills; partial for source-less forced/deferred death |
| Elite Kills | Above + `CombatRoom.Encounter.RoomType == RoomType.Elite` | Classify credited kill by current combat encounter | Same killer NetId | High under defined kill-credit rule |
| Boss Kills | Above + `CombatRoom.Encounter.RoomType == RoomType.Boss` | Classify credited kill by current combat encounter | Same killer NetId | High under defined kill-credit rule |
| Cards Played | `Hook.AfterCardPlayed(ICombatState, PlayerChoiceContext, CardPlay)`; `CardPlay.Card.Owner`, `PlayIndex`, `PlayCount`, `IsAutoPlay` | Count each successfully completed CardPlay; keep per-card ID counts | Owner `NetId`; action queue is synchronized | High |
| Assisted Damage | `CreatureCmd.Damage`; `Hook.ModifyDamage`; final power deltas and live Vulnerable multiplier | Compute actual HP contribution, then allocate by per-enemy cumulative Vulnerable weights | Applying player/pet-owner NetId; attacker self-share discarded | Exact event pool; proportional ownership convention for merged duration |
| Damage Prevented | Same damage pipeline; `WeakPower.ModifyDamageMultiplicative`; command result scope | Sum every target's pre-Block prevention, allocate once by cumulative Weak weights, split self/teammates | Applying player/pet-owner NetId; target subtotals retained for self split | Exact command pool; proportional ownership convention for merged duration |
| Strength-assisted damage | Action snapshots; final `StrengthPower` changes/restorations; resolved damage scope | Track signed non-self events by target/source/lifetime; allocate exact post-Block/HP marginal damage per hit | Responsible player action/source NetId; attacker self change omitted | Exact for supported powered-attack pipeline; ambiguous modifiers fail closed |
| Strength-assisted prevention | Same Strength ledger and resolved enemy damage scope | Exclude current protected player's events; allocate signed pre-Block marginal prevention per target/hit | Event impactor NetId; self-protection omitted per target | Exact for supported powered-attack pipeline; Weak layer separated |
| Gold Earned | `PlayerCmd.GainGold`; `Hook.AfterModifyingGoldGained`; `PlayerMapPointHistoryEntry.GoldGained` | Prefer saved per-room field and/or patch final positive delta; exclude `wasStolenBack` | Explicit `Player.NetId`; reward operations synchronized | High |
| Gold Spent | `PlayerCmd.LoseGold(... GoldLossType)`; `Hook.AfterItemPurchased`; saved `GoldSpent` | Count final removed amount only when type is `Spent`; history is source of truth | Explicit player NetId | High |
| Cards Obtained | `CardPileCmd.Add` permanent-deck path; saved `PlayerMapPointHistoryEntry.CardsGained` | Aggregate saved `CardsGained`; exclude combat-generated paths | Card owner NetId; reward synchronizer carries player | High |
| Cards Upgraded | `CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle)`; saved `UpgradedCards` | Aggregate successfully recorded permanent upgrades | Card owner NetId | High |
| Cards Removed | `CardPileCmd.RemoveFromDeck`; `Hook.BeforeCardRemoved`; saved `CardsRemoved` | Aggregate saved removals; hook/patch provides live notification | Card owner NetId before removal | High |
| Relics Obtained | `RelicCmd.Obtain`; `Player.RelicObtained`; saved `RelicChoices` | Count successful non-silent acquisitions; exclude starting population | Explicit player owner NetId | High |
| Potions Obtained | `PotionCmd.TryToProcure`; `Hook.AfterPotionProcured`; `Player.PotionProcured`; saved `PotionChoices` | Count only successful procurement | Potion owner NetId | High |
| Potions Used | `PotionModel.Use` pipeline; `Hook.AfterPotionUsed`; saved `PotionUsed` | Count completed use after effect | Potion owner NetId | High |
| Most Played Card | `Hook.AfterCardPlayed` | Maximum per-card completed-play count; deterministic tie rule must be approved/documented | Per owner NetId | High (optional) |
| Damage/Healing by Card | damage hook `cardSource`; healing requires source context around `CreatureCmd.Heal` | Count only when source is explicit; otherwise leave unattributed | Card owner NetId | Damage high where present; healing partial (optional) |
| Relic/Potion contributions | damage/heal source context is not uniformly carried into `DamageResult`/heal hooks | Only implement per known reliable provenance path; never infer from timing alone | Owner NetId | Partial/optional |

## Damage and assisted pipeline findings

`CreatureCmd.Damage` is the central resolution path:

1. `Hook.ModifyDamage` applies card enchantment effects, then all listeners' additive modifiers, multiplicative modifiers, and caps, returning the modifier models that changed the value.
2. `Hook.BeforeDamageReceived` fires.
3. `Creature.DamageBlockInternal` removes Block and truncates decimal values to integer changes.
4. `Hook.ModifyHpLost` runs in BeforeOsty and AfterOsty phases and may redirect the unblocked target.
5. `Creature.LoseHpInternal` truncates to `int`, caps HP at zero, and returns `DamageResult` with blocked, unblocked, overkill, and killed fields.
6. History and `AfterDamageGiven`/`AfterDamageReceived` hooks run.

`VulnerablePower` is a target-owned debuff and returns a 1.5 multiplicative modifier for powered attacks. `WeakPower` is a dealer-owned debuff and returns 0.75 for powered attacks. Both can be altered by relics/powers (for example Paper Phrog/Krane and Debilitate), which is why hard-coded 1.5/0.75 arithmetic is prohibited. Counterfactuals must use the live pipeline and current modifiers. Modifier ordering and integer truncation mean contributions are not safely derivable from card text.

## Multiplayer architecture

- `Player.NetId` is a stable `ulong`, serialized in `SerializablePlayer`; `RunState.GetPlayer(netId)` and `CombatState.GetPlayer(netId)` resolve it. `LocalContext.NetId` identifies only the local player and must not be used as the actor for all events.
- `INetGameService.Type` distinguishes `Singleplayer`, `Host`, and `Client`. The host orders actions. Clients request enqueue; the host broadcasts reliable `ActionEnqueuedMessage`s; every peer reconstructs and executes the same `GameAction`. Hook game actions likewise run on all peers. Therefore ordinary counters can be independently derived on all peers without sending a network message per hit.
- RunStats must update once per resolved synchronized event on every peer, keyed by the event's actual owner/dealer/receiver, not local player. Never both derive an event and apply a received delta for the same event.
- The release build is client-optional: the dormant RunStats `INetMessage` implementations have been removed, it sends no custom traffic, and it declares `affects_gameplay: false`, allowing unmodded friends to join.
- Every peer with RunStats independently derives synchronized events and persists its own snapshot. Cross-peer reconciliation after disconnect/rejoin is deliberately not claimed.
- `NetFullCombatState.PowerState` serializes only power ID and amount, not contribution history. Weighted ledgers therefore start fresh when combat state cannot be reconstructed; RunStats never guesses historical contributors.

## Save architecture

- Do not add fields to or rewrite vanilla `current_run.save` / `current_run_mp.save`.
- Reuse serialized `RunState.MapPointHistory` as the persisted source/validation baseline for fields STS2 already records per NetId: Damage Taken, HP Healed, Max HP Gained, Gold Gained/Spent, Cards Gained/Removed/Upgraded, Relic Choices, Potion Choices, and Potions Used.
- Persist RunStats-only fields (damage dealt, block totals, kills, cards played/card counts, assists, revisions/ambiguity diagnostics) in atomic JSON sidecars under each active profile's `com.bradbeise.runstats` directory. Single-player and multiplayer use separate active/pending files, with completed sidecars retained under `archive`. The profile already contains `modded\profile1\RunStatsCollector`; RunStats must neither reuse nor modify that directory. It must also avoid the existing `mod_data` namespaces. This modifies only newly created RunStats-owned user data, never vanilla or other-mod saves.
- Identify a run with a composite including `RunState.Rng.StringSeed`, game mode, serialized player NetIds, profile ID, and run start time. `RunManager` preserves start time through `SerializableRun.StartTime`; it is private at runtime, so load/start patches must capture it from the `SerializableRun` or generated `RunManager.ToSave` identity. Do not rely on seed alone.
- Subscribe to `RunManager.RunStarted`; patch or subscribe around `RunManager.SetUpSavedSingleplayer`, `SetUpSavedMultiplayer`, `RunManager.OnEnded`, and `RunManager.CleanUp` as needed. Subscribe to `SaveManager.Saved` so sidecar checkpoints correspond to successful vanilla run saves; also atomically checkpoint after stat mutations with debouncing or at safe action/room boundaries.
- On load, validate schema/run identity, then merge vanilla-history-backed totals with RunStats-only sidecar fields. On corruption/mismatch, log and fall back safely without touching the vanilla save. Each installed peer restores its own matching sidecar; no custom RunStats messages are sent.
- v0.2.1 advances snapshots and sidecars to schema 3 so Assisted Damage and Assisted Damage Prevented can be signed. Complete schema-2 documents migrate every nonnegative total unchanged. Complete schema-1 documents additionally initialize Poison Applied and the three poison diagnostics to zero. Legacy schemas reject negatives; schema 3 allows them only for the two assisted statistics. Combat-only Poison, Weak, Vulnerable, Accelerant, and Strength attribution state is never serialized. Valid legacy `assisted_ownership` records are accepted only so totals restore, then logged and discarded; new sidecars emit an empty collection.
- On new run, create a new in-memory state. On abandon/death/victory, archive only the matching RunStats sidecar after final state handling. Archives are retained indefinitely as the conservative, non-destructive default.

## UI architecture

- `NRun.Instance.GlobalUi.TopBar` is the run-level top bar. `NTopBar._Ready` binds `%Map`, `%Deck`, `%PauseButton`, potion, room/floor/boss icons, gold, HP, portrait, and timer. The observed stable tree includes `/root/Game/RootSceneContainer/Run/GlobalUi/TopBar/RightAlignedStuff/Options`.
- Inject one RunStats-owned `STATS` control after `NTopBar._Ready` (Harmony postfix), preferably adjacent to the right-aligned options/pause controls. Guard by a RunStats-owned node name/group to prevent duplicate injection. Remove automatically with the run tree.
- Implement the statistics view as an `IOverlayScreen` and open it through `NOverlayStack.Instance.Push`. This gives standard backstop, active-screen/focus management, stacking, and map hide/show behavior. Use `NModalContainer` only for true confirmations; it permits only one modal and is not the right primary container.
- Use Godot containers (`MarginContainer`, `PanelContainer`, `VBoxContainer`, `HBoxContainer`, `ScrollContainer`) and game controls/fonts/themes where accessible. Implement controller focus neighbors and close/back behavior through `ActiveScreenContext`/screen context conventions. Single-player uses one column; multiplayer uses player columns and derived team totals; tabs are optional if vertical layout becomes unwieldy.
- The v0.2.0 Damage tab includes Poison Applied immediately after Damage Dealt. Damage Dealt already includes each player's attributed Poison HP damage; Poison Applied measures stacks added, not damage.
- STS2 global UI supports content scale from 1680x1080 through narrow 1680x1260 and wide 2580x1080 bounds. Layout must be container-driven and tested at narrow/default/wide sizes.
- A PCK is likely required for localization and any `.tscn` scene. Pure programmatic C# UI avoids scene-script registration; if a `.tscn` references mod C# scripts, initialize `ScriptManagerBridge.LookupScriptsInAssembly`.

## Recommended architecture and project structure

```text
RunStats/
  RunStats.sln
  src/RunStats/
    RunStats.csproj
    mod_manifest.json
    Main/ModEntry.cs
    Configuration/RunStatsConfig.cs
    Models/RunStatsState.cs
    Models/PlayerStats.cs
    Models/StatDefinitions.cs
    Tracking/CombatTracker.cs
    Tracking/DamageTracker.cs
    Tracking/BlockTracker.cs
    Tracking/KillTracker.cs
    Tracking/CardTracker.cs
    Tracking/EconomyItemTracker.cs
    Tracking/AssistedDamageTracker.cs
    Tracking/PowerAttributionLedger.cs
    Integration/Hooks/
    Integration/Patches/
    Multiplayer/PlayerResolver.cs
    Multiplayer/StatsSync.cs
    Multiplayer/Messages/
    Save/RunIdentity.cs
    Save/RunStatsSaveStore.cs
    UI/StatsButton.cs
    UI/StatsOverlay.cs
    UI/PlayerStatsPanel.cs
    UI/StatRow.cs
    RunStats/localization/eng/
  tests/RunStats.Tests/
  tools/Deploy-RunStats.ps1
  README.md
```

Keep pure models/calculation/persistence tests independent of STS2 where possible. Isolate all game-version-sensitive code under `Integration`. One central event aggregator owns deduplication and emits typed stat mutations. Persistence and networking carry versioned DTOs, not live game objects.

## Dependencies, build, and deployment

- Required compile/runtime references: game `sts2.dll`; Godot 4.5.1/.NET SDK support (`Godot.NET.Sdk/4.5.1` if using Godot UI types); game-bundled Harmony 2.4.2 (`0Harmony.dll`, `Private=false`). Use `System.Text.Json` from the framework.
- No BaseLib or third-party runtime package is currently required. Do not add one without a demonstrated need.
- Target `net9.0`. Build in this repository, initially with deployment disabled. Validate then `dotnet build`; build a PCK only for localization/scenes/assets.
- Local development destination: `D:\Steam\steamapps\common\Slay the Spire 2\mods\RunStats\` (created and verified in Stage 1).
- Exact Debug deployment allowlist after the Stage 1 load test: `RunStats.dll`, `RunStats.pdb`, `mod_manifest.json`, and `runstats.pck`. `RunStats.deps.json` and `RunStats.runtimeconfig.json` remain workspace build outputs but must not be deployed because STS2 scans every JSON in a mod folder as a manifest. Game-bundled dependencies are references only and are not copied.
- Deployment tooling must resolve and verify absolute paths, verify STS2 is not running before replacing binaries, compare the target inventory for conflicts, and copy/remove only an explicit allowlist inside the dedicated RunStats directory. Never clean the mods directory. The first-deployment approval gate was fulfilled in Stage 1.

## Stage 1 deployment record

- **Destination:** `D:\Steam\steamapps\common\Slay the Spire 2\mods\RunStats\` was created after approval. No other local-mod directory was created or changed.
- **Current inventory:** the game-local `mods` directory now contains only the dedicated RunStats folder. Separately, Steam Workshop has BaseLib, Minty Spire 2, and Import Vanilla Saves installed. Their IDs and paths do not conflict with the RunStats ID or local destination.
- **Artifacts deployed:**
  - `src\RunStats\bin\Debug\net9.0\RunStats.dll` (6,144 bytes)
  - `src\RunStats\bin\Debug\net9.0\RunStats.pdb` (11,384 bytes)
  - `src\RunStats\mod_manifest.json` (308 bytes)
  - `src\RunStats\runstats.pck` (1,091 bytes; nine empty localization tables)
- **Corrections during verification:** the first copy included `RunStats.deps.json` and `RunStats.runtimeconfig.json`. STS2 treated each JSON file as a manifest and logged missing-ID errors. After closing the game, those two RunStats-owned files were removed, the deployment allowlist was narrowed to four files, `min_game_version: 0.107.1` was added, and assembly/manifest versions were aligned at 0.1.0. No unrelated file was removed or replaced.
- **Safety checks:** the script resolves and verifies the game root via `release_info.json`, refuses deployment while `SlayTheSpire2` is running, copies only the four allowlisted files, never touches Workshop content or AppData, and refuses unexpected files in its dedicated destination.
- **Load verification:** final startup loaded `mod_manifest.json`, `RunStats.dll` version 0.1.0.0, and `runstats.pck`; called `RunStats.Main.ModEntry`; logged both `[RunStats]` initialization messages; and finished initialization with no RunStats warnings or exceptions. The game remained responsive at its main window and was closed without loading a run. Existing progress-parse warnings for removed/unknown content are unrelated to RunStats.
- **Integrity verification:** deployed hashes match workspace artifacts. The modded single-player save, multiplayer save, and both backups retained their exact pre-launch SHA-256 hashes. STS2 is no longer running.

## Stage 2 implementation record

- Added a canonical immutable `RunIdentity` containing seed, mode, profile ID, UTC start time, and sorted distinct player NetIds. Equality is structural, so peers that observe players in different orders still identify the same run.
- Added lifecycle states `Empty`, `Active`, and `Ended`. Starting a run fully replaces prior players, totals, diagnostics, identity, and revision; ending prevents further mutations; clearing returns to an empty state.
- Added all required per-player statistic kinds plus internal diagnostic kinds. State is keyed exclusively by `ulong` NetId; there is no local-player shortcut.
- Added typed positive mutations, including an atomic card-play mutation that updates both the total and exact ordinal card-ID count. Unknown players, inactive runs, invalid kinds/amounts/sources, and numeric overflow fail closed without advancing revisions or partially applying data.
- Added independent player revisions and a run-wide monotonic revision. Every accepted statistic or diagnostic change advances the run revision exactly once.
- Added detached read-only schema-v1 snapshots for future persistence/networking, with stable zero entries, per-player lookup, diagnostic lookup, and team totals derived from player totals.
- Added a dependency-free executable unit-test project so model tests require no external test framework or packages. A repository-local NuGet configuration clears package sources for deterministic offline restore.
- No STS2 lifecycle/combat hook, save write, network message, UI, Workshop artifact, or run load was added in this stage. The tested Debug DLL was copied to the already approved dedicated local RunStats directory; its deployed SHA-256 matches the workspace build.

## Stage 3 implementation record

- Added run lifecycle integration through `RunManager.RunStarted`, `RunManager.OnEnded`, and `RunManager.CleanUp`. Each non-Replay run starts a canonical state from the seed, profile, UTC start time, mode, and all player NetIds; cleanup detaches every creature event and clears all transient state.
- Added player-creature subscriptions for actual `BlockChanged`, `CurrentHpChanged`, and `MaxHpChanged` deltas. A scoped `CreatureCmd.GainMaxHp` patch suppresses its accompanying HP increase from Healing Done while still recording the actual positive maximum-HP delta.
- Added `Hook.AfterDamageGiven` and `Hook.AfterDamageReceived` patches. Damage Dealt and Damage Taken use `DamageResult.UnblockedDamage`, which is post-Block and capped to actual HP removed. Dealer attribution resolves direct players and pet owners; non-player dealers are ignored and null-source indirect damage is deliberately uncredited with an internal diagnostic.
- Added reference-identity kill deduplication and current-room classification for reliably attributed enemy, elite, and boss lethal damage. Forced, environmental, or deferred deaths without player provenance remain uncredited.
- Extracted a game-independent `CoreCombatTracker` used by the runtime so resolved event semantics can be tested without constructing STS2 live objects. Tracking is per NetId and does not use local-player identity, preserving deterministic peer-local derivation for later multiplayer reconciliation.
- Every integration entry point fails closed and logs its first failure without affecting gameplay. No per-frame polling, persistence, networking message, UI, assisted statistic, card/economy/item tracking, Workshop artifact, or vanilla-save write was added.
- The tested Debug artifact was deployed locally with SHA-256 `7EB9F9D177B6CC4F56A623A959170A798B0FDCB64744635D6EAC1B5B4CA64CB1`; the source and deployed hashes match. The destination contains exactly the four approved artifacts.

## Stage 4 implementation record

- Added a reference-identity contributor ledger for standard non-instanced `VulnerablePower` and `WeakPower`. The power's original `Applier` establishes ownership; effective positive `PowerCmd.ModifyAmount` applications from the same player preserve it, while a different or unknown contributor makes the instance permanently ambiguous until removal/reset.
- Added an async-local scope around the exact central `CreatureCmd.Damage(PlayerChoiceContext, IEnumerable<Creature>, decimal, ValueProp, Creature?, CardModel?)` overload. Its `Hook.ModifyDamage` observation is therefore limited to real damage resolution rather than card previews.
- Assisted Damage supports uniquely teammate-owned Vulnerable on an enemy damaged by a player or player-owned pet. The implementation captures STS2's live Vulnerable multiplier, including Paper Phrog, Cruelty, and Debilitate adjustments, algebraically removes that multiplier, then compares actual HP removed with the counterfactual after the target's pre-hit Block, integer truncation, and HP/overkill cap.
- Assisted Damage Prevented supports uniquely teammate-owned Weak on an enemy attacking a player. It captures the live Weak multiplier, including Paper Krane and Debilitate adjustments, and compares actual versus counterfactual integer incoming damage before defender Block, powers, relics, and other mitigation.
- The counterfactual never invokes the full hook pipeline a second time and never mutates a creature, power, Block, HP, RNG, or game action. Multipliers commute within STS2's ordered additive/multiplicative/cap pipeline. If a damage-cap override participated, the event is omitted. Assisted Damage is also omitted whenever an active hook listener overrides HP-loss or damage-redirection stages, because algebraic inversion alone cannot prove the final HP contribution there.
- Self-benefit, unknown ownership, and zero-contribution rounding/Block/overkill cases receive no assist. Ambiguous merged ownership receives no assist and increments the appropriate internal ambiguity diagnostic.
- Peer-local processing uses actual actor, beneficiary, and contributor NetIds rather than `LocalContext`. Host/client reconciliation remains Stage 6 work; persistence/rejoin reconstruction remains Stage 8 work.
- No card/economy/item tracking, network message, persistence, UI, Workshop artifact, or vanilla-save write was added. The tested local Debug DLL SHA-256 is `B9D040104C2D61DD068E16CAD18938F7B888200D43683AA14E6E05894531974F`; it matches the deployed DLL, and the destination still contains exactly four allowlisted artifacts.

## Stage 5 implementation record

- Added `RunProgressTracker`, the pure accumulator used by all Stage 5 integration paths. Every mutation remains keyed by the event's actual `Player.NetId`; zero/negative deltas and inactive/unknown players fail closed through the existing state model.
- Cards Played patches `Hook.AfterCardPlayed`, which fires after each completed `CardPlay`. Manual play, autoplay, and every card Replay execution count independently. Canonical `ModelId.ToString()` values feed the existing per-card count map.
- Most Played Card is derived from per-card counts: highest count wins, with ordinal ascending canonical card ID as the deterministic tie rule. No additional mutable total is stored.
- Cards Obtained wraps the central `CardPileCmd.Add(IEnumerable<CardModel>, CardPile, ...)` result and counts only successful additions whose destination is the permanent `PileType.Deck`. Generated/moved combat-pile cards and prevented/failed additions are excluded.
- Cards Upgraded wraps the central bulk `CardCmd.Upgrade` overload, snapshots permanent-deck upgrade levels, and records only actual positive level deltas. Combat-only card upgrades are excluded.
- Cards Removed wraps the central bulk `CardPileCmd.RemoveFromDeck` task and records reference-distinct cards that actually reached `HasBeenRemovedFromState`, including successfully completed entries before a later bulk-operation fault.
- Gold Earned wraps `PlayerCmd.GainGold` and records the actual positive player-balance delta after game modifiers. Returned stolen gold (`wasStolenBack`) is excluded. Gold Spent wraps `PlayerCmd.LoseGold` and records the actual balance decrease only for `GoldLossType.Spent`; ordinary loss and theft are excluded.
- Relics Obtained and Potions Obtained subscribe to `Player.RelicObtained` and `Player.PotionProcured` after `RunStarted`. Starting inventory is already populated before that event; silent load/setup and failed potion procurement do not emit these events. Potion use patches `Hook.AfterPotionUsed`, after the effect has completed; discards do not count.
- STS2's per-map-point `CardsGained`, `CardsRemoved`, `UpgradedCards`, `GoldGained`, `GoldSpent`, `RelicChoices`, `PotionChoices`, and `PotionUsed` remain the future load/reconciliation baseline. Stage 5 does not read or write saves, so Continue Run totals before the current process are not reconstructed yet.
- Optional Damage/Healing by Card and relic/potion contribution breakdowns were not implemented. Damage has reliable `cardSource` only on some paths; healing and item provenance are not uniformly carried. Partial labels would be misleading without a dedicated attributed/unattributed presentation.
- No network message, persistence sidecar, UI, Workshop artifact, or vanilla-save write was added. The tested local Debug DLL SHA-256 is `208B3AD9CEA99EAD26D0E7EEAAE886866A7ADC3EC43856EA4CE5A050EB2A3086`; it matches the deployed DLL, and the destination still contains exactly four allowlisted artifacts.

## Stage 6 implementation record

- Ordinary combat, cards, economy, and item events still mutate peer-local state exactly once and are keyed only by the event actor/owner/receiver/contributor `Player.NetId`. No per-hit or per-stat delta messages were added, preventing network echo double-counting.
- Added public mod-discovered `StatsSnapshotRequestMessage` and `StatsSnapshotMessage` implementations using the installed `INetMessage`/`PacketWriter`/`PacketReader` API and `NetTransferMode.Reliable`. The wire envelope has protocol version 1, host sequence, request sequence, snapshot schema/revision, full structural run identity, every player total/card count, every diagnostic, and bounded assisted ownership records.
- Clients request an authoritative snapshot when `RunStarted` completes, which covers fresh joins and rejoin reconstruction. Hosts answer only known run-player senders with a new request sequence and broadcast low-frequency checkpoints after map-location changes. Host request duplicates/stale sequences are suppressed.
- Clients accept packets only from the concrete connection's `HostNetId`. Protocol mismatch, nonpositive or stale/duplicate host sequence, run identity mismatch, missing/unknown players, player/key mismatch, negative/overflowed/missing counters, card-play-total mismatch, malformed lifecycle/revision, duplicate assisted keys, unknown assisted contributors, and oversized assisted metadata all fail closed.
- Snapshot replacement is atomic and may repair either undercount or overcount disagreement; validation builds detached replacement players/diagnostics before changing live state. A rejected packet leaves both state and the last accepted sequence unchanged.
- Active assisted ownership survives host reconciliation despite STS2 `NetFullCombatState.PowerState` omitting `PowerModel.Applier`: host metadata keys standard non-instanced Vulnerable/Weak by stable `Creature.CombatId` and power kind, preserving unique, ambiguous, or unsupported resolution. Missing local matches remain unsupported instead of guessed.
- Kept `affects_gameplay: true`, because STS2's mod message IDs depend on the common discovered message-type set. No persistence sidecar, vanilla-save field, UI, Workshop metadata/package, or release action was added.
- The tested local Debug DLL SHA-256 is `C0B03F628F02B8929BF0F2C1E1A1BE44F9B935776BBB3302C4E096D37E73166F`; it matches the deployed DLL. The destination contains exactly `RunStats.dll`, `RunStats.pdb`, `mod_manifest.json`, and `runstats.pck`.

## Stage 7 implementation record

- Added a duplicate-guarded `STATS` button immediately before the right-aligned Options control through an `NTopBar._Ready` Harmony postfix. It is event-driven through `ActiveScreenContext.Updated`, exists only with the run tree, and is hidden/disabled when no RunStats run is active, the map is open, another overlay is present, or a modal/capstone owns interaction.
- Added a programmatic `IOverlayScreen` opened through `NOverlayStack.Instance.Push`, using the shared backstop and standard active-screen lifecycle. The screen closes by mouse/button or STS2's `cancel`, `pauseAndBack`, and `back` actions, restores focus through the overlay stack, and connects the custom top-bar control into the neighboring controller focus chain.
- The container-driven screen uses a centered styled panel, title/seed/revision header, two-axis `ScrollContainer`, responsive grid, alternating rows, and close control. Single-player has one player column. Multiplayer orders columns by canonical run identity, adds a derived Team column, and safely displays an em dash if a derived sum overflows.
- All 20 stored statistics plus derived Most Played Card are shown. Team Most Played Card aggregates card counts across players and uses the existing ordinal card-ID tie rule. Values use invariant thousands separators.
- Added pure view-model/layout tests covering inactive rejection, complete 21-row single-player output, canonical multiplayer ordering, team totals, deterministic number formatting, team card aggregation/ties, overflow suppression, and exact panel sizing at 1680x1080, 1680x1260, and 2580x1080.
- Debug and Release builds pass with 0 warnings and 0 errors; all 53 dependency-free tests pass in both configurations; mod validation passes with 0 errors and 0 warnings. The final Debug DLL SHA-256 is `0524A5F2F30019139D11AD0D5C169C33FBE25E98ED8F466A5F2F7D5A28149975`, it matches the deployed DLL, and the destination contains exactly the four allowlisted artifacts.
- The immediately preceding UI build loaded from the real local mod folder, invoked `RunStats.Main.ModEntry`, initialized successfully, and was present in the responsive process with Harmony loaded. The final change only extracted the already-used size formula into a pure tested helper, but the final DLL was not relaunched. The UI itself was not instantiated because no disposable active run was available; live mouse/controller/keyboard behavior and visual inspection at the three sizes remain explicitly unclaimed.
- **Save-integrity clarification:** before that menu session, profile1's `current_run.save` and `.backup` matched baseline SHA-256 values `19917C...68E0` and `862909...3CA6`. During the session the game recorded `Abandoning run from main menu`, created abandoned history record `1788224854.run` for seed `4V5Z5MATHL`, and deleted that single-player save pair. The user subsequently confirmed that they intentionally abandoned this run. The multiplayer pair remained byte-identical. No restoration was attempted, and future automated live tests still require an isolated disposable profile plus pre-created external backups.

## Stage 8 implementation record

- Added schema-versioned, deterministic RunStats sidecars containing composite run identity, the complete detached statistics snapshot, card counts, diagnostics, and active Vulnerable/Weak ownership metadata. Decoding rejects unknown fields, incompatible schemas, invalid snapshots, oversized input, run-identity mismatches, and vanilla-save checkpoint mismatches.
- Added separate single-player and multiplayer `active_*.json` and `pending_*.json` files beneath the active profile's `com.bradbeise.runstats` directory. Debounced pending snapshots capture mutations but are deliberately non-restorable. A prepared snapshot becomes authoritative only after `SaveManager.Saved` confirms a successful vanilla save.
- Added atomic sidecar writes using a flushed temporary file and same-directory replacement. New-run/load/end/delete lifecycle integration restores only an exact active checkpoint, archives only matching RunStats-owned state, and never rewrites the vanilla save schema or files. Completed archives are retained indefinitely as a conservative default.
- Added vanilla map-history extraction and an atomic maximum merge for statistics already recorded by STS2. This repairs a sidecar that trails vanilla history without double-counting or partially mutating state on validation/overflow failure. Single-player and hosts restore locally; multiplayer clients receive the host-authoritative snapshot.
- Added 12 persistence-focused tests covering full deterministic round trips, single-player/multiplayer separation, non-restorable pending files, malformed/schema/identity/checkpoint/size rejection, corrupt-write safety, vanilla maximum merging and overflow atomicity, scoped archival, successful-save promotion, debouncing, and assisted-ownership validation/round trip.
- Debug and Release builds pass with 0 warnings and 0 errors; all 65 dependency-free tests pass in both configurations; mod validation passes with 0 errors and 0 warnings. The final Debug DLL SHA-256 is `3CCABA0C55F531DB10C345399FECBD2039525F19827CC9F7E793E3CA89E6F99F`, it matches the deployed DLL, and the destination contains exactly the four allowlisted artifacts.
- The final DLL loaded at the real Steam menu, invoked `RunStats.Main.ModEntry`, and initialized successfully with no RunStats exception. A menu-only launch created no RunStats profile directory. The protected multiplayer save/backup and all three pre-existing `RunStatsCollector` files retained their exact baseline SHA-256 hashes. Godot emitted only its unrelated resource-leak diagnostics during shutdown.
- No live run save/quit/continue or host/client persistence cycle is claimed. The user's prior single-player run was intentionally abandoned, and profile1's surviving multiplayer save remains protected. Live lifecycle validation requires an isolated disposable run/profile and external backup procedure in Stage 9.

## Stage 9 implementation record

- Created the external recovery set `C:\Users\Brad Beise\Documents\RunStats-Stage9-Backup-20260906` before live testing. The existing multiplayer save/backup, progress save/backup, and three `RunStatsCollector` files were protected by SHA-256 baselines throughout the test.
- Exercised a real single-player run, representative combat/progression mutations, Run Statistics opening/closing, vanilla save, exit, and Continue. The log confirms exact sidecar restoration at vanilla checkpoint `1788787365`; the user confirmed the continued run and overlay remained functional.
- Fixed three live-only integration defects found during the pass: profile-scoped `user://` paths are globalized before filesystem use; saved-run setup tolerates the network service not yet being installed; and the custom overlay builds from direct `IOverlayScreen` lifecycle callbacks instead of relying on unavailable generated callbacks.
- Replaced the text-like top-bar control with a compact chart icon, official-style transparent/hover/pressed states, and a short hover tween. The final overlay uses tabs for Damage, Healing / Block, Kills, Cards, Economy, Relics, and Potions; platform usernames label player columns; multiplayer retains a Team column.
- Most Played Card remains deterministically derived from stored canonical IDs but is rendered with STS2's native pooled card/holder UI after its Ready signal. The user confirmed the final card name, art, cost, and description display correctly without requiring vertical scrolling at the tested resolution.
- Corrected vanilla-history reconciliation so the Ancient/Neow opening record does not count initial HP as Healing Done. The affected RunStats-owned test sidecar was backed up separately and repaired from 86 to the actual 6; the vanilla single-player save was not edited.
- Live observed totals at the tested checkpoint included Damage Dealt 52, Damage Taken 14, Healing Done 6, Block Gained/Lost 5/5, Enemies Killed 2, Cards Played 7, Gold Earned 13, Cards Obtained 1, and Relics Obtained 1. Most Played Card correctly resolved to Ironclad Strike from four recorded plays.
- Debug and Release builds pass with 0 warnings and 0 errors; all 67 dependency-free tests pass. The final Debug DLL SHA-256 is `93E9C13FC27D3EDF0A7DCC48533B4076E495AF22E41821D4CB75B86156D1CCB5`, matches the installed DLL, and the destination contains exactly `RunStats.dll`, `RunStats.pdb`, `mod_manifest.json`, and `runstats.pck`.
- The final closed-game log contains successful RunStats initialization, sidecar restoration, and active-run tracking with no RunStats warning or exception. STS2's existing progress-data warnings and Godot shutdown resource-leak diagnostics remain unrelated to RunStats behavior.
- The protected multiplayer save and backup retained SHA-256 `0A8C46DBF2C69653C30AD2BD8AA6E24FC3930C8B2B50400F590E6841E5A60C5B` and `D68CF6F526E031BCDFC4DE5B38B9D0409B2AB7D950097117A4C3004C38FB7D0E`. All three `RunStatsCollector` files also retained their recorded baselines.
- A live unmodded-friend compatibility requirement led to the final client-optional design: `affects_gameplay` is false, custom `INetMessage` types are excluded from the release assembly, and each installed peer tracks/persists synchronized game observations locally. Cross-peer reconciliation after disconnect/rejoin is not claimed.

## Stage 10 implementation record

- The user explicitly authorized Workshop preparation and publishing after accepting the completed local feature set.
- Mega Crit's official `sts2-mod-uploader` v0.2.0 Windows x64 package was selected. Its downloaded archive matches the official SHA-256 `2b55c19cc5932235ca9dbd663ca07d04e5d5a0018402303199f9b4ec8ca06578`.
- Prepared `workshop\RunStats` with a private-first `workshop.json`, a generated original 512x512 RunStats thumbnail (735,516 bytes), and a three-file content package containing only `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`.
- The staged Release DLL contains no RunStats custom network-message types, and the staged manifest declares `affects_gameplay: false`, version 0.1.0, and minimum game version 0.107.1.
- After the user explicitly agreed to Steam's Workshop terms, Mega Crit's uploader successfully created Workshop item `3797791393` under Steam account `76561198407892354`, whose current profile name is `LordWildling`. The workspace contains `mod_id.txt` with that ID so all future uploads update the same item. After private-page review and separate user approval, the same item was updated to public visibility. An anonymous web request confirmed the page title `Steam Workshop::RunStats`.
- The user subscribed to the public item and moved the final local Debug test installation outside the game's recursively scanned mod tree to `C:\Users\Brad Beise\Documents\Repos\RunStatsLocalTestModFolder\RunStats`. The original game-local `mods\RunStats` path is absent.
- The subscribed production directory `D:\Steam\steamapps\workshop\content\2868840\3797791393` contains exactly the three staged Release files. Its DLL SHA-256 is `7283EB1E106E160305EF355D67AC711FE80D7BABF9618C34D1BCE4671C6CF070`; the backed-up Debug DLL SHA-256 is `06D3B1B222CE30BABAE848972B365917DEA573118CCB53F7F27CD72AC90B43B0`.
- Added `MAINTENANCE.md` as the operational handoff for future versioning, duplicate-free local testing, Release packaging, Workshop updates, verification, and rollback preparation.

## v0.2.0 PoisonFix phased implementation record

- **Phase 1:** Verified installed STS2 v0.107.1 poison/Accelerant implementation, all vanilla poison source categories, exact central mutation/damage APIs, save timing, Steam library location, disabled Workshop state, and Release prerequisites.
- **Phase 2:** Added the Poison Applied model field, poison diagnostics, per-enemy continuous-cycle contributions, exact fractional allocator, deterministic kill selector, ordered Accelerant sponsorship, assist calculator, and focused tests.
- **Phase 3:** Added narrow power lifecycle and poison sequence/damage hooks. Final power deltas capture modifiers, top-level returned damage is aggregated once, generic dealer-less damage is suppressed only for that poison command, and nested damage remains on its ordinary path. Combat-only state clears on combat/run boundaries.
- **Phase 4:** Added Poison Applied to the Damage UI, advanced snapshot/sidecar schema to 2 with strict schema-1 migration, advanced the dormant fixed-layout snapshot protocol constant to 2, and documented behavior, limits, persistence, rollback, and later deployment gates.
- **Phase 5:** Built and installed the Release candidate locally with the subscribed Workshop copy disabled; verified the three-file installation and completed the initial single-player checklist.
- **Phase 6:** Fixed lethal-poison damage/kill attribution ordering, added one-to-four-contributor regressions, redeployed Release, and passed the remaining single-player and multiplayer playtests. Multi-owner Accelerant ordering remains manually untested; deterministic automated coverage passes.
- **Phase 7:** Synchronized version `0.2.0`, updated Workshop metadata and operational documentation, preserved the published v0.1.0 rollback package, and prepared a verified three-file upload package for existing item `3797791393`. No Workshop upload was performed.
- **Phase 7 package hashes:** `RunStats.dll` `5C6D8FB363E2E38D67B392500AFCBDEE324D69B5C5975C6BCBF9A26A037D44C0`; `runstats.pck` `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`; `mod_manifest.json` `F4D3A9D3C0F06DAE6DA8CC6B9223763AE0B0BAEBD2BF50D0EB75D67CD36EE7C9`.
- **Phase 8:** Changed Block Lost to use enemy-sourced `DamageResult.BlockedDamage`, excluding turn clearing and non-enemy reductions. Debug and Release builds passed with 0 warnings/errors, and all 84/84 tests passed in both configurations. The corrected change note covering both v0.2.0 features was pushed before publication.
- **Published package:** On 2026-09-09, Mega Crit's uploader updated the existing public item `3797791393` under `LordWildling`; no duplicate item was created. Steam's public API returned success, public visibility, file size 158,085 bytes, and the updated poison and Block Lost description. The public change-notes page contained both release-note entries. Published hashes are `RunStats.dll` `E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`, `runstats.pck` `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, and `mod_manifest.json` `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974`.

## v0.2.1 WeakVulnTweaks phased implementation record

- **Phase 1:** Reverified STS2 v0.107.1 power and damage ordering; added a checked weighted cycle ledger with reverse-application remainder rotation, unattributed shares, zero refresh, and self-exclusion tests. Decided to retain snapshot/sidecar schema 2 and discard rather than reinterpret legacy ownership metadata.
- **Phase 2:** Connected final Weak/Vulnerable power deltas to independent per-enemy/effect ledgers. Positive final deltas add cumulative weight, nonzero reductions preserve it, explicit removal/zero clears it, and combat/run boundaries reset all combat-only state.
- **Phase 3:** Connected Vulnerable's existing Block/HP/overkill-aware integer assist pool to proportional allocation. Attacker self-shares and unattributed shares are discarded; lethal results are attributed before cleanup.
- **Phase 4:** Expanded Weak to every attacked player, summing one command-level prevention pool before one weighted allocation. Each contributor's gross award is split between self and teammates; exact fractional self-split carry persists until zero. Damage-command scopes defer lethal/reactive cleanup until final rows are processed.
- **Phase 5:** Retained schema 2, removed live unique/ambiguous ownership persistence, preserved strict legacy decoding and accumulated totals, made all new sidecars emit empty `assisted_ownership`, removed dormant custom network-message sources, and updated documentation. No installation or Workshop mutation occurred.
- **Automated evidence:** 104 dependency-free tests cover old behavior plus proportional one-to-four contributors/targets, aggregation-before-rounding, reverse remainder rotation, unknown shares, self exclusion and carry, zero refresh, lethal ordering, persistence compatibility, Poison regressions, unchanged UI, and absence of custom network messages. Debug and Release builds pass with zero warnings/errors.

## v0.2.1 Strength phased implementation record

- **Phase 1:** Reverified STS2 v0.107.1 Strength and damage mechanics; added the signed combat-local event/lifetime/allocation model.
- **Phase 2:** Connected action snapshots, direct/reactive/delayed ownership, temporary restoration, reset, and cleanup observation without damage awards.
- **Phase 3:** Added exact outgoing per-hit Strength counterfactuals with Block, HP, lethal/overkill, Weak scaling, Vulnerable non-overlap, and harmful-first earliest-event allocation.
- **Phase 4:** Added incoming pre-Block counterfactuals with per-target self exclusion, Weak non-overlap, downstream Vulnerable scaling, and signed harmful-first allocation.
- **Phase 5:** Connected atomic signed awards, limited signed storage to the two assisted statistics, and advanced sidecars to schema 3 with strict schema-1/2 migration.
- **Phase 6:** Updated release/migration/Workshop-draft documentation and performed the complete Debug/Release and package-invariant review without installation or upload.
- **Automated evidence:** 137 dependency-free tests cover all prior functionality plus Strength ownership/lifetimes, signed ordering, outgoing and incoming boundaries, one-to-four-player/target behavior, schema migration, signed save/continue, UI formatting, atomic overflow, peer-local determinism, and absence of custom network messages.

## Performance and error handling

- Event/hook subscriptions and scoped Harmony patches only; no `_Process` polling or repeated reflection.
- Maintain O(1) per-player counters and small per-combat attribution dictionaries. Counterfactual damage calculations run only when qualifying teammate-owned damage modifiers are present.
- Sidecar writes are atomic (`.tmp` + replace) and debounced/checkpointed, never performed synchronously per animation frame.
- Optional source breakdowns and assisted cases fail closed: log once/rate-limit, increment diagnostics, and omit uncertain credit. `EnableDebugLogging` defaults false.

## Testing requirements and current results

- Unit-test stat definitions, actual-delta/overkill/block calculations, serialization/migration, deduplication, run identity, counterfactual rounding, proportional contributors, unattributed shares, zero refresh, and self-assist exclusion.
- Integration-test each hook using `Statistic | Hook | Scenario | Expected | Actual | Status`.
- Assisted test tables must include the exact columns requested in the project brief. Cover basic teammate, self exclusion, multi-hit, overkill/block, multiple effects/owners, refresh/stack, rounding, synchronization, save/load, and rejoin.
- Test single-player and at least host + one client; confirm identical revisions/totals and no double application. Test combat, map, events, shop, rest, rewards, acts, new/abandon/death/victory, save/quit/continue, UI scaling/input, exceptions/log volume, and performance.
- **Stage 0 tests/checks completed:** MCP setup/game info; direct `release_info.json` and Steam appmanifest verification; read-only installation/mod inventory; decompiled code/index queries for hooks, commands, run/player identity, damage/powers, save/history, networking, and UI; Bridge/GodotExplorer availability checks. At that stage no buildable code existed, no game process was running, and no live playtest was performed.
- **Stage 1 tests/checks completed:** project validation passes with 0 errors and 0 warnings; Debug solution build passes with 0 errors and 0 warnings; PCK build passes with nine files and no warnings; deployment `WhatIf` passes; first deployment and corrected redeployment contain exactly four allowlisted artifacts; the final menu-only startup loads and initializes RunStats 0.1.0 without RunStats warnings/errors; source/deployed artifact hashes match; all four protected active-run/save-backup hashes are unchanged; STS2 exited successfully. No gameplay/run test was performed because Stage 1 implements no statistic model or tracking.
- **Stage 2 tests/checks completed:** Debug and Release solution builds each pass with 0 warnings and 0 errors. The same 10 dependency-free tests pass in both configurations: canonical/structural run identity, zeroed isolated players, NetId-scoped mutations, atomic card totals/counts, fail-closed invalid mutations, fail-closed overflow, detached snapshots, diagnostic/revision behavior, deterministic end/reset/new-run lifecycle, and derived team totals. Mod validation passes with 0 errors and 0 warnings. The deployed Debug DLL hash matches the tested workspace artifact; STS2 remains stopped and saves were not accessed or modified during Stage 2.
- **Stage 3 tests/checks completed:** Debug and Release builds pass with 0 warnings and 0 errors; all 20 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. A menu-only startup loaded the exact deployed DLL/PCK, invoked `RunStats.Main.ModEntry`, and logged successful RunStats initialization with no RunStats/Harmony warning or exception. The game remained responsive and exited normally. The modded single-player save, multiplayer save, and both backups retained their exact baseline SHA-256 hashes before launch, while running at the menu, and after shutdown.
- **Stage 4 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 35 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. The exact deployed Stage 4 DLL/PCK loaded at a responsive real game menu, all Harmony patches were discovered, and RunStats initialization completed without a RunStats/Harmony warning or exception. STS2 exited normally. All four protected active-run/save-backup SHA-256 hashes remained unchanged before and after deployment/load verification.
- **Stage 5 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 41 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. The exact deployed Stage 5 DLL/PCK loaded at a responsive real game menu, all Harmony patches were discovered, and RunStats initialization completed without a RunStats/Harmony warning or exception. STS2 exited normally. All four protected active-run/save-backup SHA-256 hashes remained unchanged before and after deployment/load verification.
- **Stage 6 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 46 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. Tests cover every statistic independently for P1/P2, both assisted totals, divergent-client replacement, host-only authority, protocol/sequence rejection, atomic malformed-snapshot rejection, and P1/P2/ambiguous assisted ownership metadata. The exact deployed DLL/PCK reached the real Steam main menu and initialized RunStats without a RunStats warning or exception; message subtype bootstrap completed without a type-map failure. STS2 was stopped afterward. All four protected active-run/save-backup SHA-256 hashes remained unchanged before and after deployment/load verification. Live two-machine co-op transport/rejoin remains unclaimed because no disposable second Steam peer/profile was available.
- **Stage 7 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 53 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. Tests validate the complete UI projection, deterministic columns/team derivation, overflow handling, and the required three layout bounds. The preceding UI build initialized at the real menu with the exact RunStats DLL loaded, but the overlay was not opened. The multiplayer save and backup stayed unchanged. The user intentionally abandoned the prior single-player run during that session; see the Stage 7 clarification above. No visual/live-input claim is made.
- **Stage 8 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 65 dependency-free tests pass in both configurations; and STS2 mod validation passes with 0 errors and 0 warnings. Persistence tests cover strict codec validation, exact checkpoint/identity matching, SP/MP isolation, non-restorable debounce files, successful-save promotion, atomic write/reconcile failure, scoped archival, vanilla maximum merging, and assisted metadata. The exact deployed DLL initialized successfully at the real menu. That menu-only test created no RunStats data; the protected multiplayer pair and existing `RunStatsCollector` data stayed byte-identical. No live save/continue or co-op claim is made.
- **Stage 9 tests/checks completed:** Debug and Release builds each pass with 0 warnings and 0 errors; all 67 dependency-free tests pass. A real single-player run exercised representative combat/progression tracking, the responsive seven-tab overlay, native Most Played Card rendering, platform username labels, icon hover feedback, save/quit/continue, exact sidecar restoration, and corrected Ancient-history healing. The user visually accepted the final UI. The installed DLL matches SHA-256 `93E9C13FC27D3EDF0A7DCC48533B4076E495AF22E41821D4CB75B86156D1CCB5`; the four-file deployment allowlist and all protected multiplayer/other-mod hashes remain intact. Live two-peer multiplayer remains unclaimed.

| Statistic | Hook | Test | Expected | Actual | Status |
|---|---|---|---:|---:|---|
| Damage Dealt | `Hook.AfterDamageGiven` | Three resolved 4-HP hits by one player | 12 | 12 | Pure scenario passed; runtime patch bound at load |
| Damage Taken | `Hook.AfterDamageReceived` | Fully blocked hit, `UnblockedDamage = 0` | 0 | 0 | Pure scenario passed; runtime patch bound at load |
| Healing Done | `Creature.CurrentHpChanged` | Near-full heal 48→50; max-HP heal 50→55 inside scope | 2 | 2 | Pure scenario passed; event subscription awaits live run |
| Max HP Gained | `Creature.MaxHpChanged` + `CreatureCmd.GainMaxHp` scope | Maximum HP 50→55 | 5 | 5 | Pure scenario passed; Harmony patch bound at load |
| Block Gained | `Creature.BlockChanged` | Block 0→10 | 10 | 10 | Pure scenario passed; event subscription awaits live run |
| Block Lost | `Hook.AfterDamageReceived` | Enemy damage absorbs 6 Block; turn clearing removes the remainder | 6 | 6 | Pure scenario verifies enemy absorption counts and clearing does not |
| Enemies Killed | `Hook.AfterDamageGiven` + lethal `DamageResult` | Same enemy receives duplicate lethal observations | 1 | 1 | Pure scenario passed; runtime patch bound at load |
| Elite Enemies Killed | Damage-given hook + `RoomType.Elite` | One uniquely credited elite kill | 1 | 1 | Pure scenario passed; runtime patch bound at load |
| Bosses Killed | Damage-given hook + `RoomType.Boss` | One uniquely credited boss kill | 1 | 1 | Pure scenario passed; runtime patch bound at load |
| Indirect attribution | Damage-given hook with null dealer | 9 damage/lethal elite event with no provenance | 0 credited; 1 diagnostic | 0 credited; 1 diagnostic | Passed |
| Multiplayer attribution | Same hooks/events keyed by `Player.NetId` | P10 deals 7; P20 deals 3 and takes 6 | P10 7/0; P20 3/6 | P10 7/0; P20 3/6 | Pure scenario passed; live co-op deferred |

The table validates the exact accumulator used by the integration layer and confirms Harmony patch discovery during a real menu load. It does **not** claim that combat hooks or creature subscriptions were exercised in a live run. Loading either existing Continue save could modify protected user data, and starting a new run could replace current-run state; the MCP bridge is not installed. Focused live combat and co-op verification therefore require a disposable profile/run or an explicitly approved backup/restore isolation procedure in a later testing stage.

### Assisted Damage validation

| Debuff | Owner | Attacker | Base Damage | Actual Damage | Expected Assist | Actual Assist | Status |
|---|---|---|---:|---:|---:|---:|---|
| Vulnerable | P1 | P2 | 10 | 15 | 5 | 5 | Pure scenario passed |
| Vulnerable (self) | P1 | P1 | 10 | 15 | 0 | 0 | Self-benefit excluded |
| Vulnerable, three hits | P1 | P2 | 4 x 3 | 6 x 3 | 6 | 6 | Multi-hit passed |
| Vulnerable, target at 4 HP | P1 | P2 | 10 | 4 HP removed | 0 | 0 | Overkill cap passed |
| Vulnerable, target has 12 Block | P1 | P2 | 10 | 3 HP removed | 3 | 3 | Block interaction passed |
| Vulnerable + another 2x multiplier | P1 | P2 | 10 | 30 | 10 | 10 | Multiple effects passed |
| Merged Vulnerable | P1 + P2 | P3 | 10 | 15 | 0 | 0 | Ambiguous owners suppressed |
| Refreshed Vulnerable | P1 + P1 | P2 | 10 | 15 | 5 | 5 | Same-owner refresh passed |
| Vulnerable rounding | P1 | P2 | 7 | 10 | 3 | 3 | Integer rounding passed |
| Vulnerable peer replay | P1 | P2 | mixed | mixed | identical totals/revision | identical | Pure synchronization passed |

### Damage Prevented validation

| Debuff | Owner | Enemy | Protected Player | Base Damage | Modified Damage | Expected Prevented | Actual Prevented | Status |
|---|---|---|---|---:|---:|---:|---:|---|
| Weak | P1 | E1 | P2 | 8 | 6 | 2 | 2 | Pure scenario passed |
| Weak (self-benefit) | P1 | E1 | P1 | 8 | 6 | 0 | 0 | Self-benefit excluded |
| Weak, defender has 8 Block | P1 | E1 | P2 | 8 | 6 | 2 | 2 | Pre-Block separation passed |
| Weak, defender mitigation leaves 0 HP loss | P1 | E1 | P2 | 8 | 6 | 2 | 2 | Defender mitigation excluded |
| Weak, three hits | P1 | E1 | P2 | 8 x 3 | 6 x 3 | 6 | 6 | Multi-hit passed |
| Weak + another 0.5x reducer | P1 | E1 | P2 | 8 | 3 | 1 | 1 | Multiple effects passed |
| Merged Weak | P1 + P2 | E1 | P3 | 8 | 6 | 0 | 0 | Ambiguous owners suppressed |
| Refreshed Weak | P1 + P1 | E1 | P2 | 8 | 6 | 2 | 2 | Same-owner refresh passed |
| Weak rounding | P1 | E1 | P2 | 7 | 5 | 2 | 2 | Integer rounding passed |
| Paper Krane-modified Weak | P1 | E1 | P2 | 10 | 6 | 4 | 4 | Modified-multiplier calculation passed |
| Weak peer replay | P1 | E1 | P2 | mixed | mixed | identical totals/revision | identical | Pure synchronization passed |

These tables exercise the exact pure ledger/calculator used by the runtime and the real menu load proves patch binding. They do **not** claim live combat or host/client execution. The installed MCP bridge is unavailable, and using either current Continue file or creating a new run could change protected current-run state. No save/load/rejoin test is claimed because Stage 4 adds no persistence or reconciliation; those remain explicitly assigned to Stages 6, 8, and 9 with a disposable test profile or approved isolation procedure.

### Stage 5 statistic validation

| Statistic | Hook | Scenario | Expected | Actual | Status |
|---|---|---|---:|---:|---|
| Cards Played | `Hook.AfterCardPlayed` | Manual, autoplay, and one additional Replay execution of one card | 3 total; card count 3 | 3; 3 | Pure scenario passed; patch bound at load |
| Most Played Card | Derived from card-ID counts | BASH and STRIKE tied at 2 | `CARD.BASH` | `CARD.BASH` | Ordinal tie passed |
| Cards Obtained | Central `CardPileCmd.Add` result to `PileType.Deck` | Two successful permanent additions | 2 | 2 | Pure count passed; success filter bound at load |
| Cards Upgraded | Central `CardCmd.Upgrade` level delta | One permanent upgrade | 1 | 1 | Pure count passed; level-delta patch bound at load |
| Cards Removed | Central `CardPileCmd.RemoveFromDeck` completion | One reference-distinct permanent removal | 1 | 1 | Pure count passed; completion patch bound at load |
| Gold Earned | `PlayerCmd.GainGold` actual balance delta | 12 gained, 8 stolen gold returned | 12 | 12 | Passed |
| Gold Spent | `PlayerCmd.LoseGold` + `GoldLossType.Spent` | Spend request exceeds 5 held; separate 10 ordinary loss | 5 | 5 | Passed |
| Relics Obtained | `Player.RelicObtained` | Three post-start successful acquisitions | 3 | 3 | Pure count passed; event subscription bound at load |
| Potions Obtained | `Player.PotionProcured` | Two successful post-start procurements; failed procurement emits no event | 2 | 2 | Pure count passed; event subscription bound at load |
| Potions Used | `Hook.AfterPotionUsed` | One completed use and one discard | 1 | 1 | Pure count passed; use hook bound at load |
| Starting inventory | Subscribe only after `RunStarted` | Pre-run relic/potion observations | 0 / 0 | 0 / 0 | Passed |
| Multiplayer attribution | All events use owner `NetId` | P10 card/deck stats; P20 relic/potion stats | Isolated | Isolated | Pure scenario passed; live co-op deferred |

The Stage 5 table tests the exact state accumulator and derived Most Played Card behavior. Successful/failed operation boundaries were verified against current decompiled STS2 source, and real menu loading verifies Harmony discovery. It does **not** claim live run, shop, reward, or co-op execution because the protected current saves cannot safely be replaced and no disposable MCP bridge profile exists. Those live flows remain for Stages 6, 8, and 9 after an isolated test setup is available.

## Known limitations and risks

- Target is early-access v0.107.1; Harmony patches and private/tree paths are version-sensitive. Validate release commit/hash on startup and fail closed on incompatible builds.
- Exact intrinsic ownership of merged non-instanced debuff duration is absent from game state. Version 0.2.1 therefore uses the user-approved cumulative positive-application weighting convention and treats unknown applications as uncredited denominator shares.
- Stage 4 supports standard Vulnerable and Weak only. Damage-cap participation is omitted; Assisted Damage is also omitted around any active HP-loss/redirection override. This conservative boundary prevents fabricated credit but undercounts otherwise beneficial effects.
- Strength attribution is limited to exact, reliably owned, powered-attack cases. Incoming hits reduced to zero are reconstructed from the captured pre-modifier attack amount and live Strength only when the additive stack is otherwise known. Unknown source/lifetime, ambiguous restoration, participating damage caps, redirection/HP-loss ambiguity, and unknown additive or multiplicative modifiers are omitted instead of estimated.
- Source provenance is absent for some indirect damage/healing (`dealer`/`cardSource` may be null). Stage 3 deliberately records a diagnostic and omits Damage Dealt/kill credit when no player or pet-owner provenance exists; general healer-source attribution remains unavailable.
- `AfterDeath` has no killer argument. Damage-result kill credit is reliable for lethal damage but not forced/environmental death.
- STS2's existing history persists many ordinary stats but not Damage Dealt, Block, Cards Played, per-player kills, or assists; those require the sidecar.
- Live scene inspection and playtesting were unavailable in Stage 0 because STS2 was not running, and asset search/extraction tooling was unavailable/errored. UI node placement must be verified before implementation is finalized.
- The user intentionally abandoned the former single-player run. The active modded profile still contains a protected `current_run_mp.save` and backup, extensive run history, other-mod state, and an existing `RunStatsCollector` data directory. All live save tests require RunStats-specific isolation plus before/after integrity checks and must not use that multiplayer pair.
- Healing Done is currently defined and implemented as actual healing received by that player's creature. General healer/source attribution is not present in the exposed hook, so source-based co-op healing remains unavailable unless a later reliable path is found.
- Stage 6 validates the actual wire API, packet model, and deterministic two-player/reconciliation logic, but does not claim a live two-Steam-peer session. A live host/client join, forced disagreement, disconnect, and rejoin pass remains part of final isolated multiplayer playtesting when a disposable second peer/profile is available.

## Decisions and unresolved questions

### Explicitly approved by user

- Entire requirements document and staged process.
- Stage 0 only; creation of this specification is authorized.
- Names `RunStats`, `STATS`, and `Assisted Damage` remain unchanged. The visible `Assisted Damage Prevented` label was renamed to `Damage Prevented` by user approval before the v0.3.1 commit; the internal `AssistedDamagePrevented` identifier remains unchanged for save compatibility.
- Existing-mod/game/process safety rules and first-deployment approval gate.
- Stage 1 project scaffold, local installation, and menu-only load verification.
- Stage 2 run/player model and local deployment.
- Stage 3 core combat tracking under the documented conservative rules: recipient-based Healing Done, Block Lost from enemy damage absorption only, and only reliably player-attributed lethal-damage kills.
- Stage 4 conservative assisted policy and implementation: exact credit only for supported, uniquely owned Vulnerable/Weak cases; no self credit or ambiguous split.
- Stage 5 card/economy/item rules, including all completed autoplay/Replay card executions, actual spend/gain deltas, starting/failed-item exclusions, and ordinal card-ID tie-breaking for Most Played Card.
- Stage 6's historical host-authoritative prototype plus the final client-optional replacement: peer-local NetId ownership, no custom network messages, and `affects_gameplay: false`.
- Stage 7 native overlay architecture, responsive table/view model, top-bar visibility gating, deterministic team presentation, and keyboard/controller/mouse close/navigation behavior.
- Stage 8 sidecar design: profile-scoped `com.bradbeise.runstats` ownership, separate SP/MP active and pending files, exact identity/checkpoint restore, successful-vanilla-save promotion, indefinite archive retention, and peer-local multiplayer restore for each installed user.
- Stage 9 local playtest, including the seven-tab/native-card/username UI refinements, hover animation, Ancient-history healing correction, and RunStats-only repair of the affected test sidecar.

## Next stage

Version 0.3.1 was uploaded to the existing public Workshop item `3797791393` with `mod_id.txt` unchanged. Wait for Steam to refresh the subscribed copy before enabling it alongside any local installation. Deferred live multiplayer cases remain documented above.
