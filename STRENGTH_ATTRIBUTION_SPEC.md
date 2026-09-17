# RunStats Strength Attribution Design Specification

## Status and authorization gate

- **Status:** Strength Phases 1-9 are complete. The verified v0.3.1 Release is installed locally and packaged for Workshop review; no upload, commit, or pull request has been performed.
- **Branch:** `WeakVulnTweaks`.
- **Target release:** `0.3.1` unless changed before Workshop readiness.
- **Scope:** Attribute non-self player-caused Strength changes to Assisted Damage and Assisted Damage Prevented without double-crediting Weak or Vulnerable.
- **Visible label:** The persisted/internal `AssistedDamagePrevented` statistic is displayed as **Damage Prevented**. This label-only change does not alter saved totals or attribution behavior.
- **Next authorization:** Steam upload and commit/pull-request creation remain separate actions and require explicit direction.
- The combined Doom/Strength/Weak/Vulnerable Release candidate is installed from the local folder. The subscribed Workshop copy remains disabled and unchanged.

## Goals

1. Observe the final signed Strength change caused by player actions, cards, potions, relics, powers, and player-triggered reactions.
2. Store a separate active impact event for every affected non-self creature.
3. Preserve the responsible player, affected creature, signed Strength delta, application order, and actual lifetime.
4. Attribute every resolved powered-attack hit independently, including repeated and multi-target attacks.
5. Add helpful effects and subtract harmful effects from the existing assisted statistics.
6. Exclude self-benefit while retaining effects on teammates and enemies.
7. Reset combat-only attribution safely and never guess an unknown player or lifetime.

## Verified STS2 mechanics

- `StrengthPower` is a signed counter power and allows negative amounts.
- Strength contributes through `ModifyDamageAdditive` only when the Strength owner is the damage dealer and the damage has `ValueProp.PoweredAttack`.
- Additive damage hooks run before multiplicative hooks and damage caps.
- Damage is then resolved independently for every target through Block, HP-loss hooks, redirection, current HP, and overkill.
- A card's repeat count and target set can change at runtime. RunStats will therefore observe returned per-hit/per-target damage results instead of predicting `hit_count * target_count`.
- Temporary Strength sources use a concrete `TemporaryStrengthPower`. It applies the corresponding signed `StrengthPower` change, removes itself at the appropriate side-turn end, and restores the same amount.
- `PlayCardAction`, `UsePotionAction`, and player-owned hook actions expose a stable player owner on every peer. Monster turns are not player `GameAction`s.

## Active event model

Each known non-self contribution is represented by:

```text
StrengthImpactEvent {
    event_id,
    impactor_player_net_id,
    entity_impacted,
    impact_value,
    remaining_turns,
    expiry_kind,
    restoration_source
}
```

- `event_id` is a combat-local, monotonically increasing integer. Lower IDs were applied earlier.
- `impactor_player_net_id` is the player responsible for the action or reliably traced delayed source.
- `entity_impacted` uses combat/reference identity, not a display name.
- `impact_value` is the final signed change in `StrengthPower.Amount` for that event.
- `remaining_turns = -1` means no fixed turn countdown; the event lasts until an actual reversal/reset, source removal, entity removal, or combat end.
- Fixed temporary effects retain their remaining-turn representation, but their authoritative expiration is the game's linked temporary/restoration source.
- `expiry_kind` distinguishes fixed temporary, source-bound/conditional, persistent-for-combat, and explicit-reset lifetimes.
- `restoration_source` identifies the game's temporary or conditional source where available, allowing the exact event and amount to expire instead of recording the restoration as a new player contribution.

The ledger is combat-only and separated by affected creature. One action that grants +1 Strength to three teammates creates three events. An effect on the impactor's own creature creates no event.

## Source responsibility

Use this priority order:

1. A delayed Strength change produced by a previously applied player-owned source remains credited to the player who originally supplied that source.
2. Otherwise, every net Strength consequence resolved inside a player action belongs to that action's owner. This includes a monster reaction triggered by the player's card type, draw, discard, card play, potion use, or other input action.
3. A directly owned relic, potion, card, or power effect outside an ordinary action belongs to its reliably resolved player owner.
4. A change caused only by a monster action, environment, or unknown source has no player event.
5. If competing scopes or missing provenance prevent reliable resolution, do not create a known event.

Self-benefit is tested after resolving the impactor and target. A player's Strength change on their own creature is ignored even if a source record exists.

## Observation boundary

For every eligible player-owned action or hook scope:

1. Snapshot the signed `StrengthPower.Amount` of every living player and enemy before execution. Missing Strength is zero.
2. Observe final Strength mutations during the scope so different lifetimes can remain distinct.
3. After the complete action and its synchronous reactions finish, snapshot all surviving creatures again.
4. Reconcile the mutation observations to each creature's final before/after delta. Only reconciled, final nonzero changes create events.
5. Split different lifetimes into separate events even if one action changes the same creature more than once.
6. Ignore transient changes that cancel before the action completes unless a surviving source proves that an active effect remains.

This action-level reconciliation prevents a nested power application from being counted twice while retaining the before/after behavior requested by the user.

## Lifetime and reset rules

- Expire a fixed temporary event when its linked game restoration occurs. Do not create an opposite event for that restoration.
- If restoration is partial, reduce or split only the linked event by the restored amount.
- Expire a conditional event when its linked source reverses or removes its Strength contribution.
- Explicit removal/reset of the affected creature's `StrengthPower` clears all active events for that creature.
- A reliably linked reversal takes priority over treating the change as a new action impact.
- An unrelated positive or negative Strength application creates a new event; it does not consume older events.
- If a partial, unlinked reset makes the known ledger impossible to reconcile safely, clear that creature's Strength events and fail closed for future attribution until new known events are observed.
- Death/despawn clears only that creature. Combat end clears the entire ledger and resets `event_id`.
- The event ledger is not persisted because STS2 does not restore an active combat checkpoint. Final assisted totals continue through normal persistence.

## Shared damage principles

- Process only actual powered attacks to which `StrengthPower` applied.
- Observe each resolved target and hit separately. Do not precompute card repeat or target counts.
- Use the game's live additive, multiplicative, cap, Block, HP, overkill, and redirection values algebraically. Never run the damage pipeline a second time.
- A damage cap or an unprovable HP-loss/redirection override fails closed rather than estimating.
- Use checked integer arithmetic for finalized statistics.
- Attribute the final hit before kill/death cleanup removes its event ledger.

## Non-overlap with Weak and Vulnerable

Damage assistance is decomposed in the game's modifier order so one point is never credited to multiple effects:

1. Establish the result with all live effects.
2. Attribute the existing Vulnerable or Weak multiplicative layer using its approved counterfactual.
3. Remove that already-attributed multiplicative contribution from the Strength eligibility envelope.
4. Attribute only the remaining causal additive Strength contribution.

Vulnerable and Weak keep their approved contribution ledgers and self-exclusion rules. Strength events do not change their ownership weights. If exact decomposition cannot be proven for a hit, omit Strength attribution for that hit.

## Assisted Damage from Strength

This applies when a player or player-owned pet deals a powered attack and active external Strength events target that dealer.

1. Keep the attacker's self-created and unattributed/native Strength in the baseline; they are not assisted contributions.
2. Compute the actual enemy HP removed for this target/hit.
3. Algebraically compute the enemy HP that would have been removed with all active known external Strength events removed while retaining the attacker's own capability and all non-Strength game state.
4. Respect the same Block, current HP, overkill, redirection, rounding, and supported caps.
5. Remove any damage already credited to Vulnerable from the Strength eligibility envelope.
6. The resulting signed difference is the total Strength Assisted Damage for this target/hit.

### Event allocation

For every hit, restart allocation from the beginning:

1. Process harmful negative Strength events first, from lowest `event_id` to highest, and apply their realized negative marginal credits.
2. Process helpful positive Strength events from lowest `event_id` to highest.
3. Give each event no more than its realized marginal effect within the remaining actual eligibility envelope.
4. Stop when no eligible signed difference remains. Any causal amount that cannot be assigned to a known event is ignored.
5. Multiple events belonging to the same impactor accumulate into that player's Assisted Damage total.

This deliberately favors earlier events on every new hit, as requested; it does not rotate or proportionally distribute Strength assistance.

### Approved Conflagration correction

Player B has 5 self Strength and 15 teammate Strength, so Conflagration deals `2 + 20 = 22` before defenses.

- Against Enemy A with 8 Block and 100 HP, actual HP loss is 14. The self-only `2 + 5 = 7` baseline is completely blocked, so the exact external-Strength pool is 14. Allocate A=5, C=5, A=4.
- Against Enemy B with 20 HP and no Block, the self-only baseline removes 7 HP, so the exact external-Strength pool is 13. Allocate A=5, C=5, A=3.
- Each later surviving target/hit is calculated again from its own live Block and HP result, starting with the earliest active event.

## Assisted Damage Prevented from Strength

This applies when an enemy with active player-caused Strength events performs a powered attack against one or more players.

For each resolved target and hit:

1. Select active events targeting the attacking enemy.
2. Exclude events whose impactor is the player being attacked. Their effect remains in the attack's baseline for that target, so self-protection receives no credit.
3. Calculate the live pre-Block attack amount.
4. Algebraically calculate the pre-Block amount with the selected external Strength events removed.
5. Remove any prevention already credited to Weak from the Strength eligibility envelope.
6. `Strength Assisted Damage Prevented = counterfactual pre-Block attack - actual pre-Block attack`.

Player Block and defender-owned mitigation are excluded. Thus, if an enemy would attack for 10 and negative Strength reduces it to 4, the Strength prevention pool is 6 even when Block makes actual HP loss zero.

### Event allocation

For every target/hit, restart allocation from the beginning:

1. Enemy positive-Strength events are harmful and receive realized negative Assisted Damage Prevented first, from lowest `event_id` to highest.
2. Enemy negative-Strength events are helpful and receive realized positive Assisted Damage Prevented next, from lowest `event_id` to highest.
3. Each event is capped by its realized marginal effect and the remaining signed eligibility envelope.
4. Self-protection events, unknown sources, and unprovable residuals remain uncredited.

The same enemy attack can therefore give one player negative Assisted Damage Prevented and another player positive Assisted Damage Prevented, with the signed awards summing to the known causal Strength effect.

## Signed statistic storage

- `Assisted Damage` and `Assisted Damage Prevented` become signed `long` totals and may fall below zero.
- All other statistics remain nonnegative and continue rejecting nonpositive mutations.
- A signed assisted mutation must be nonzero; checked overflow fails atomically.
- UI rows remain in their existing locations and render a leading minus sign naturally.
- Snapshot/sidecar schema advances from 2 to 3 so signed assisted semantics are explicit.
- Schema-1 and schema-2 saves migrate all existing nonnegative totals unchanged.
- Schema-3 validation permits negative values only for the two assisted statistics.
- The visible statistic set does not change.

## Determinism and multiplayer

- Each peer observes the same ordered player actions and combat commands and derives the same combat-local events.
- `event_id` order comes only from deterministic command completion, never wall-clock time or object hash order.
- Creature identity uses stable combat identity plus guarded reference ownership.
- No game RNG and no custom network messages are used.
- Unknown or ambiguous player attribution fails closed.

## Required automated coverage

- Positive and negative permanent Strength on allies and enemies.
- Temporary Strength apply, stack, partial restoration, full restoration, and source removal.
- One action affecting one through four allies/enemies; self event omitted.
- Card, potion, relic, power, draw, discard, play, and player-triggered monster reaction scopes.
- Delayed source ownership and unknown-source rejection.
- One through four contributors, repeated contributors, and earliest-event ordering on every hit.
- Harmful events before helpful events for both assisted statistics.
- Single-hit, multi-hit, multi-target, modified repeat, dead-target, Block, overkill, lethal, redirection, damage cap, and non-powered damage boundaries.
- Strength combined with Weak and Vulnerable without duplicate credit.
- Negative assisted totals, schema-1/2 migration, schema-3 round trip, overflow, UI formatting, and save/continue.
- Combat/entity/reset cleanup and final lethal-event attribution.
- Existing Poison, Accelerant, Weak, Vulnerable, UI, persistence, and no-network regressions.

## Phased implementation plan

### Strength Phase 1 — Installed-build verification and pure event model

- Reverify the installed STS2 build and inspect all Strength, temporary restoration, action-owner, side-turn, damage-stage, reset, and cleanup boundaries.
- Inventory vanilla cards, potions, relics, powers, and player-triggered monster reactions that can change Strength.
- Implement only a game-independent signed event ledger, lifetime reconciliation, and allocation model with tests.
- Do not add runtime hooks, install files, or edit Workshop artifacts.
- Build/test and stop for approval.

**Completed 2026-09-10.** Only the game-independent event model, tests, and specification changed; no runtime hook or installed/Workshop file changed.

- Reverified STS2 `v0.107.1`, commit `59260271`. Installed `sts2.dll` length is `9,364,480` bytes and SHA-256 is `A1F9E653F1E28E4076558FEE1E60D218619CB7E057B887C6417F62C62C6D7A52`.
- Confirmed `StrengthPower` is a signed counter whose additive modifier returns its amount only for its owner dealing a powered attack. Additive hooks precede multiplicative hooks and caps; each target is then resolved independently through Block and HP-loss processing.
- Confirmed all production `TemporaryStrengthPower` subclasses restore their internally applied Strength through the shared source-bound removal path at the affected side's turn end. A linked restoration can therefore remove the exact event/source instead of creating an opposite contribution.
- Confirmed explicit full Strength removal exists (`PlowPower`) and must clear that creature's ledger. A partial restoration involving several contributions merged into one temporary source has no intrinsic per-player ownership; Phase 2 must resolve an exact event or clear the affected ledger rather than guess.
- Added `StrengthImpactLedger` with combat-local monotonic event IDs, signed deltas, per-creature isolation, self rejection, fixed/source/reset lifetimes, partial event reduction, source expiration, target/combat cleanup, signed per-event capacities, harmful-first allocation, earliest-event ordering, conservation, and fail-closed validation.
- Added eight pure-model tests covering signed ordered registration, self exclusion, target isolation, turn/source expiration, partial restoration, earliest-event restart on every hit, harmful-first outgoing and incoming allocation, sign validation, duplicate capacities, and atomic rejection.
- Verification: all `112/112` tests pass in Debug and Release; both complete solution builds finish with zero warnings and zero errors.

#### Vanilla Strength source inventory

The inventory is deliberately broader than sources expected to earn assistance. Self effects are observed for reconciliation but excluded from event creation; monster-native effects remain uncredited unless the approved player-action scope makes the player responsible.

- **Cards referencing direct, temporary, or delayed Strength (25 production):** Arsenal, Brand, Bulk Up, Coordinate, Crush Under, Dark Shackles, Demon Form, Dominate, Dying Star, Enfeebling Touch, Feeding Frenzy, Fight Me!, Friendship, Inflame, Mad Science, Malaise, Mangle, Monarch's Gaze, Monologue, Piercing Wail, Prowess, Resonance, Rupture, Setup Strike, and Shared Fate.
- **Potions (5):** Flex Potion, Fysh Oil, Mazaleth's Gift, Shackling Potion, and Strength Potion.
- **Relics (15):** Brimstone, Ember Tea, Girya, Mini Regent, Philosopher's Stone, Rainbow Ring, Red Skull, Reptile Trinket, Ruined Helmet, Shuriken, Sling of Courage, Sparkling Rouge, Sword of Jade, Toasty Mittens, and Vajra.
- **Delayed/conditional power handlers that directly apply or remove Strength (15):** Arsenal, Crab Rage, Demon Form, Enrage, High Voltage, Monologue, Plow, Possess Strength, Ravenous, Ritual, Rupture, Suck, Tender, and Territorial powers, plus the shared Temporary Strength base.
- **Temporary Strength subclasses (13 production):** Coordinate, Crush Under, Dark Shackles, Dying Star, Enfeebling Touch, Feeding Frenzy, Flex Potion, Mangle, Monarch's Gaze Strength Down, Piercing Wail, Reptile Trinket, Setup Strike, and Shackling Potion powers.
- **Monster-native direct applications:** 50 monster model files plus the Murderous encounter modifier apply Strength directly. They remain baseline unless a specific change completes inside an attributable player action, such as Enrage reacting to that player's Skill.

### Strength Phase 2 — Action attribution and lifecycle observation

- Add scoped before/after snapshots and final Strength mutation reconciliation.
- Resolve direct, reactive, relic/potion, delayed-source, unknown, and self cases.
- Connect actual temporary restoration and reset cleanup.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Strength observation and lifecycle tracking are active in source, but no Strength damage/prevention awards are connected and no build was installed.

- Added `StrengthContributionTracker`, which holds deterministic per-action before snapshots and final mutation observations, verifies each affected creature against the after snapshot, and commits reconciled changes only when the complete action finishes.
- Player action ownership overrides a direct applier for immediate action consequences, so an enemy's Enrage-style reaction belongs to the player whose action caused it. A reliably mapped older power source overrides the later action owner, preserving the approved original-provider rule.
- Final direct changes outside a player action use a reliable player applier. Unknown monster/environment changes remain baseline and create no event. Self changes are accepted for reconciliation but create no event.
- Added ActionExecutor start/finish subscriptions and cancellation cleanup. Paused/resumed actions retain one scope and snapshot instead of being counted twice; canceled and transient net-zero actions commit nothing.
- Added final `StrengthPower` observation to the existing apply/modify/remove boundary. Full zero/removal clears the affected creature's active ledger, while combat/run cleanup clears all action, source, and event state.
- Added scoped observation around the shared `TemporaryStrengthPower` apply, stack-change, and side-turn restoration methods. Exact linked restoration removes or reduces its event; a partial restoration across several merged contributors clears that creature's ledger because STS2 does not identify whose portion was restored.
- Added source-owner mapping for reliably attributable delayed powers. The installed vanilla inventory contains no known player-to-teammate lasting power that later grants permanent Strength, but the supported path is retained and unknown delayed provenance fails closed.
- Added explicit source scopes for Brimstone and Philosopher's Stone. Their enemy Strength applications pass a null game applier, so these wrappers preserve the owning player's harmful contribution; other inventoried non-self card, potion, and temporary effects already expose an action owner or player applier.
- Added seven Phase 2 tests for player-caused monster reactions, delayed-source priority, self/unknown rejection, exact temporary expiration, ambiguous partial restoration, mismatch/cancellation atomicity, and transient changes.
- Verification: all `119/119` tests pass in Debug and Release; both complete solution builds finish with zero warnings and zero errors.

### Strength Phase 3 — Assisted Damage

- Add exact per-result outgoing Strength counterfactual and earliest-event allocation.
- Integrate Vulnerable non-overlap, Block, overkill, lethal, multi-hit, and multi-target handling.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Exact outgoing Strength attribution now runs at the resolved per-hit/per-target damage boundary. Signed awards are deliberately calculated but not persisted until the Phase 5 schema change, and no build was installed.

- Added `StrengthDamageCalculator`, which removes the Vulnerable layer first, derives the self/native/additive baseline by removing only known external Strength events, and computes signed causal HP loss after the target's actual pre-hit Block and HP cap.
- Event capacities are recalculated for every resolved hit in harmful-first and then earliest-event order. This preserves the approved ordering for repeated contributors and naturally restarts it for multi-hit and multi-target attacks.
- The corrected Conflagration cases are covered exactly: the 8-Block target produces a 14-point Strength pool allocated A=9/C=5, while the 20-HP lethal target produces a 13-point pool allocated A=8/C=5.
- Vulnerable owns only the outer multiplicative HP-loss layer. Strength owns only the underlying no-Vulnerable HP-loss difference, so Block, overkill, lethal damage, and Vulnerable cannot be credited twice.
- A live outgoing observation accepts the known Weak multiplier on the attacker and preserves the game's decimal-to-integer damage boundary. Damage caps, HP-loss/redirection hooks, multiple relevant power instances, an unknown multiplicative modifier, and a zero-clamped result whose original value cannot be reconstructed all fail closed.
- Added six Phase 3 tests covering the corrected Block/lethal examples, Vulnerable separation, harmful-before-helpful ordering, Weak rounding, per-hit restart behavior, and atomic rejection of ambiguous observations.
- Verification: all `125/125` tests pass in Debug and Release; both complete solution builds finish with zero warnings and zero errors. The installed local DLL remains the prior Release (`D9847D36DD9007D1F27D2CDB6777E5588DABDBC57076C84001C8F6A5554CB714`), and the disabled Workshop DLL remains unchanged (`E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`).

### Strength Phase 4 — Assisted Damage Prevented

- Add pre-Block incoming Strength counterfactual, per-target self exclusion, and signed harmful-first allocation.
- Integrate Weak non-overlap and multi-target/multi-hit handling.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Exact incoming Strength attribution now runs once for every resolved enemy-hit/player-target pair. Signed awards are calculated but remain transient until Phase 5, and no build was installed.

- Added the incoming counterfactual to `StrengthDamageCalculator`. It removes Weak's multiplicative layer first, retains the attacked player's own contribution and native/unattributed Strength in that target's baseline, and measures the selected external events entirely in pre-Block integer damage.
- Block, HP loss, overkill, and lethal state do not reduce the prevention pool. A fully blocked attack therefore retains the same Strength prevention as an unblocked attack with the same pre-Block amount.
- Selection and allocation restart independently for every player target and every hit. An impactor's event is excluded only when that same player is the current protected target; it remains eligible for teammates hit by the same enemy attack.
- Positive enemy Strength is harmful and receives signed negative Assisted Damage Prevented first. Negative enemy Strength is helpful and receives positive credit afterward, with both groups ordered by earliest event ID.
- Weak owns only the removed Weak layer. Strength owns only the underlying no-Weak pre-Block difference. A supported Vulnerable multiplier on the protected target remains downstream of Strength and preserves the game's decimal truncation without duplicate Weak credit.
- Damage caps, redirection/HP-loss ambiguity, multiple relevant power instances, unknown multiplicative modifiers, and unrecoverable zero-clamped damage continue to fail closed.
- Added six Phase 4 tests covering pre-Block behavior, per-target self exclusion, harmful-before-helpful signed allocation, Weak separation, downstream Vulnerable rounding, four-target restart behavior, and atomic failure cases.
- Verification: all `131/131` tests pass in Debug and Release; both complete solution builds finish with zero warnings and zero errors. The installed local DLL remains the prior Release (`D9847D36DD9007D1F27D2CDB6777E5588DABDBC57076C84001C8F6A5554CB714`), and the disabled Workshop DLL remains unchanged (`E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`).

### Strength Phase 5 — Signed persistence and compatibility

- Implement schema 3 signed-assisted validation and schema-1/2 migration.
- Confirm peer-local determinism, no network messages, combat-only ledgers, and unchanged visible rows.
- Build/test Debug and Release without installation; stop for approval.

**Completed 2026-09-10.** Outgoing and incoming Strength allocations now update the existing assisted statistics, and signed totals persist safely under schema 3. No build was installed and no Workshop artifact changed.

- Added centralized statistic semantics: only `AssistedDamage` and `AssistedDamagePrevented` accept nonzero signed mutations or negative stored totals. Every other statistic retains its positive-mutation and nonnegative-total rules.
- Added atomic multi-player mutation batches. A Strength hit validates and applies every nonzero player award together; overflow, an unknown player, malformed allocation conservation, or an invalid statistic leaves every player and revision unchanged.
- Added `StrengthStatTracker` and connected both live resolved-hit paths. Helpful and harmful Strength awards now flow into the existing Assisted Damage or Assisted Damage Prevented row without creating any new visible statistic.
- Advanced runtime snapshots and sidecars from schema 2 to schema 3. New files allow negative values only for the two assisted statistics. Schema-2 files migrate their complete nonnegative totals unchanged; schema-1 migration additionally retains the established Poison Applied and poison-diagnostic initialization. Negative values in schema 1/2 and negative ordinary statistics in schema 3 fail closed.
- Schema-3 sidecars preserve signed totals through active-store write/load and save/continue reconstruction. UI formatting uses the existing invariant number formatter, including minus signs for player and team totals and grouped negative values.
- Strength event ledgers remain combat-only and are never serialized. Signed awards are derived locally and deterministically from event order; no custom network-message source was reintroduced.
- Added six Phase 5 tests covering signed mutation scope, atomic cross-player overflow, schema-2 migration/rejection, schema-3 signed sidecar restoration and validation, player/team UI rendering, and peer-local deterministic awards.
- Verification: all `137/137` tests pass in Debug and Release; both complete solution builds finish with zero warnings and zero errors. The compiled assembly still contains no custom network-message implementation. The installed local DLL remains the prior Release (`D9847D36DD9007D1F27D2CDB6777E5588DABDBC57076C84001C8F6A5554CB714`), and the disabled Workshop DLL remains unchanged (`E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`).

### Strength Phase 6 — Documentation and full regression validation

- Update `README.md`, `RUNSTATS_SPEC.md`, maintenance/migration guidance, and Workshop draft text.
- Run the complete Debug/Release suite and inspect the Release assembly/package invariants.
- Do not install or upload; stop for approval.

**Completed 2026-09-10.** User-facing, technical, migration, maintenance, and review-only Workshop draft documentation now covers the combined Weak/Vulnerable/Strength behavior. No file was installed, packaged into Workshop content, or uploaded.

- Updated `README.md` with proportional Weak/Vulnerable behavior, signed source/lifetime-aware Strength attribution, schema-3 persistence, exact outgoing/incoming damage boundaries, self exclusion, and fail-closed cases.
- Updated `RUNSTATS_SPEC.md` with the completed Strength architecture and phase record, signed Assisted Damage definitions, damage-hook map, schema-1/2 migration, known limitations, and the combined Phase 7 playtest gate.
- Updated `MAINTENANCE.md` with schema-3 upgrade/rollback guidance, atomic signed awards, combat-only state, duplicate-install rules, the validated candidate hashes, and the unchanged later version/package gate.
- Added `WORKSHOP_UPDATE_DRAFT.md` containing proposed v0.2.1 description and change-note copy. Live `workshop.json`, Workshop content, visibility, item ID `3797791393`, and installed artifacts remain unchanged.
- Corrected the maintenance guide's stale v0.1.0 manifest hash to the checked-out file's actual `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA`; the rollback file itself matches `HEAD` and was not edited.
- Debug and Release each pass all `137/137` tests. Both complete solution builds finish with zero warnings and zero errors, including the compiled no-custom-network-message invariant.
- The future package inputs exist and remain exactly scoped by the packaging helper: Release DLL `81B1BB3994AA2C01656D99BCE1B5A4168C0192644066B90779FF5070E5ED94BA`, PCK `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, and current manifest `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974`.
- Project, assembly, and manifest versions intentionally remain 0.2.0 until Strength Phase 9 synchronizes 0.2.1. The source manifest still has `affects_gameplay: false`; `mod_id.txt` remains `3797791393`; published Workshop and v0.1.0 rollback hashes remain preserved.
- The installed local DLL remains the earlier Weak/Vulnerable candidate (`D9847D36DD9007D1F27D2CDB6777E5588DABDBC57076C84001C8F6A5554CB714`), and the disabled Workshop DLL remains published v0.2.0 (`E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`).

### Strength Phase 7 — Local Release installation and combined playtest

- Confirm STS2 is closed, local/folder RunStats is enabled, and Workshop RunStats is disabled.
- Deploy exactly the three RunStats-owned Release files to the local mod folder and verify hashes.
- Present the Strength checklist together with every deferred multiplayer Weak/Vulnerable Phase 6 case.
- Explicitly mark every untested multiplayer case as pending rather than passed.
- Stop for user results and approval.

**Installed 2026-09-10; replaced 2026-09-14 after the v0.3.0 merge.** Awaiting user playtest results. No Workshop file was changed.

- Confirmed Slay the Spire 2 was closed before deployment.
- Confirmed `settings.save` has local `runstats` enabled from `mods_directory`, Workshop `runstats` disabled from `steam_workshop`, and global mods enabled.
- Deployed exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json` to `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats` using the Release-only deployment path.
- Installed/source hashes match: DLL `81B1BB3994AA2C01656D99BCE1B5A4168C0192644066B90779FF5070E5ED94BA`, PCK `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, manifest `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974`.
- The Steam-managed Workshop DLL remains unchanged at `E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8`.
- The Mods screen will still label this candidate 0.2.0 because coordinated 0.2.1 project/assembly/manifest synchronization is intentionally Strength Phase 9.

The 2026-09-14 replacement fast-forwarded the branch to public v0.3.0 and retained Doom alongside all Strength and proportional Weak/Vulnerable behavior. The combined snapshot/sidecar schema is 4: schema 3 remains the published Doom format, while schema 4 enables signed assisted totals. Debug and Release builds completed with zero warnings/errors, all 141 tests passed in both configurations, and the installed/source hashes match: DLL `0C54F35369228DAA17194D4310FC562F75200028DADBCA0328A6A476CEF521D7`, PCK `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, manifest `0B2EE0AAB26CB7F41DAD124630B4F7F82FDBD953B8589E30A6E577C5C0D2CD98`. The candidate displays version 0.3.0 until the final next-release synchronization gate.

#### Combined playtest checklist

Every case below is **pending** until the user reports a result. Automated coverage is not a substitute for this live evidence.

1. Open Mods and confirm local/folder RunStats is enabled and Workshop RunStats is disabled/duplicate. Confirm the Stats screen opens and existing totals load.
2. Apply teammate Strength to a player, then use single-hit, multi-hit, and multi-target powered attacks. Confirm Assisted Damage increases once per actual hit/target and never exceeds HP damage after Block/overkill.
3. Confirm the corrected boundary where base+self damage is fully blocked but teammate Strength penetrates Block. On lethal targets, only HP actually removed is eligible.
4. Apply Strength to yourself and attack; confirm the self-created portion gives no Assisted Damage. Apply harmful negative Strength to a teammate and confirm the responsible player's Assisted Damage subtracts, including a visible negative total where possible.
5. Apply negative Strength to an enemy and let it attack blocked and unblocked players. Confirm prevention is based on pre-Block damage and is credited separately for each player hit.
6. Confirm a player receives no prevention credit from their own enemy-Strength event when they are hit, but still receives credit for teammates hit by the same enemy. Apply harmful positive Strength to an enemy and confirm its owner receives negative Assisted Damage Prevented.
7. Combine teammate Strength with Vulnerable on outgoing attacks and enemy Strength reduction with Weak on incoming attacks. Confirm there is no obvious double credit between the two effects.
8. Test temporary and combat-long Strength effects. Confirm temporary credit stops after the game's actual restoration, persistent credit remains, explicit Strength reset/zero starts fresh, and a new combat starts clean.
9. Where practical, cover card, potion, relic, power, draw/discard/play, delayed, and player-triggered monster-reaction Strength changes. Unknown/unowned effects should not assign player credit.
10. With repeated/multiple Strength contributors, confirm harmful events are applied first and helpful credit starts with the earliest surviving application on every new hit rather than rotating.
11. Save, quit, and continue after producing positive and negative assisted totals. Confirm totals restore with their signs and attribution resumes from fresh combat-local events.
12. **Deferred Weak/Vulnerable multiplayer:** test one through four contributors; cumulative weights; nonzero-duration reductions preserving weights; zero/removal refresh; reverse-application remainder rotation; a repeat application restarting the cursor at the newest contributor; unknown shares remaining uncredited; Vulnerable attacker self-share discard; lethal/Block/overkill Vulnerable; multi-player Weak command aggregation; per-target Weak self-protection discard and fractional carry; Block-independent Weak prevention; and save/continue totals.
13. **Existing deferred Poison regression:** if multiple Accelerant owners are available, confirm upgraded A then regular B sponsors triggers as `standard, A, A, B`. This remains pending from the earlier poison playtest.

### Strength Phase 8 — Playtest fixes and final regression

- Diagnose and fix only approved findings.
- Repeat relevant tests, Release build, safe local deployment, and artifact verification.
- Record remaining deferred evidence and stop for approval.

**Approved and implemented 2026-09-17.** The user confirmed the general Strength, save/continue, Weak/Vulnerable regression, and Doom/Poison merge checks. The only confirmed defect was incoming Strength prevention when repeated enemy hits were reduced to zero. Items involving harmful teammate Strength, harmful enemy Strength, difficult ordering combinations, multiple Accelerant owners, and some broader multiplayer combinations remain explicitly untested rather than treated as passed.

- Root cause: the incoming calculator deliberately rejected a final zero because the clamp erased the negative intermediate value, so every zero-damage hit was omitted even though repeated-hit iteration itself was correct.
- The `Hook.ModifyDamage` prefix now captures the original per-hit amount. For a zero result, runtime accepts reconstruction only when Strength is the sole additive modifier; unsupported additive stacks continue to fail closed.
- The calculator combines that original amount with live Strength, removes only eligible non-self tracked events, preserves downstream Vulnerable scaling and Weak separation, and allocates every hit and player target independently.
- Added a four-player regression matching the reported scenario: one `8 -> 2` hit awards `6 × 3 = 18`; eight `2 -> 0` hits award `2 × 8 × 3 = 48`; the applier receives exactly `66` Assisted Damage Prevented and receives no self-protection credit.
- Debug and Release builds complete with zero warnings/errors and all `142/142` tests pass in both configurations. The verified Release DLL hash is `7178884B6F496EB870A68E7AA80D243051DF920A0B31D83D6577F8FC79B7BB72`.
- After the initial deployment check found Slay the Spire 2 running, deployment waited until the process closed. The Release candidate was then installed with exactly the DLL, PCK, and manifest; all installed hashes match the verified workspace sources. No Workshop file was changed.
- On 2026-09-17, the user could not repeat the live four-player setup and explicitly accepted the exact automated regression as sufficient confirmation. The scenario therefore remains labeled **automated pass / live retest not performed**, rather than being represented as a live multiplayer pass.

### Strength Phase 9 — v0.3.1 Workshop readiness

- Synchronize project, assembly, manifest, migration notes, Workshop metadata, and change note.
- Preserve Workshop item `3797791393` and the rollback package.
- Package exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json` and verify hashes.
- Do not upload or change publication state without separate explicit authorization.
- Commit and create a pull request only when explicitly requested.

**Completed 2026-09-17.** Project/package version and assembly version are synchronized at `0.3.1`/`0.3.1.0`; schema remains 4. The reviewed Workshop description and change note include retained Poison/Doom behavior, proportional Weak/Vulnerable attribution, signed Strength attribution, and exact repeated zero-damage hit handling.

- Preserved the prior public three-file package under `workshop/RunStats/rollback/v0.3.0` before replacing uploader content.
- Preserved Workshop item ID `3797791393`, public visibility metadata, and `affects_gameplay: false`. The thumbnail remains 735,516 bytes, below the 1 MB limit.
- Debug and Release builds complete with zero warnings/errors and all `142/142` tests pass in both configurations. The Release assembly contains no custom network-message implementation.
- Packaged exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`; each package hash matches its source. The same three files were installed locally and their installed hashes also match.

| v0.3.1 file | SHA-256 |
| --- | --- |
| `RunStats.dll` | `09B82B9C9F84DD08FED6E977C5BB2E249C80E62336061FBF1128E58945DC6B7E` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `322D17AF10E56DBCED064ABE3812BE097890364A96FBD6DA623CF403077C840B` |

Before commit, the visible `Assisted Damage Prevented` label was renamed to `Damage Prevented`; the internal persisted identifier remains unchanged. No uploader command was run and no publication state changed.

## Safety invariants

- Never modify gameplay, vanilla saves, or unrelated mods.
- Never edit the Steam-managed Workshop copy.
- Never enable local and Workshop RunStats simultaneously.
- Never estimate an unknown contributor, lifetime, reset, or unsupported damage counterfactual.
- Never execute the damage pipeline a second time for attribution.
- Never use per-frame polling, game RNG, or custom network messages.
- Never treat deferred multiplayer evidence as passed.

## Decision record

1. Use exact Block/HP/overkill counterfactuals for outgoing Strength; the corrected Conflagration pools are 14 and 13.
2. Incoming Strength prevention is measured pre-Block, so Block never inflates the prevention pool.
3. Harmful effects subtract from assisted totals, and those totals may become negative.
4. Use actual source-linked restoration/expiration rather than relying solely on a guessed countdown.
5. Reliably traced delayed Strength belongs to the player who supplied the original lasting source.
6. Decompose Strength from Weak/Vulnerable so the same damage is never credited twice.
7. Process harmful events first and otherwise allocate earliest application to latest on every hit.
