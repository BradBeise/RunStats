# RunStats Poison Tracking Design Specification

## Status and authorization gate

- **Status:** All implementation phases complete; v0.2.0 is locally validated and Workshop-ready, with commit and pull request in progress.
- **Branch:** `PoisonFix`.
- **Scope:** Add one displayed statistic, **Poison Applied**; attribute actual poison HP damage into each contributing player's existing **Damage Dealt** total; and credit **Assisted Damage** when a player's Accelerant causes extra poison triggers that deal teammates' poison damage.
- **Implementation authorization:** All remaining phases, final commit, and pull-request creation were approved on 2026-09-08. The Steam-owned Workshop copy must remain untouched; an actual Workshop upload remains outside this repository-readiness phase.
- **Next action:** Commit the completed branch and create the approved pull request. Workshop publication remains a separate action.
- Each implementation phase must report its code changes, tests, and unresolved evidence, then stop for approval before the next phase.

## User goals

1. Add **Poison Applied** to the Damage tab, with per-player and Team totals.
2. Detect the actual positive change in poison on every affected enemy after a reliably player-owned action resolves, rather than relying on card text or a hard-coded list of poison cards.
3. Cover cards, potions, relics, powers, pets/summons, and any other game action that can reliably be traced to a player.
4. Add poison damage to the existing **Damage Dealt** value for the player or players who contributed to that enemy's poison pool.
5. Preserve a contribution history while that enemy remains poisoned. Clear it when poison reaches zero or the power is removed; later poison begins a fresh ownership pool.
6. Treat the first poison trigger in a turn as standard. For each additional trigger caused by Accelerant, credit the Accelerant owner with Assisted Damage equal to teammates' credited poison damage on that trigger.
7. When several players contribute Accelerant, assign extra triggers in the order their Accelerant was played; an upgraded Accelerant contributes two consecutive positions.
8. Support single-player, co-op, save/continue, and the existing client-optional multiplayer model without custom network messages.
9. Produce and locally install a **Release** build for user testing while the subscribed Workshop copy remains disabled.
10. Finish with all repository and package metadata ready to update the existing Workshop item, but do not publish without separate authorization.

## External verification basis

- The requested STS2 modding MCP project documents tools for decompilation, hook discovery, builds, deployment, and playtesting: <https://github.com/elliotttate/sts2-modding-mcp>.
- The MCP server was not connected in the current environment. For Phase 1, its repository was cloned temporarily and its documented decompilation workflow was applied directly to the user's installed assembly; no bridge or game mod was installed.
- Independent open-source STS2 mod evidence shows poison damage flowing through the central `CreatureCmd.Damage` path and identifies `PoisonPower.AfterSideTurnStart` as the poison trigger. This is supporting evidence only, not a substitute for checking the user's installed build: <https://github.com/brian-gates/sts2-damage-charts>.
- No broad reflection patch over every power/relic hook is planned. A published STS2 damage mod reports instability from blanket hook patching; RunStats will use the smallest verified central hooks.

## Phase 1 installed-build findings

Research completed on 2026-09-08 against the user's installed assembly. These findings replace the earlier provisional hook assumptions.

### Environment and safety state

- Steam root: `C:\Gaming\Clients\Steam`.
- Steam library containing app `2868840`: `D:\SteamLibrary`.
- Game root: `D:\SteamLibrary\steamapps\common\Slay the Spire 2`.
- Steam build ID: `23811903`.
- Game: `v0.107.1`, commit `59260271`, branch `v0.107.1`, release date 2026-06-18.
- `sts2.dll`: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll`; length 9,364,480 bytes; SHA-256 `A1F9E653F1E28E4076558FEE1E60D218619CB7E057B887C6417F62C62C6D7A52`.
- Slay the Spire 2 was not running during research.
- The game-local `mods` directory is absent, so there is no local RunStats copy.
- Workshop item `3797791393` is subscribed and installed under Steam's Workshop content tree. Its DLL/PCK/manifest hashes match the v0.1.0 baseline in `MAINTENANCE.md`.
- The user's `settings.save` has global mods enabled but `runstats` explicitly set to `is_enabled: false` with source `steam_workshop`, confirming the subscribed copy is off.
- Existing RunStats sidecars were observed but not opened, changed, migrated, or used for a live test.
- The user-provided MCP repository and an ILSpy tool were placed only in temporary directories. The installed assembly was decompiled only into a temporary directory. No MCP bridge, mod, or game asset was installed.

### Build prerequisites

- With user approval, .NET SDK `9.0.317` and its `9.0.19` runtimes were installed system-wide on 2026-09-08. The earlier .NET 6 installation remains available side-by-side.
- With user approval, Python `3.12.10` was installed system-wide at `C:\Program Files\Python312\python.exe`; the Python launcher resolves it through `py -3.12`.
- The provided MCP's pure-Python PCK builder successfully parsed the installed RunStats PCK as a Godot 4.5.1 pack containing the expected nine localization files.
- A temporary verification build from `src/RunStats/RunStats` also succeeded with the same nine logical paths. It was written outside the repository and was not installed or packaged.
- The cloned repository intentionally does not contain `src/RunStats/runstats.pck` or Release output. These artifacts will be rebuilt in their normal ignored workspace locations during the approved build/deployment phases.
- Phase 2 now has the required compiler/test runtime. The verified Python/PCK path is ready for the later Release packaging phase.

### Exact poison mechanics

`PoisonPower` is a non-instanced Counter debuff. Its `AfterSideTurnStart(CombatSide, IReadOnlyList<Creature>, ICombatState)` behavior is:

1. Run only when the poisoned owner is among the creatures starting the side turn.
2. Compute the iteration count once as `min(current poison, 1 + sum(AccelerantPower on living opponents))`.
3. For each iteration, call `CreatureCmd.Damage` on the poisoned creature using its current poison amount, `ValueProp.Unblockable | ValueProp.Unpowered`, and null dealer/card source.
4. If the poisoned creature survives, call `PowerCmd.Decrement`, which routes through `PowerCmd.ModifyAmount(..., -1, null, null)`.
5. If the creature dies, do not decrement again; wait briefly instead.

Consequences for the design:

- Trigger 0 is standard. Trigger indices 1..N are Accelerant extras.
- Each extra trigger consumes one poison just like the standard trigger.
- Extra-trigger count is capped by the poison amount captured when the sequence begins.
- The actual damage result is authoritative because damage hooks and HP-loss/redirection logic can change HP removed even though poison is unblockable and unpowered.
- Because poison passes null dealer/card source, current RunStats deliberately treats it as unsupported. The new poison scope must intercept it before the generic unsupported-source path and must prevent double counting.
- `Hook.AfterSideTurnStart` awaits combat hook listeners sequentially, so separate vanilla poison powers do not run concurrently.

### Exact Accelerant mechanics and Q10 resolution

- `Accelerant` is a 1-energy Rare self-targeting Power card.
- It applies 1 `AccelerantPower`; its upgrade applies 2.
- `AccelerantPower` is a non-instanced Counter buff on the casting player's creature and has no behavior of its own. `PoisonPower` reads and sums it.
- Each player owns a separate Accelerant power instance. Repeated plays by that player stack the same instance.
- No vanilla code decreases or explicitly removes Accelerant before ordinary combat cleanup. The only direct vanilla application is the `Accelerant` card.
- Dead Accelerant owners are excluded from `PoisonPower.TriggerCount`; if they are alive again while their power remains, the game includes them again.
- **Q10 resolved:** sponsor slots persist for the combat, ordered by completed positive Accelerant applications. Before each poison sequence, filter slots to living owners and reconcile each owner's usable slot count to their live `AccelerantPower.Amount`. Any unexpected/mismatched extra trigger remains unsponsored and grants no Assisted Damage rather than guessing. No invented removal order is needed for vanilla v0.107.1.

### Exact central mutation and damage boundaries

- First power application flows through `PowerModel.ApplyInternal(Creature owner, decimal amount, bool silent = false)` after all given/received and multiplayer amount modifiers. For a new Poison/Accelerant instance, this is the exact point to observe the final initial amount.
- Stacking and every decrement flow through `PowerCmd.ModifyAmount(PlayerChoiceContext, PowerModel, decimal offset, Creature? applier, CardModel? cardSource, bool silent = false)`. Its returned `Task<int>` contains the final new amount after modifiers and removal checks.
- `PowerModel.SetAmount` is called only by the central power code in this build; no separate vanilla poison mutation bypass was found.
- Explicit power removal flows through `PowerModel.RemoveInternal()`/`PowerCmd.Remove`. A removed Poison power resets its entire contribution cycle.
- The preferred implementation hooks only first internal application, central amount modification, poison turn-start scope, and the existing central damage path. It will not patch every card/potion/relic/power implementation.
- One logical poison trigger can theoretically produce multiple `DamageResult` objects through HP redirection. Allocation must occur once per top-level poison `CreatureCmd.Damage` call after aggregating its results, not once per `AfterDamageGiven` callback. Nested damage caused by other hooks must be marked non-poison and left to normal tracking.

### Verified vanilla poison source coverage

Every vanilla `PoisonPower` application on this build routes through the central application paths and provides a player creature as `applier`:

| Category | Source | Behavior relevant to tracking |
| --- | --- | --- |
| Card | Deadly Poison | Direct targeted poison |
| Card | Poisoned Stab | Attack, then targeted poison |
| Card | Snakebite | Direct targeted poison |
| Card | Haze | Poison each hittable enemy |
| Card | Bouncing Flask | Repeated random-enemy applications |
| Card | Bubble Bubble | Adds poison only when target is already poisoned |
| Power | Corrosive Wave | Card play installs a player power; later card draws poison all hittable enemies using the power owner as applier |
| Power | Envenom | Powered attacks that deal unblocked damage apply poison using the power owner as applier |
| Power | Noxious Fumes | At the owner's side-turn start, poisons all hittable enemies using the power owner as applier |
| Potion | Poison Potion | Targeted poison using potion owner's creature as applier; card source is null |
| Relic | Twisted Funnel | First player turn poisons all hittable enemies using relic owner's creature as applier |
| Relic modifier | Snecko Skull | Adds 1 to the central final poison application amount; the observed positive delta naturally includes it without separate credit or double counting |

`OutbreakPower` observes positive poison applications and occasionally deals separate player-owned AoE damage. It does not apply or trigger poison, so that damage remains ordinary Damage Dealt and receives no Accelerant assist.

Pet/summon and modded sources are not present in the verified vanilla list, but they are covered when they use the central paths and their applier resolves through an explicit player or `PetOwner`. Unknown appliers remain unattributed under Q3.

### Save/continue finding

- `SerializableRun` contains run/map/player/history state but no live combat state, creatures, powers, or action queue.
- Leaving through the in-run pause menu returns to the main menu without creating a new live-combat save; it waits only for any already-running save task.
- The game saves after combat ends and the room is marked pre-finished. Therefore a Continue reload starts from the last vanilla checkpoint, not from the middle of the abandoned combat.
- The poison-cycle, fractional carry, per-cycle kill totals, and Accelerant sponsor ledgers must remain combat-only and must **not** be serialized. Existing RunStats pending-sidecar promotion rules already prevent abandoned-combat totals from becoming the restored checkpoint.

## Definitions

### Poison Applied

For one resolved, reliably player-owned operation and one enemy:

```text
poison_applied = max(0, poison_after - poison_before)
```

- Credit the positive observed change, not the value printed on a card, potion, relic, or power.
- Evaluate each affected enemy independently.
- A multi-target operation can add Poison Applied for every enemy whose poison increased.
- Reductions, trigger decay, removal, immunity, artifact/prevention, and failed applications add zero.
- A multiplier or transformation that increases existing poison credits only the actual positive delta it creates. Example: changing 6 poison to 18 credits 12 Poison Applied to the responsible player.
- Nested effects must be recorded once. If one card application causes a relic to add extra poison inside the same command chain, the final observed increase must not be double-counted.

### Actual poison damage

- Credit actual enemy HP removed by a verified poison trigger, capped by current HP and after any applicable block, redirection, prevention, or HP-loss modification.
- The sum credited across players for a poison trigger must never exceed the actual HP removed.
- The generic Damage Dealt tracker and poison-specific tracker must have an explicit deduplication boundary so the same HP loss is never counted twice.
- A poison trigger with no credited player share may still deal gameplay damage, but RunStats follows the approved unattributed-poison policy below.

### Contribution cycle

- A contribution cycle is scoped to one enemy and one continuous nonzero poison instance.
- The cycle begins on a transition from no poison/zero poison to positive poison.
- It ends immediately when the poison amount becomes zero, the poison power is removed, the enemy leaves combat, or combat ends.
- Poison applied after the cycle ends starts a new ledger with no ownership carried over.

### Accelerant-assisted damage

- The first poison trigger in the applicable turn-trigger sequence is the standard trigger and has no Accelerant sponsor.
- Every additional poison trigger caused by Accelerant has exactly one sponsor: the player whose Accelerant contribution occupies that ordered trigger position.
- Poison Damage Dealt on a sponsored trigger is still divided normally using the active enemy contribution cycle, including fractional carry and unattributed weight.
- The sponsor's Assisted Damage for that trigger is the sum of poison Damage Dealt credited on that trigger to all other players. The sponsor's own poison-damage share and every unattributed share are excluded.
- Accelerant does not create Poison Applied; it creates additional poison triggers.
- No Assisted Damage is recorded for an extra trigger that deals no actual HP damage.
- The ordered Accelerant sponsor list persists for the combat and is filtered against living owners and their live Accelerant amounts, matching the Phase 1 evidence and Q10.

## Proposed data model

### Run-level statistic

- Append `PoisonApplied` to `StatKind`; do not renumber existing values.
- Store it as the existing non-negative `long` mutation total per `Player.NetId`.
- Team Poison Applied is derived as the checked sum of player totals.
- Add a diagnostic for poison increases without reliable player ownership and a diagnostic for poison damage that cannot be safely identified or allocated.

### Combat-only poison ledger

Maintain one entry per live enemy/poison cycle:

```text
EnemyPoisonLedger
  enemy identity/reference
  observed poison amount
  credited applied weight by Player.NetId
  unattributed applied weight
  credited poison damage this cycle by Player.NetId
  exact deterministic fractional carry by Player.NetId
  cycle generation/token
```

- The ledger stores application weights, not a fabricated owner on `PoisonPower`.
- All ownership, poison-damage, applied, and fractional state is cleared at combat end and whenever the contribution cycle ends.
- It is not part of the public statistics UI.
- The weights are cumulative applications within the current nonzero poison cycle; ordinary decay or reduction above zero does not reduce them.
- Combat ledgers are not serialized because the verified game does not save/resume live combat. They are discarded with the abandoned combat and rebuilt empty when a new combat begins.

Maintain a combat-level ordered Accelerant sponsor ledger:

```text
AccelerantSponsorLedger
  ordered sponsor slots: [Player.NetId, Player.NetId, ...]
  verified active Accelerant amount/state by Player.NetId
```

- A regular Accelerant application contributes one sponsor slot; an upgraded application contributing two extra triggers contributes two adjacent slots.
- Later applications append their slots after earlier applications, preserving play order.
- The sponsor order applies consistently to every poisoned enemy affected by the game mechanic.
- The ledger is cleared at combat end. Vanilla Accelerant persists until combat cleanup; dead owners' slots are temporarily filtered because the game excludes dead owners from its live Accelerant sum. Unexpected live-count mismatches fail closed with unsponsored extra triggers.

## Proposed attribution pipeline

### 1. Observe poison changes centrally

Phase 1 identified the smallest central completed-operation hooks that cover:

- first application of `PoisonPower`;
- stacking an existing `PoisonPower`;
- direct amount replacement/multiplication;
- ordinary trigger decay;
- explicit reduction and removal;
- prevention or immunity;
- poison added by cards, potions, relics, powers, pets/summons, and generated effects.

The approved implementation boundary is:

1. Observe new Poison/Accelerant instances at `PowerModel.ApplyInternal`, after final application modifiers are calculated.
2. Wrap the returned `Task<int>` from `PowerCmd.ModifyAmount` to compare the previous amount with the final completed amount for existing instances.
3. Observe `PowerModel.RemoveInternal` to reset a removed poison cycle and reconcile unexpected Accelerant removal conservatively.
4. A positive poison delta is credited only when a player can be reliably resolved; a positive Accelerant delta appends one sponsor slot per final added stack.
5. A zero/negative poison result updates or resets ledger state without increasing Poison Applied.

This extends the project's existing completed `PowerCmd.ModifyAmount` observation pattern and adds the verified `PowerModel.ApplyInternal` first-application boundary.

### 2. Resolve the responsible player

The precise parameter names/order must be verified against the installed build. Resolution will use evidence in this order and will never guess:

1. Explicit player creature applier.
2. Pet/summon applier resolved through `PetOwner`.
3. Explicit card owner when the command provides a reliable card source.
4. Explicit potion owner, relic owner, or power owner/applier exposed by the verified central call or a narrowly scoped enclosing hook.
5. Otherwise unattributed, with a diagnostic.

The source category does not change the statistic: all reliable player-owned positive deltas feed that player's Poison Applied total and poison contribution weight.

### 3. Identify poison damage

- Establish a narrow async-local poison-trigger scope at `PoisonPower.AfterSideTurnStart`, then claim only top-level central damage calls made by that scope.
- Use the existing resolved-damage result/HP delta as the amount, not the displayed poison number.
- The installed build deals each poison hit first and decrements poison afterward. The damage allocation snapshot uses the contribution weights that existed for that trigger, before any cycle reset caused by the subsequent decay.
- Extra triggers in the same turn use the same ownership pool unless poison reaches zero between them.
- If a modifier causes poison to hit multiple times, each actual resolved hit is allocated independently.
- Aggregate all `DamageResult` values returned by one top-level poison damage command before allocation; do not mistake redirected or nested damage callbacks for additional Accelerant triggers.

### 4. Allocate poison damage

For actual poison HP damage `D`, player application weights `w[p]`, and optional unattributed weight `u`:

```text
denominator = sum(w[p]) + u
exact_share[p] = D * w[p] / denominator
```

- Only player shares are added to Damage Dealt.
- Any unattributed share remains uncredited so player totals cannot overstate provenance.
- Fractional entitlements carry forward exactly and deterministically. With three equal contributors, extra integer points rotate so no player repeatedly wins the rounding tie; equal recurring shares balance over a three-trigger cycle.
- Allocation must be deterministic across peers and iteration-order independent.
- Checked arithmetic or an overflow-safe equivalent is required; overflow fails closed and records a diagnostic.

### 5. Attribute poison-created kills

After allocating the lethal poison trigger, choose exactly one kill recipient using this hierarchy:

1. Among players with attributable poison participation in that enemy's current nonzero contribution cycle, keep the player or players with the highest credited poison damage in that cycle.
2. If tied, keep only the tied player or players with the highest Poison Applied to that enemy in that cycle.
3. If still tied, choose exactly one of those players using a deterministic pseudo-random calculation based on synchronized run/combat/enemy information and the tied player IDs.

The winner receives **Enemies Killed** and, when applicable, **Elite Enemies Killed** or **Bosses Killed**. Poison with no attributable player participation produces no player kill credit.

### 6. Attribute Accelerant-assisted damage

For each verified poison turn-trigger sequence on an enemy:

1. Mark trigger index 0 as standard and record no Accelerant assist.
2. Map extra trigger index 1 to sponsor slot 1, index 2 to sponsor slot 2, and so on.
3. Allocate the trigger's actual poison HP damage across poison contributors first.
4. Sum only the damage credited on that trigger to players other than the sponsor.
5. Add that sum to the sponsor's existing Assisted Damage total.

Example: A and B each receive 3 Damage Dealt from an A-sponsored extra poison trigger. A also receives 3 Assisted Damage for enabling B's damage. B receives no Assisted Damage for that trigger.

Example sponsor order: A plays upgraded Accelerant, then B plays regular Accelerant. Each applicable poison sequence is `standard, A, A, B`. A owns the first two extra-trigger assists and B owns the third.

The integration must derive the real standard/extra trigger boundary from verified game execution, not merely count arbitrary consecutive poison damage events.

## Worked scenarios

These scenarios become executable unit/integration tests after the open decisions are approved.

1. Player A applies 6 to an unpoisoned enemy: A gains 6 Poison Applied; the ledger is A=6.
2. Poison triggers for 6 actual HP loss: A gains 6 Damage Dealt. The game reduces poison to 5; the contribution cycle remains active.
3. With no new application, poison next triggers for 5 actual HP loss: A gains another 5 Damage Dealt.
4. An application is prevented: poison stays unchanged and no Poison Applied is credited.
5. A card changes poison from 6 to 18: the responsible player gains 12 Poison Applied and contribution weight 12.
6. One action applies 3 poison to two enemies: the player gains 6 Poison Applied total and each enemy has an independent ledger.
7. Poison is reduced to zero or removed, then Player B applies 4: the old ledger is discarded and the new ledger is B=4.
8. Poison deals more nominal damage than the enemy's remaining HP: allocate only actual HP removed.
9. A player-owned potion/relic/power/pet applies poison: credit its owning player when ownership is explicit.
10. An enemy/environment/unknown source applies poison: follow the approved unattributed-poison rule.
11. Two players contribute and a trigger produces a fractional split: carry the fractional entitlement forward and ensure credited damage does not exceed actual HP loss.
12. Three equal contributors repeatedly produce indivisible damage: rotate extra points so the distribution balances over a three-trigger cycle.
13. A poison trigger kills an enemy: allocate its Damage Dealt first, then select one kill recipient by poison damage, Poison Applied, and the approved random tie-break.
14. A and B own poison; A supplies one Accelerant trigger: both receive normal Damage Dealt shares, and A receives Assisted Damage equal to B's credited share.
15. A supplies upgraded Accelerant before B supplies regular Accelerant: each sequence is standard, A-sponsored, A-sponsored, B-sponsored.
16. Three poison contributors on a sponsored fractional trigger: Assisted Damage equals the integer poison damage actually credited to all non-sponsor teammates after fair fractional allocation.
17. An extra trigger has only sponsor-owned or unattributed poison damage: the sponsor receives zero Assisted Damage.
18. Poison reaches zero during an earlier trigger: later scheduled triggers deal zero and create no Damage Dealt or Assisted Damage.

## Persistence and compatibility

- Adding `PoisonApplied` changes the required `StatKind` total set. Increment the snapshot/sidecar schema version and add an explicit v1-to-v2 migration that initializes Poison Applied to zero while preserving all existing totals, card counts, identity, lifecycle, diagnostics, and revisions safely.
- Migration must not manufacture a live poison ownership ledger. The verified game does not resume live combat, so all combat-only ledgers start empty with each newly created combat state.
- Existing same-version save/continue behavior and atomic sidecar writes remain unchanged.
- Multiplayer remains peer-local and observational. Do not add custom `INetMessage` implementations or change `affects_gameplay: false`.
- A load/migration failure must leave the existing file untouched and fail closed.

## UI design

- Add **Poison Applied** as a row in the existing **DAMAGE** tab.
- Proposed order: Damage Dealt, Poison Applied, Damage Taken, Assisted Damage, Assisted Damage Prevented.
- Use the existing per-player columns, Team column, number formatting, responsive layout, and navigation behavior.
- Do not add a second poison-damage row; poison damage is included in Damage Dealt as requested.

## Test matrix

### Pure model and allocation tests

- Positive delta, zero delta, negative delta, removal, reapplication after zero.
- Single contributor across multiple turns.
- Multiple contributors with exact and fractional splits, including a three-player extra-point rotation that balances over three equal triggers.
- Deterministic tie handling independent of dictionary/peer order.
- Unattributed contribution mixed with player contributions.
- Multiplier/doubling and nested modifier deduplication.
- Multiple enemies and multi-target application.
- Overkill, block/HP-loss modification, repeated triggers, enemy death, and the poison-kill hierarchy.
- Overflow and invalid-player failure paths.

### State, UI, and persistence tests

- Poison Applied mutations, per-player isolation, Team total, row placement, and formatting.
- Snapshot round trip and v1-to-v2 migration.
- Wrong schema, malformed ledger/state, and atomic fail-closed behavior.
- Existing statistics remain unchanged except for the intentional poison addition to Damage Dealt.
- Existing Assisted Damage remains unchanged except for the intentional Accelerant-sponsored teammate poison damage.
- Release assembly still contains no custom network-message types.

### Runtime tests

- Representative verified poison card, potion, relic, power, and Snecko Skull modifier paths; simulated central-path pet/summon and unattributed sources.
- Regular/upgraded Accelerant, multiple Accelerant owners, later applications, ordering, removal if possible, and one/many poisoned enemies.
- Application prevented/modified by game mechanics.
- Single-player and two-player co-op contribution order permutations.
- Save/continue at every game-supported checkpoint relevant to combat.
- Map, defeat, and victory screen regression checks.
- Workshop copy disabled + local Release copy enabled; confirm only one RunStats instance loads.

## Phased implementation plan

### Phase 1 — Installed-build research and final hook proof

- Locate and verify the user's current STS2 installation, game version, assembly hash, mod directories, disabled Workshop state, and whether the game is running.
- Use the referenced MCP/decompiler against that installed build to inspect `PoisonPower`, every central poison mutation path, trigger order, damage call/results, removals, and save-in-combat behavior.
- Inspect `Accelerant` and `AccelerantPower`: application amount, upgrade behavior, duration/removal, multiplayer execution order, how the extra-trigger loop is produced, and whether any other vanilla effect adds poison triggers.
- Enumerate vanilla poison-producing cards, potions, relics, powers, pets/summons, and mechanics to build the runtime test set. The implementation must remain central-hook based so modded/unlisted sources also work when provenance is available.
- Update this document with exact signatures and evidence.
- Make no source or deployment changes. Report and stop for approval.
- **Completed 2026-09-08.** All research items above were completed without changing product/game/save/Workshop files. Live combat was not launched because decompilation proved the required mechanics and protected current run state did not need to be touched.

### Phase 2 — Pure statistic and poison-ledger model

- Add `PoisonApplied`, diagnostics, the poison and Accelerant ledgers, deterministic allocator, kill selector, assist calculator, and unit tests.
- No Harmony/runtime hooks, UI changes, installation, or Workshop changes.
- Run dependency-free tests. Report and stop for approval.
- **Completed 2026-09-08.** Appended `PoisonApplied = 20` without renumbering existing stats and appended three fail-closed poison diagnostics. Added an exact rational carry allocator, per-enemy nonzero-cycle ledger, approved kill selector, run-length-encoded Accelerant sponsor ledger, and Accelerant assist calculator.
- **Phase 2 verification:** 78/78 dependency-free model tests pass against the discovered game assembly path, including two-player alternation; one-extra and two-extra three-player rotations; changing cumulative shares; cycle reset; mixed unattributed poison; kill hierarchy/tie stability; Accelerant order, dead-owner filtering, and mismatch handling; and exclusion of self/unattributed assist.
- At Phase 2 completion, `PoisonApplied` was intentionally not yet placed in the visible UI or persisted with a new schema; those gated changes were completed in Phase 4 after Phase 3 established the runtime mutation path.

### Phase 3 — Runtime integration and deduplication

- Add the narrow verified poison mutation and trigger hooks.
- Wire reliable player resolution, lifecycle cleanup, actual HP-loss allocation, ordered Accelerant sponsorship, Assisted Damage, diagnostics, and generic Damage Dealt deduplication.
- Add integration-shaped tests for all verified source categories and multiplayer ordering.
- Build Debug and run the full automated suite. Do not install yet. Report and stop for approval.
- **Completed 2026-09-08.** Added post-mutation observation for new, stacked, decreased, and removed Poison; recorded final modified amounts so Snecko Skull and other central amount modifiers are naturally included. Added equivalent Accelerant application observation with self-owner validation.
- Scoped the exact vanilla `PoisonPower.AfterSideTurnStart` and central `CreatureCmd.Damage` call. The outer poison command is removed from generic dealer-less damage processing, while nested damage commands remain ordinary events. Returned `DamageResult` values are aggregated once per trigger before attribution.
- Runtime attribution now applies Poison Applied, Damage Dealt, Accelerant Assisted Damage, and approved poison kill credit; derives deterministic kill entropy without consuming game RNG; and clears combat-only poison/sponsor state on combat end and run cleanup.
- **Phase 3 verification:** Debug solution build succeeds with 0 warnings and 0 errors against STS2 v0.107.1. The complete dependency-free suite passes 81/81, including central source-category convergence, combined damage/assist/kill mutations, unattributed runtime behavior, fractional fairness, and sponsor reconciliation. `git diff --check` passes.

### Phase 4 — UI, schema migration, and documentation

- Add Poison Applied to the Damage tab.
- Implement schema/version migration and persistence changes required by the approved design.
- Update `README.md`, `RUNSTATS_SPEC.md`, and `MAINTENANCE.md` with definitions, limits, test evidence, and rollback notes.
- Run all tests and a Release build in the workspace. Do not install yet. Report and stop for approval.
- **Completed 2026-09-08.** Added Poison Applied directly after Damage Dealt in the Damage tab for player and derived Team columns. The UI now projects all 21 stored statistics plus Most Played Card.
- Advanced snapshot and sidecar schemas from 1 to 2. Strict migration accepts only complete schema-1 legacy data, preserves every existing total/card count/identity/lifecycle/revision/assisted-ownership value, inserts zero for Poison Applied and new poison diagnostics, and emits a current schema-2 snapshot. Incomplete schema-2 and unsupported schemas fail closed.
- Advanced the dormant fixed-layout snapshot protocol constant to 2; Release still excludes all custom RunStats network-message types and remains client-optional.
- Updated `README.md`, `RUNSTATS_SPEC.md`, and `MAINTENANCE.md` with poison definitions, limitations, current-machine paths, schema behavior, combat-only state, deployment gates, and rollback notes.
- **Phase 4 verification:** Debug and Release solution builds both succeed with 0 warnings and 0 errors. All 82/82 tests pass in both configurations, including schema-1 migration and incomplete schema-2 rejection. `git diff --check` passes. No files were installed into the game or Workshop directories.

### Phase 5 — Local Release installation and user playtest

- Reconfirm that Slay the Spire 2 is closed and the subscribed Workshop RunStats copy is disabled.
- Preserve the Steam-owned Workshop directory; never edit its files.
- Build and validate Release artifacts, build/verify the PCK, and deploy only RunStats-owned files to the game-local `mods/RunStats` folder using the verified game path and `-Configuration Release`.
- Verify installed hashes match workspace Release artifacts, no unexpected files are present, and only one RunStats instance is enabled/loaded.
- Provide a focused user playtest checklist covering single contributor, two/three contributors, reapplication after zero, multiplier, potion/relic/power source, regular/upgraded/multi-owner Accelerant, kill attribution, and save/continue.
- Stop for user test results and approval.
- **Release candidate installed 2026-09-08; user playtest pending.** Before deployment, STS2 was closed, the game-local `mods\RunStats` destination was absent, and settings contained only the disabled `steam_workshop` RunStats entry. The Steam-owned Workshop directory was read only and remains unchanged.
- Release build and all 82/82 tests passed with 0 warnings/errors. The pure-Python builder produced a Godot 4.5.1 format-2 PCK with exactly nine `runstats/localization/eng/*.json` entries and no warnings.
- The game-local destination contains exactly three files and no subdirectories: `RunStats.dll` SHA-256 `5A671E88783E690CD5259F2BA5C5F37B08A6B057AA19E70ACC300D0ECE44F53E`, `runstats.pck` SHA-256 `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, and `mod_manifest.json` SHA-256 `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA`. All installed hashes match their Release sources.
- The installed manifest retains `affects_gameplay: false`; the Release DLL contains no RunStats custom network-message markers. The local source has no disabling settings entry and is enabled by default, while the Workshop source has an explicit disabled entry. STS2's duplicate-source logic gives the local copy precedence.
- Per the approved phase plan, semantic version strings remain `0.1.0` in this test candidate and are changed consistently to `0.2.0` during Phase 7 Workshop readiness.
- **Single-player playtest results (2026-09-08):** Local loading, Damage-tab presentation, Poison Applied, potion/power attribution, and save/continue passed. Relic/other application sources, zero-cycle reset, poison multiplier behavior, multiplayer allocation, multiplayer Accelerant assistance, and multi-owner Accelerant ordering remain untested.
- **Confirmed lethal-trigger defect:** If poison kills an enemy, its actual remaining-HP damage is not added to Damage Dealt and the poison kill is not credited. The game awaits death processing inside `CreatureCmd.Damage`; death processing removes `PoisonPower`; `OnPowerRemoved` currently clears the enemy contribution cycle before the wrapped damage task returns and before RunStats allocates the returned `DamageResult`. Both observed failures therefore share one lifecycle-order cause.
- **Phase 6 coverage clarification:** Damage allocation must remain correct and conserved for any supported contributor count from one through four, including unequal shares and exact fractional carry/extra-point rotation.

### Phase 6 — Playtest fixes and regression validation

- Diagnose and fix approved issues found during the user's Release playtest.
- Repeat automated tests, Release build, safe local deployment, and relevant manual checks.
- Record final evidence and known limitations. Stop for approval.
- **Completed 2026-09-08.** Death cleanup inside `CreatureCmd.Damage` removes `PoisonPower` before returning its lethal `DamageResult`. The removal hook now defers only that enemy's cycle reset while its recognized poison damage command is active. The wrapper allocates the returned actual HP loss, selects and records the poison kill, and then clears the cycle in a `finally` path. Ordinary zero/removal and combat-end resets remain immediate.
- Added explicit allocation regressions for one through four contributors with unequal 1:2:3:4 weights, per-trigger damage conservation, and a four-player equal-share extra-point rotation. The lethal regression models 14 poison against 10 remaining HP, verifies 10 Damage Dealt plus one kill before reset, rejects allocation after reset, and verifies a fresh later cycle.
- Debug and Release builds succeed with 0 warnings and 0 errors; all 84/84 executable tests pass in both configurations. `git diff --check` passes.
- The Phase 6 Release candidate was deployed only to the game-local `mods\RunStats` directory after confirming STS2 was closed. It contains exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`. Installed hashes match the Release sources: DLL `5930E32CD502E014681D7681CCA58BBA596A689B63BEC865EA45C694B168EA03`, PCK `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, manifest `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA`.
- The subscribed Workshop files retain their Phase 1 hashes and remain untouched. Settings explicitly enable the `mods_directory` copy and disable the `steam_workshop` copy. Multiplayer and Accelerant behavior still awaits user testing; automated allocation and sponsor/assist tests pass.
- **Multiplayer playtest results received 2026-09-08:** Local/Workshop source selection, Damage-tab placement, single-contributor application and damage, zero-to-fresh-cycle reset, two-player `3/2` then `2/3` fractional allocation, three-player remainder rotation, two-player regular/upgraded Accelerant behavior, exactly-one-player poison kill credit, and save/quit/continue restoration all passed. Potion, relic, power, and Corrosive Wave draw-effect application also passed. Multi-owner Accelerant order (`standard, A, A, B`) remains manually untested; its deterministic automated sponsor-order coverage passes. The user accepted Phase 6 and approved all remaining phases.

### Phase 7 — Workshop deployment readiness

- Apply the approved semantic version consistently in the project and manifest.
- Update Workshop description/features, compatibility text, and `changeNote`.
- Preserve `workshop/RunStats/mod_id.txt` so the existing item `3797791393` is updated rather than duplicated.
- Back up/document the currently published Workshop content, package only `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`, and verify file list and hashes.
- Update repository deployment/rollback documentation and confirm `affects_gameplay: false` plus the no-custom-network-message invariant.
- Do **not** run the uploader or change Workshop visibility without separate explicit user approval.
- Report the upload-ready package and stop.
- **Completed 2026-09-08.** Synchronized project, assembly, manifest, and Workshop text at version 0.2.0; updated poison features, compatibility copy, and the release change note. The installed game remains the researched v0.107.1/commit `59260271` compatibility target.
- Preserved the complete subscribed v0.1.0 production package under `workshop\RunStats\rollback\v0.1.0`; all three hashes match the documented production baseline. Preserved `mod_id.txt` value `3797791393` and public visibility metadata so a later authorized upload updates the existing item.
- Rebuilt Debug and Release with 0 warnings/errors and passed all 84/84 executable tests in both configurations. The Release assembly has version 0.2.0.0 and contains no custom RunStats network-message types; the manifest retains `affects_gameplay: false`.
- Packaged exactly three v0.2.0 files under `workshop\RunStats\content`, matching Release sources: DLL `5C6D8FB363E2E38D67B392500AFCBDEE324D69B5C5975C6BCBF9A26A037D44C0`, PCK `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D`, manifest `F4D3A9D3C0F06DAE6DA8CC6B9223763AE0B0BAEBD2BF50D0EB75D67CD36EE7C9`. No uploader was run and the Steam-managed subscribed directory was not modified.
- Updated the game-local test installation to the exact final v0.2.0 package after confirming STS2 was closed. Source, package, and installed hashes match; the subscribed Workshop v0.1.0 copy remains disabled and unchanged.

## Safety invariants

- Never edit the Steam-managed subscribed Workshop copy.
- Never allow active local and active Workshop RunStats copies at the same time.
- Never modify vanilla saves or unrelated mods.
- Never launch the game, replace installed mod files, or upload to Workshop while the game is running.
- Preserve the existing Workshop item ID file and a rollback copy of the last published package.
- Optional tracking failures must not change gameplay. Catch/log once, increment diagnostics where safe, and fail closed.
- No per-frame polling and no blanket patching of every model hook.

## Design decisions

### Q1 — Contribution weight lifetime

- **Approved:** Use cumulative applied amounts for the whole nonzero cycle. A=6 then B=4 gives A 60% / B 40% until more poison is applied or poison reaches zero, even though the visible stack decays.

### Q2 — Fractional poison-damage allocation

- **Approved:** Remember fractional entitlements between poison triggers. Extra integer points rotate fairly rather than repeatedly favoring one player.
- **Three-player requirement:** With three equal contributors, the extra-point allocation must balance over a three-trigger cycle. Tests must cover both one-extra-point and two-extra-point cases.

### Q3 — Poison without reliable player ownership

- **Approved:** If an increase in an enemy's poison cannot be attributed to a player, assign no Poison Applied for that increase. Preserve it as an unattributed contribution weight only to ensure its later damage share is also assigned to no player, including when mixed with attributable poison.

### Q4 — Poison-created kill credit

- **Revised approved hierarchy:** First compare poison damage dealt to that enemy. If tied, compare Poison Applied to that enemy. If still tied, randomly choose exactly one tied player.
- The selected player receives the applicable regular, elite, or boss kill credit. Exactly one player is credited, so a poison kill increases the Team kill total by at most one.

### Q5 — Version for the test/Workshop-ready release

- **Approved:** `0.2.0`.

### Q6 — Local game installation

- **Approved:** Discover the current Slay the Spire 2 installation through Steam during Phase 1. The stale paths recorded for another machine must not be used as deployment targets.

### Q7 — Kill-comparison window

- **Approved:** Compare poison damage and Poison Applied only within the enemy's current continuous nonzero poison cycle. When poison reaches zero or is removed, both kill-comparison totals reset along with contribution percentages.

### Q8 — Multiplayer-safe random tie-break

- **Approved:** Select the final tied winner with a deterministic pseudo-random calculation based on synchronized run/combat/enemy information and tied player IDs. It must produce the same winner on every peer and must not consume or alter the game's combat RNG.

### Q9 — Assisted Damage contents

- **Approved:** An Accelerant sponsor receives Assisted Damage equal only to poison damage credited to other players on that extra trigger. The sponsor receives no assist for their own poison share or for unattributed poison.

### Q10 — Accelerant sponsor-list lifetime

- **Resolved by installed-build evidence:** Vanilla Accelerant is not reduced or explicitly removed before combat cleanup. Sponsor slots persist for the combat in application order, are filtered while their owner is dead because the game excludes dead owners, and become active again if the owner is alive with the power still present. Unexpected count mismatches produce unsponsored triggers and no assist rather than an invented ownership/removal rule.

### Q11 — Other extra-trigger effects

- **Approved:** Limit extra-trigger Assisted Damage specifically to Accelerant for version 0.2.0. Other vanilla or modded effects that cause extra poison triggers still receive normal poison Damage Dealt allocation but create no Assisted Damage under this feature.

## Approval record

- Draft created: 2026-09-08.
- Clarification answers: Q1–Q11 resolved. Phase 1 installed-build evidence is recorded above.
- Initial design approval: granted on 2026-09-08 for Phase 1.
- Phase 1 evidence/refinements and Phase 2 continuation approval: granted on 2026-09-08.
- Phase 1 research: complete.
- Phase 2 pure model implementation: complete.
- Phase 3 runtime integration and deduplication: complete.
- Phase 4 UI, schema migration, and documentation: complete.
- Phase 5 Release build, PCK creation, local installation, artifact verification, and single-player playtest: complete; lethal poison damage/kill regression identified, with remaining multiplayer cases deferred until available.
- System-wide build prerequisites: installed and verified with user approval on 2026-09-08.
- Phase 6 approval and acceptance: granted on 2026-09-08 after the completed single-player and multiplayer playtests.
- Phase 7, final commit, and pull-request creation: approved on 2026-09-08.
- Phase 7 Workshop-readiness: complete on 2026-09-08.
- Version 0.2.0 publication: completed on 2026-09-09 to existing public Workshop item `3797791393`, together with the enemy-damage-only Block Lost correction and a change note covering both features.
