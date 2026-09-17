# RunStats Weak and Vulnerable Attribution Design Specification

## Status and authorization gate

- **Status:** Included in the combined Strength Phase 7 Release candidate rebased onto published v0.3.0; all deferred multiplayer Weak/Vulnerable playtests remain pending.
- **Branch:** `WeakVulnTweaks`.
- **Target release:** `0.3.1`.
- **Scope:** Replace the current single-owner/ambiguous Weak and Vulnerable assist policy with proportional per-player contribution ledgers, deterministic remainder rotation, and lifecycle-safe attribution.
- **Visible label:** The persisted/internal `AssistedDamagePrevented` statistic is displayed as **Damage Prevented**. Historical design references retain the earlier wording where needed.
- **Source behavior:** Proportional Weak/Vulnerable attribution is implemented and included in the installed combined local/folder Release test copy. The subscribed Workshop package remains unchanged and disabled.
- **Next approval phrase:** `Approved. Begin Phase 7.` after reporting Phase 6 playtest results.
- Each phase must report its changes, tests, deployment effects, and unresolved evidence, then stop for approval before continuing.

## Approved user goals

1. Track how much Weak and Vulnerable each player applies to each individual enemy.
2. Divide Assisted Damage from Vulnerable among all contributors according to their cumulative applications during the current uninterrupted Vulnerable cycle.
3. Divide Assisted Damage Prevented from Weak among all contributors according to their cumulative applications during the current uninterrupted Weak cycle.
4. Support one through four contributors and unattributed applications without inventing ownership.
5. Round proportional shares down, then distribute indivisible remainder points through a deterministic rotating contributor order.
6. Reset the remainder order whenever a positive application succeeds. The newest application bucket receives the first remainder, followed by the other buckets in reverse application order.
7. Reset all contribution and rounding state only when that specific enemy's corresponding power reaches zero, is removed, or combat ends.
8. Calculate every qualifying final damage/prevention event before death cleanup resets its contribution state.
9. Preserve the existing rule that self-benefit is not assistance. A player's self-benefiting share is discarded and never redistributed.
10. Build and install a Release configuration locally for user testing while the subscribed Workshop copy remains disabled.
11. Finish with version 0.2.1 metadata and the existing Workshop item package ready for a separately authorized publication step.

## Terminology

- **Power amount:** The live remaining duration shown by `WeakPower.Amount` or `VulnerablePower.Amount`.
- **Contribution weight:** A player's cumulative positive amount applied since that enemy's corresponding power last reached zero. Ordinary decay and nonzero reductions do not subtract contribution weight.
- **Cycle:** One uninterrupted period in which a specific enemy's Weak or Vulnerable power remains above zero.
- **Application bucket:** One known player or the unattributed bucket participating in a cycle.
- **Beneficiary:** For Vulnerable, the player attacking the enemy. For Weak, each player target whose incoming damage was reduced.
- **Assist pool:** The exact assisted value produced by the existing live-multiplier counterfactual before proportional division.

Weak and Vulnerable have independent ledgers. Each enemy also has independent ledgers, so applications or rounding on one enemy never affect another enemy.

## Contribution lifecycle

### Positive applications

Observe the actual power amount immediately before and after the game's final Weak/Vulnerable mutation:

1. A positive delta adds that final delta to the reliable applying player's cumulative weight.
2. If no player can be resolved reliably, add the delta to the unattributed weight. Do not award a statistic merely for applying Weak or Vulnerable.
3. A prevented, negated, or zero-delta application adds no weight and does not restart remainder rotation.
4. Cards, potions, relics, powers, pets/summons, and other sources use the same central mutation path when their applying player is reliable.
5. A repeat application by an existing contributor increases that contributor's cumulative weight and moves their bucket to the front of the reverse-application order.
6. A new contributor is inserted at the front of that order.
7. An unattributed positive application updates and moves the single unattributed bucket to the front in the same way. This permits unattributed weight to receive an extra point that remains uncredited.
8. Every accepted positive delta resets the remainder cursor to the front, so the newest application bucket receives the next extra point.

### Reductions and reset

- Natural duration decay does not reduce contribution weights.
- Any other reduction that leaves the power above zero also leaves weights and remainder order unchanged.
- When the live amount reaches zero or the power is explicitly removed, immediately discard that enemy/power cycle's weights, order, and cursor.
- A later positive application starts a fresh cycle with no ownership or rounding state carried forward.
- Enemy removal and combat/run cleanup discard all remaining combat-only ledgers.
- When removal happens inside a qualifying damage command, defer only that cycle's reset until the completed event has been attributed. Cleanup must still run in a `finally` path.

### Example of cumulative weights

Turn 1 applications:

- Player A applies 2 Weak and 1 Vulnerable.
- Player B applies 1 Weak and 2 Vulnerable.
- Player C applies 1 Weak.

The Weak ledger is A=2, B=1, C=1. The Vulnerable ledger is A=1, B=2.

After normal duration decay, Player A applies 2 more Weak and 2 more Vulnerable. Decay does not subtract weights, so the Weak ledger becomes A=4, B=1, C=1 and the Vulnerable ledger becomes A=3, B=2. Because A made the newest positive applications, A is first for the next remainder in both ledgers.

## Proportional allocator

For an assist pool `P` and total weight `W`, each bucket with weight `w` first receives:

`floor(P × w / W)`

The sum of floors can be smaller than `P`. Allocate each remaining integer point sequentially through the current reverse-application order, starting at the remainder cursor. Advance the cursor once for every extra point awarded. A later qualifying event continues at the next bucket unless a positive application has reset the cursor.

Rules:

- Use checked integer/rational arithmetic; do not use binary floating-point percentages.
- The allocator supports one through four known contributors plus the unattributed bucket.
- A bucket can receive at most one remainder point during a single pass through the order. Since the floor deficit is always smaller than the number of positive-weight buckets, one pass is sufficient.
- The unattributed bucket receives its calculated floor and can receive a rotated remainder point. Both remain uncredited.
- A self-benefiting known-player award remains part of the completed proportional allocation but is discarded afterward. It is not redistributed.
- The allocated known, unattributed, and discarded-self shares must sum exactly to the assist pool.
- Remainder state is separate for Weak and Vulnerable on each enemy.

### Reverse application order example

If A applies, then B applies, then C applies, the order is C, B, A. If A later applies again, the order becomes A, C, B and the cursor restarts at A. If an event has two remainder points, they go to A and then C; the following remainder continues at B unless another application resets the cursor.

## Vulnerable Assisted Damage

### Determine the assist pool

Keep the existing counterfactual calculation:

1. Recognize the standard target-owned `VulnerablePower` modifier during an actual top-level damage command.
2. Capture the live Vulnerable multiplier, including game modifiers such as Paper Phrog and Debilitate. Do not hard-code 1.5.
3. Algebraically remove only the Vulnerable multiplier without executing the game damage pipeline again.
4. Apply the target's actual pre-hit Block, integer truncation, remaining HP, and overkill cap to the counterfactual.
5. Assisted Damage is actual enemy HP removed minus counterfactual enemy HP removed, never below zero.
6. Existing fail-closed exclusions for damage-cap participation and unprovable HP-loss/redirection overrides remain unchanged.

### Divide and credit

- Allocate each enemy's Vulnerable assist pool using only that enemy's active Vulnerable ledger.
- Add each known contributor's award to that player's existing Assisted Damage total.
- If the contributor is also the attacking player (including a player-owned pet's owner), discard only that contributor's award as self-benefit. Do not redistribute it.
- Unattributed awards remain uncredited.
- Multi-target attacks are evaluated separately for each damaged enemy because each enemy owns a separate Vulnerable cycle and assist pool.
- For lethal damage, allocate Assisted Damage before Vulnerable removal/death cleanup resets the ledger.

### User example

With Vulnerable weights A=1 and B=2, Player D deals a hit whose actual HP damage is 5 higher because of Vulnerable:

- Floors: A=`floor(5×1/3)=1`, B=`floor(5×2/3)=3`.
- One point remains.
- Because B was the newest applicant, B receives that remainder: A=1, B=4.
- The cursor advances so the next remainder goes to A, unless a new positive application resets the order.

After A applies 2 more Vulnerable in the same nonzero cycle, weights are A=3 and B=2, the order restarts with A, and a 5-point pool divides exactly as A=3, B=2.

## Weak Assisted Damage Prevented

### Determine prevention for every target

Keep the existing counterfactual boundary, expanded across all player targets in the damage command:

1. Recognize the standard dealer-owned `WeakPower` modifier when a Weak enemy attacks one or more players.
2. Capture the live Weak multiplier, including Paper Krane and Debilitate. Do not hard-code 0.75.
3. Algebraically remove only Weak without executing the damage pipeline again.
4. For each player target, calculate counterfactual integer incoming damage minus actual integer incoming damage before that player's Block, powers, relics, HP, overkill, redirection, or other defender-owned mitigation.
5. Never report negative prevention.
6. Sum every per-target prevention value into one command-level Assisted Damage Prevented pool. Retain the per-player target subtotals only so self-benefit can be split out after the single contributor allocation.

Example: one enemy attack would deal 10 to each of four players and Weak reduces each result to 7. The command prevented 3 per player, or 12 total—not merely 3.

### Divide and credit

For each completed enemy damage command:

1. Sum all positive player-target prevention rows.
2. Allocate that total exactly once through the enemy's active Weak ledger. Contributor floor/remainder rounding therefore operates on the command-level pool, not separately on each target.
3. For each contributor's combined gross award, split it proportionally between prevention affecting that contributor and prevention affecting teammates.
4. Discard the self-protection portion and credit only the teammate portion. Do not redistribute discarded points.
5. Carry any fractional self/teammate split forward for that contributor so repeated attacks remain exact over time. Clear these fractions only with the matching Weak zero-out cycle or combat cleanup.
6. Leave unattributed awards uncredited.
7. Advance the main contributor remainder cursor once for this command-level allocation. A positive Weak application resets it; individual target rows do not.

Thus, if A is a Weak contributor and an attack is reduced for A, B, C, and D, A receives no credit for the portion allocated to A's own protection but can receive credit for portions allocated while protecting B, C, and D.

If an attack kills a player or the attacking enemy dies during reactive processing, finalize all Weak prevention rows before any relevant lifecycle cleanup discards the enemy's Weak ledger.

## Attribution and source reliability

- Resolve a contributor from the final power mutation's applying creature, including its player or pet owner.
- Never infer a player from card text, local-player identity, turn owner, or power's stale original `Applier` when the actual mutation source is unavailable.
- An unresolved positive delta is unattributed, not ambiguous. It participates fully in proportional math and remainder rotation but produces no player statistic.
- The old permanent `Ambiguous` state is removed for standard merged Weak/Vulnerable applications because mixed known ownership is now represented explicitly by weights.
- Malformed deltas, amount mismatches, overflow, missing active cycles, or unsupported duplicate/modded power arrangements fail closed, increment an appropriate diagnostic, and do not fabricate credit.

## Data model

Use one generalized contribution ledger keyed by reference identity of the standard power instance, with a stable enemy token and a `Weak` or `Vulnerable` kind check. Each cycle stores:

- last observed live amount;
- known-player cumulative weights keyed by `Player.NetId`;
- unattributed cumulative weight;
- unique reverse-application order containing known IDs and, when present, one unattributed bucket;
- next remainder cursor;
- exact per-contributor fractional carry used only to split a Weak command award between self-protection and teammate protection;
- cycle generation/lifecycle state sufficient to prevent stale reuse.

The allocator returns:

- total assist pool;
- each player's integer award;
- unattributed award;
- discarded self-benefit award;
- next cursor;
- accepted/fail-closed status.

Do not add new visible statistics. Continue writing final awards into Assisted Damage and Assisted Damage Prevented.

## Persistence and multiplayer

- Finalized Assisted Damage and Assisted Damage Prevented totals continue through the existing snapshot/sidecar system.
- Contribution ledgers and remainder cursors are combat-only because STS2 does not save an active combat for Continue Run. Clear them at combat end and never reconstruct them from stale power `Applier` values.
- Every installed peer observes the same synchronized power mutations and damage results and independently derives the same weights, reverse order, cursor, allocations, and totals using `Player.NetId`.
- Do not add custom network messages; keep the release client-optional and `affects_gameplay: false`.
- Phase 1 must determine whether the dormant assisted-ownership DTO/sidecar fields can be removed compatibly or require a schema migration. Do not silently reinterpret the old unique/ambiguous payload as weighted state.
- Existing v0.2.0 totals must remain readable. Historical suppressed assists cannot be reconstructed retroactively.

## Death and cleanup ordering

- Attribute the completed event before clearing a cycle when Weak/Vulnerable removal happens inside its damage command.
- Vulnerable lethal hits must record the added actual HP damage before the killed enemy's powers are removed.
- Weak multi-target attacks must record prevention for every returned player result before player/enemy death cleanup can invalidate the active scope.
- Defer only the exact matching cycle and always clear it afterward in a `finally` path.
- Ordinary zero/removal outside a qualifying damage scope resets immediately.
- Never carry state from one creature reference, combat ID, power kind, combat, or run into another.

## Test matrix

### Contribution lifecycle

- One to four known contributors; repeated contributions; unattributed-only and mixed known/unattributed applications.
- Cumulative weights survive natural decay and arbitrary nonzero reductions.
- Zero/removal clears weights, order, and cursor; reapplication begins fresh.
- Separate Weak/Vulnerable ledgers on the same enemy and separate ledgers across several enemies.
- Prevented/zero-delta applications neither add weight nor reset the cursor.
- New and repeat applications move the newest bucket to the front and restart rotation.

### Allocation and rounding

- Equal and unequal weights for one through four players.
- Pools smaller than contributor count and events with multiple remainder points.
- Reverse application order, cursor advancement, and restart after every positive application.
- Unattributed floor/remainder shares remain uncredited.
- Every allocation conserves the pool across credited, unattributed, and discarded-self values.
- Large checked values and malformed/overflow cases fail closed atomically.

### Vulnerable

- Single and multiple contributors, same-player refresh, mixed unattributed weight, and self-attacker exclusion.
- Block, integer rounding, overkill, lethal damage, multi-hit, multi-target, Paper Phrog, Debilitate, and other supported multipliers.
- Damage cap and HP-loss/redirection override exclusions remain conservative.
- Lethal assist is recorded before cycle cleanup.

### Weak

- One enemy hit against one player and one multi-target command against two, three, and four players.
- A 10-to-7 hit prevents 3 for every affected player; four targets yield a 12-point total pool before self exclusions.
- A contributor targeted alongside teammates loses only their self-target award and retains awards for protecting teammates.
- Multiple hits, integer rounding, Block, defender mitigation, lethal player damage, Paper Krane, Debilitate, and other supported multipliers.
- Target result ordering and remainder rotation are deterministic on all peers.
- Final prevention is recorded before cleanup caused during the attack.

### Compatibility

- Existing non-assisted statistics, Poison/Accelerant attribution, schema migration, UI ordering, and save/continue tests remain unchanged.
- Debug and Release builds have zero warnings/errors and all tests pass.
- Release assembly contains no custom RunStats network messages.
- Manifest remains `affects_gameplay: false`.

## Phased implementation plan

### Phase 1 — Installed-build verification and pure model

- Reconfirm the installed STS2 version and inspect final Weak/Vulnerable apply, modify, decay, removal, multiplier, multi-target damage-result, and death-cleanup order.
- Resolve the dormant ownership DTO/schema migration decision.
- Implement the game-independent weighted cycle ledger and deterministic allocator with lifecycle, reverse-order, unattributed, conservation, and self-exclusion tests.
- Do not add runtime hooks, install files, or change Workshop artifacts.
- Build/test and stop for approval.

**Completed 2026-09-10.** No runtime hook, installation, manifest, or Workshop file changed.

- Verified the installed game is STS2 `v0.107.1` (`release_info.json` commit `59260271`) and `sts2.dll` SHA-256 is `A1F9E653F1E28E4076558FEE1E60D218619CB7E057B887C6417F62C62C6D7A52`.
- Confirmed `WeakPower` and `VulnerablePower` are shared `PowerInstanceType.None` counter powers. Final amount modification occurs before `AfterPowerAmountChanged`; normal enemy-side decay uses the same `PowerCmd.ModifyAmount` path and zero removes the power.
- Confirmed `CreatureCmd.Damage` resolves multi-target results independently in target enumeration order, publishes completed damage results, and then performs lethal cleanup. Runtime phases must capture per-target Weak rows and attribute final Vulnerable results before cleanup.
- Added `AssistedContributionLedger`, using reference-isolated enemy cycles, checked cumulative weights, an unattributed bucket, reverse-application remainder order, cursor reset on positive application, zero-only refresh, and post-allocation self-share discard.
- Added deterministic tests for cumulative/nonzero-reduction behavior, zero refresh, reverse and repeat application ordering, one-to-four contributors, unattributed awards, self exclusion, separate enemies, sequential target rows, mismatch rejection, and overflow atomicity.
- **DTO/schema decision:** Keep snapshot and sidecar schema version 2 for v0.2.1 because assisted ownership is combat-only and STS2 does not restore an active combat checkpoint. In Phase 5, preserve decoding of the legacy schema-2 `assisted_ownership` field so existing totals still restore, but never reinterpret its unique/ambiguous records as weighted contributions. New sidecars will emit an empty ownership collection; legacy nonempty ownership metadata will be deliberately discarded with a diagnostic while all compatible accumulated totals remain intact.
- Verification: all `90/90` Debug tests passed; the complete Release solution built with `0` warnings and `0` errors.

### Phase 2 — Runtime contribution observation

- Replace unique/ambiguous observation with final positive-delta weights for standard Weak and Vulnerable.
- Track new, stacked, modified, unattributed, decreased, zeroed, and removed powers independently per enemy and power kind.
- Wire combat/run cleanup and diagnostics.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Runtime observation is active in source but no build was installed.

- Added one `AssistedContributionTracker` with independent Weak and Vulnerable ledgers. Each ledger keys cycles by enemy reference, so neither another enemy nor the other debuff can affect its weights or remainder state.
- Replaced the former second-pass unique/ambiguous contribution call with a single observation after the awaited final `PowerCmd.ModifyAmount` result. New powers continue through the `ApplyInternal` final-state hook.
- Final positive deltas are attributed to a reliably resolved player; unresolved appliers enter the unattributed denominator. Zero/negative deltas never add weight or restart the remainder cursor.
- Explicit removal and observed zero amounts clear only the matching enemy/effect cycle. Combat end, run start, and run cleanup clear both ledgers.
- Existing Phase 2 assist awards remain conservative: the tracker exposes a unique contributor only while exactly one known player and no unattributed weight owns the cycle. Mixed cycles remain suppressed until proportional Vulnerable and Weak allocation is connected in Phases 3 and 4.
- Rejected/mismatched runtime observations fail closed and increment the corresponding existing assisted-damage diagnostic. A new persisted diagnostic enum was intentionally deferred to avoid changing schema during this phase.
- The dormant v0.2.0 ownership persistence adapter remains isolated under the `LegacyContributorLedger` name pending the approved Phase 5 compatibility removal. It no longer supplies live combat attribution.
- Added tests for creation, stacking, modified applications, unattributed increases, nonzero decreases, zero refresh, explicit removal, effect isolation, enemy isolation, and combat reset.
- Verification: all `92/92` Debug tests passed.

### Phase 3 — Proportional Vulnerable attribution

- Retain the existing exact live-multiplier counterfactual and conservative exclusions.
- Allocate each Vulnerable assist pool proportionally, discard attacker self-shares, and protect lethal ordering.
- Add one-to-four-player, rounding, block, overkill, multiplier, and death regressions.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Proportional Vulnerable attribution is active in source; no build was installed.

- Preserved the existing live Vulnerable multiplier counterfactual and its actual Block, HP-loss, overkill, integer-rounding, redirection, and HP-loss-override boundaries. The resulting integer is the single assist pool passed to the weighted allocator.
- Vulnerable no longer requires unique ownership at damage-observation time. Each qualifying damage result allocates against that enemy's current cumulative Vulnerable weights.
- Known contributors receive `Assisted Damage` according to their allocated shares. The attacking player's own share is discarded after allocation and never redistributed; unattributed floor/remainder shares also remain uncredited.
- Lethal damage is allocated from the completed `AfterDamageGiven` result before `CreatureCmd.Damage` performs kill-time power removal, so the final Vulnerable assist is retained.
- Zero-sized pools do not mutate totals or consume a remainder position. Missing/malformed cycles fail closed and use the existing assisted-damage diagnostic.
- Weak continues to use the conservative unique-contributor award path pending the approved multi-target conversion in Phase 4.
- Added integrated state/tracker tests for the user's A=1/B=2 five-point example and alternating remainder, self/unattributed discard, one-to-four contributors, Block-limited damage, overkill suppression, and lethal-before-reset attribution.
- Verification: all `96/96` Debug tests passed.

### Phase 4 — Multi-target Weak attribution

- Calculate prevention for every player target in a Weak-modified damage command.
- Sum all target rows, allocate the command total once, proportionally split/discard self-protection, and protect death/cleanup ordering.
- Add one-to-four-target and one-to-four-contributor tests, including the 12-point four-player example.
- Build/test without installation and stop for approval.

**Completed 2026-09-10.** Proportional multi-target Weak attribution is active in source; no build was installed.

- Each Weak-modified target produces its own pre-Block/pre-defender-mitigation prevention pool through the existing live multiplier counterfactual. A base 10 attack modified internally to 7.5 produces 3 prevented points per player target, so four targets produce 12 total before exclusions.
- All target rows are summed first and the command total is allocated exactly once through that enemy's Weak remainder cursor. Individual player rows do not perform separate contributor rounding or consume separate cursor positions.
- Each contributor's gross command award is then split by the ratio of prevention affecting themselves versus teammates. Only the teammate portion is credited, so contributors retain their benefit for protecting others without receiving credit for protecting themselves.
- Fractional self/teammate splits carry across later attacks in the same Weak cycle. This prevents either side of an indivisible point from being permanently favored; the carry resets when that enemy's Weak reaches zero or combat ends.
- Weak floor/remainder shares belonging to unattributed applications remain uncredited without redistribution, matching Vulnerable.
- Damage-scope tracking now defers removal of a Weak or Vulnerable cycle that participates in the active command until the command completes. This protects all final target rows if kill/death cleanup occurs during result hooks, while `finally` cleanup guarantees the stale cycle is removed afterward.
- Added integrated tests for the user's A=2/B=1/C=1 four-target example, all one-to-four contributor/target combinations, command-level aggregation, proportional self exclusion, fractional carry and zero reset, the illustrative 10-to-7 prevention boundary, and final-event attribution before cycle cleanup.
- Verification: all `102/102` Debug tests passed.

### Phase 5 — Persistence, compatibility, and documentation

- Apply the Phase 1 DTO/schema decision while preserving v0.2.0 totals and fail-closed decoding.
- Confirm peer-local determinism, no custom network messages, unchanged UI, and no Poison regressions.
- Update `README.md`, `RUNSTATS_SPEC.md`, and maintenance/migration guidance.
- Build/test Debug and Release; do not install yet. Stop for approval.

**Completed 2026-09-10.** Compatibility and documentation are finalized in source; no build was installed.

- Snapshot and sidecar schema remain version 2 because the visible statistic model is unchanged. All v0.2.0 accumulated totals and schema-1 migration behavior remain intact.
- Removed live capture/application of the v0.2.0 unique/ambiguous ownership adapter. New active, pending, and archive sidecars always serialize an empty `assisted_ownership` collection.
- The strict schema-2 decoder still validates legacy ownership records so valid old sidecars can restore their totals. Runtime deliberately ignores nonempty legacy records, logs a compatibility warning, and starts combat-only weighted attribution fresh rather than converting incompatible ownership semantics.
- Confirmed contribution weights, remainder cursors, Weak self-split fractions, Poison fractions, kill comparisons, and Accelerant sponsorship remain combat-only and reset at their defined lifecycle boundaries.
- Updated `README.md`, `RUNSTATS_SPEC.md`, `MAINTENANCE.md`, this design specification, and the local default STS2 assembly path. The UI/stat ordering remains unchanged.
- Added persistence coverage proving legacy totals restore, the compatibility notice receives old metadata, and the next write removes it. Removed the dormant custom message/host-sync implementation files entirely and added assembly inspection proving no compiled RunStats type implements `INetMessage`.
- Existing UI, schema migration, peer-local deterministic state, and all Poison/Accelerant regressions remain green.
- Verification: all `104/104` tests pass in Debug and Release; both configurations build with zero warnings and zero errors.

### Phase 6 — Local Release installation and user playtest

- Reconfirm STS2 is closed, the local/folder source is selected, and the subscribed Workshop RunStats copy is disabled.
- Build and deploy only the three RunStats-owned Release files to the existing game-local `mods\RunStats` directory. Never edit Steam's subscribed Workshop directory.
- Verify source/installed hashes and provide a focused playtest checklist for contribution changes, reverse remainder rotation, unknown shares, self-exclusion, multi-target Weak, lethal Vulnerable, zero refresh, and save/continue.
- Stop for user results and approval.

**Installed 2026-09-10.** Awaiting the user's playtest results; no Workshop file was changed.

**Deferred multiplayer evidence:** The user cannot complete the multiplayer checklist yet. Preserve every Phase 6 multiplayer case and present it again when the broader enemy-debuff work reaches its local Release playtest stage; do not treat the untested cases as passed.

- Confirmed STS2 was closed, the local/folder `runstats` source was enabled, and the Steam Workshop `runstats` source was disabled before deployment.
- Corrected the deployment helper's default Steam library path and restricted Release deployment to exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`; Debug deployments may still include the PDB.
- Release build completed with zero warnings and zero errors.
- Deployed only to `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats`. The subscribed Workshop directory `D:\SteamLibrary\steamapps\workshop\content\2868840\3797791393` remained unchanged.
- Verified SHA-256 source/install matches: `RunStats.dll` `D9847D36DD9007D1F27D2CDB6777E5588DABDBC57076C84001C8F6A5554CB714`; `runstats.pck` `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`; `mod_manifest.json` `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974`.

### Phase 7 — Playtest fixes and regression validation

- Diagnose and fix only approved playtest findings.
- Repeat relevant automated tests, Release build, safe local deployment, and artifact verification.
- Record known limitations and stop for approval.

### Phase 8 — v0.2.1 Workshop readiness

- Synchronize project/assembly/manifest/Workshop metadata at version 0.2.1.
- Update Workshop features, compatibility text, and change note.
- Preserve `mod_id.txt` for existing item `3797791393` and preserve/document the current published rollback package.
- Package exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`; verify hashes and invariants.
- Do not run the Workshop uploader or change publication state without separate explicit authorization.
- Commit and create a pull request only when explicitly requested.

## Safety invariants

- Never edit the Steam-managed subscribed Workshop copy.
- Never enable both local and Workshop RunStats sources simultaneously.
- Never modify vanilla saves or unrelated mods.
- Never deploy or package an untested Debug artifact as the user test release.
- Never invoke the Workshop uploader as an implied part of implementation or Release installation.
- Optional/unsupported attribution fails closed without changing gameplay.
- No per-frame polling, game RNG use, arbitrary local-player ownership, or duplicate damage-pipeline execution.

## Clarification record

1. **Self-benefit:** Discard a contributor's share only where they are the Vulnerable attacker or the Weak-protected target. Do not redistribute it. For multi-target Weak, the contributor still earns shares for teammates protected by the same command.
2. **Unattributed applications:** Include them as a full denominator and remainder participant; their awards remain uncredited.
3. **Nonzero reductions:** Preserve all cumulative contribution percentages until the zero-out refresh.
4. **Remainder order:** Use reverse application order. Every positive application, including a repeat contribution, moves the newest bucket first and restarts the cursor there.
5. **Damage boundaries:** Preserve actual Vulnerable HP/Block/overkill semantics and Weak's pre-defender-Block/mitigation semantics.
6. **Release version:** Use 0.2.1.
7. **Weak command aggregation:** Calculate prevention independently with the live game values for every attacked player, sum those values, and perform one proportional contributor allocation for the entire damage command.
8. **Weak self-split fractions:** Proportionally split each contributor's gross command award between self and teammates, carry indivisible fractions across attacks, and reset those fractions with the Weak zero-out cycle.

## Approval record

- Draft completed on 2026-09-10 from the user's requested proportional design and clarification answers.
- Phase 1 approved by the user and completed on 2026-09-10.
- Phase 2 approved by the user and completed on 2026-09-10.
- Phase 3 approved by the user and completed on 2026-09-10.
- Phase 4 approved by the user and completed on 2026-09-10.
- Phase 5 approved by the user and completed on 2026-09-10.
- Phase 6 approved by the user and installed on 2026-09-10; user playtest results are pending.
