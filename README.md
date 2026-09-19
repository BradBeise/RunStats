# RunStats

RunStats is a Slay the Spire 2 mod that tracks complete-run, per-player statistics in single-player and co-op. Version 0.3.0 is public as Steam Workshop item `3797791393`; it adds Doom application, proportional Doom-kill damage, and Doom kill credit. The `WeakVulnTweaks` branch contains the verified, packaged v0.3.1 candidate with proportional Weak/Vulnerable assistance and signed Strength attribution; it has not been uploaded or replaced the published package.

## Compatibility

- Slay the Spire 2 public/default Steam build `23811903`
- Game version `0.107.1`, commit `59260271`
- Godot 4.5.1 C# and .NET 9

The project compiles against the exact `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll` shipped with the installed game. It deliberately does not copy those assemblies into the mod. `affects_gameplay` is disabled and the release assembly contains no custom network-message types, so unmodded friends can join a RunStats user's co-op game.

## Build

1. Install a .NET SDK capable of targeting .NET 9.
2. Confirm `Sts2DataDir` in `src/RunStats/RunStats.csproj` points to the installed game's `data_sts2_windows_x86_64` directory, or override it with `-p:Sts2DataDir=<path>`.
3. Run `dotnet restore RunStats.sln --configfile NuGet.config`, then `dotnet build RunStats.sln --configuration Debug --no-restore`.
4. Build `runstats.pck` from `src/RunStats/RunStats` into `src/RunStats` using the STS2 modding resource-pack builder.

Builds never deploy automatically. After the approved first deployment, `tools/Deploy-RunStats.ps1` copies only `RunStats.dll`, `RunStats.pdb`, `runstats.pck`, and `mod_manifest.json` into the dedicated `mods/RunStats` directory and refuses to run while the game process is active. The build's `.deps.json` and `.runtimeconfig.json` stay in the workspace because STS2 treats every JSON in a mod directory as a manifest.

## Debugging

The included VS Code launch configuration attaches the .NET debugger to a selected Slay the Spire 2 process. `RunStatsConfig.EnableDebugLogging` defaults to `false`; normal initialization and errors still use the game's logging system with a `[RunStats]` prefix.

The model and statistic-tracker tests are dependency-free and run with `dotnet run --project tests/RunStats.Tests/RunStats.Tests.csproj --configuration Debug --no-restore` after the solution restore.

## Run and player model

`RunStatsState` owns one active run at a time. A canonical `RunIdentity` combines seed, mode, profile, UTC start time, and sorted distinct player NetIds. Positive typed mutations are accepted only for known players in an active run; Assisted Damage and Damage Prevented additionally accept nonzero signed mutations. Invalid, ambiguous, and overflowing mutations fail closed without partially changing state, including atomic multi-player Strength awards. Every accepted mutation advances the run revision once, while each affected player has an independent revision. Snapshots are detached read-only copies with schema version 4, per-player totals, exact card-play counts, diagnostics, and derived team totals.

## Core combat tracking

RunStats starts and clears its in-memory state with the game run lifecycle and subscribes to each player's creature events. It records resolved Damage Dealt/Taken, Healing Done (healing received by that player), Max HP Gained, Block Gained/Lost, and uniquely credited normal/elite/boss kills. Damage is attributed only to an explicit player or pet owner; source-less indirect damage is deliberately omitted and diagnosed. Replay tracking remains disabled.

## Assisted statistics

RunStats tracks each player's cumulative positive Weak and Vulnerable applications separately for every enemy. Natural decay and other nonzero reductions do not change contribution weights. Reaching zero, explicit removal, or combat cleanup starts a fresh cycle. Unknown applications remain in an unattributed denominator rather than being assigned to a player.

Vulnerable Assisted Damage uses STS2's exact live multiplier and actual Block, HP loss, integer truncation, and overkill boundaries. Each resulting assist pool is divided by cumulative Vulnerable weights. The attacker's own share and unattributed shares are discarded without redistribution.

Weak Damage Prevented is measured independently for every player target before that player's Block or other defender mitigation. All target values from one enemy damage command are summed and allocated once by cumulative Weak weights. Each contributor's gross award is then split between self-protection and teammate protection; only the teammate portion is credited. Fractional self-splits carry across attacks until the Weak cycle resets.

Both effects round contributor shares down and distribute indivisible points through a rotating reverse-application order. Every positive application moves its contributor to the front and restarts the remainder cursor. Events involving an applied damage cap, or Vulnerable events involving unprovable HP-loss/redirection overrides, remain omitted rather than estimated.

Strength attribution records each reliably player-caused signed Strength change as a separate combat-local event for every non-self creature affected. It observes cards, potions, relics, powers, draw/discard/play reactions, and player-triggered monster reactions from their final before/after Strength change. Temporary events expire with their linked restoration source; explicit Strength reset, entity removal, and combat end clear the relevant events. Unknown source or lifetime information is never guessed.

For a player's powered attack, RunStats measures each target/hit after Block, HP limits, overkill, and integer truncation, removes Vulnerable's already credited layer, and gives signed Assisted Damage to external Strength events. For an enemy powered attack, it measures pre-Block damage for every player hit, removes Weak's layer, excludes that protected player's own event, and gives signed Damage Prevented. Harmful events are applied first and subtract from the statistic; otherwise earlier Strength events receive credit first on every hit. Incoming hits reduced to zero use the captured pre-modifier attack value so repeated zero-damage hits still receive exact prevention credit. Unsupported damage caps, redirection/HP-loss ambiguity, and unknown additive or multiplicative modifier stacks fail closed.

## Poison tracking (v0.2.0)

Poison Applied records each player's actual positive change to an enemy's Poison power after game modifiers. Cards, potions, relics, delayed powers, pets, and other effects share the same central tracking path when the applying player is reliable. Unknown applications remain as an unattributed contribution weight so neither their Poison Applied nor their later damage is credited to a player.

Poison damage is added to each contributor's existing Damage Dealt total according to cumulative poison applied since that enemy was last at zero. Exact fractional entitlements carry between triggers, so indivisible points rotate fairly across two or more contributors. Reaching zero or removing Poison starts a fresh contribution cycle.

Accelerant's first poison trigger is standard. Extra triggers are sponsored in Accelerant application order; upgraded Accelerant contributes two adjacent positions. The sponsor receives Assisted Damage equal only to teammates' credited poison damage on that extra trigger, excluding the sponsor's own and unattributed shares. Poison kills use current-cycle credited poison damage, then Poison Applied, then one deterministic pseudo-random tied winner without consuming game RNG.

## Doom tracking (v0.3.0)

Doom Applied records positive changes to an enemy's Doom power from cards, potions, relics, and other sources. When Misery copies Doom, the player who played Misery receives credit for the copied amount. Applications with no reliable player source remain unattributed.

Doom contributes to Damage Dealt only when it kills an enemy. The HP actually removed by Doom is divided according to each player's share of Doom applied to that enemy, with unattributed Doom kept in the denominator. For example, 70 and 30 Doom against an enemy with 10 HP remaining award 7 and 3 Damage Dealt. The largest Doom contributor gets the kill; tied contributors are compared by damage dealt to that enemy, then selected deterministically. Removing Doom without a kill starts a new contribution cycle.

## Cards, economy, and items

RunStats counts each completed card execution, including autoplay and each card Replay execution. It tracks successful permanent card additions, upgrade levels gained, and removals; actual gold gained and spent; and successful non-starting relic/potion acquisitions and potion uses. Ordinary gold loss, theft, returned stolen gold, generated combat-only cards, failed potion procurement, discarded potions, and starting inventory are excluded.

Per-card play counts use canonical card IDs. Most Played Card selects the highest count and resolves ties by ordinal card ID, making the result deterministic across peers. Damage/healing by source and relic/potion contribution breakdowns remain unimplemented because STS2 does not carry reliable provenance for every event.

## Run statistics screen

During an active run, a chart icon is added beside the top-right Options control with native-style hover/pressed feedback. It opens an overlay with tabs for Damage, Healing / Block, Kills, Cards, Economy, Relics, and Potions. Doom Applied appears directly below Poison Applied in the Damage tab. Most Played Card uses the game's native card visual. Player columns use platform usernames when available, multiplayer adds derived Team totals, and deterministic player labels remain the fallback. The screen scales across the supported narrow/default/wide bounds and closes with its button or the game's standard keyboard/controller back actions. The icon hides on the map and while ordinary overlays or modals own interaction, but remains available on the built-in defeat and victory screens.

## Multiplayer synchronization

RunStats derives synchronized game events locally on each peer that has the mod and attributes them by actor/owner `Player.NetId`. It deliberately registers and sends no custom multiplayer messages, which keeps the mod client-optional and avoids forcing friends to install it. Each installed peer maintains its own observational statistics and persistence; RunStats does not claim cross-peer reconciliation after a disconnect or rejoin.

## Persistence

RunStats stores only its own versioned JSON sidecars under the active profile's `com.bradbeise.runstats` directory. Single-player and multiplayer use separate active and pending files. Mutation-driven pending snapshots are debounced and never treated as restorable checkpoints; an active sidecar is promoted only after STS2 reports that the corresponding vanilla save succeeded. Every installed peer may restore its own matching local sidecar. Restore requires an exact composite run identity and exact vanilla save timestamp, then conservatively merges history-backed totals by maximum value. The opening Ancient/Neow history entry is excluded from Healing Done because STS2 records initial HP there as healed rather than as an in-run heal.

Writes use a flushed temporary file followed by atomic replacement. The combined branch advances snapshots and sidecars to schema 4: schema 3 supplies Doom Applied, while schema 4 permits negative values only for Assisted Damage and Damage Prevented. Complete schema-1 and schema-2 files initialize missing Doom Applied to zero; schema-1 migration also initializes Poison Applied and poison diagnostics. Legacy schemas reject negative totals. Poison, Accelerant, Weak, Vulnerable, Strength, and Doom contribution state remains combat-only, while finalized totals persist. Legacy assisted-ownership records are validated so compatible totals can restore, then discarded rather than reinterpreted; newly written sidecars leave that collection empty.

Malformed, oversized, unsupported-schema, incomplete-current-schema, wrong-run, and wrong-checkpoint files fail closed without changing the in-memory state or vanilla saves. Completed, defeated, victorious, and abandoned runs are archived in the RunStats-owned archive directory. Each installed peer derives events and restores its own matching local sidecar; no custom RunStats network messages are registered or sent.

## Project specification

The foundational requirements and historical releases are maintained in `RUNSTATS_SPEC.md`. The approved proportional Weak/Vulnerable design is in `WEAK_VULNERABLE_ATTRIBUTION_SPEC.md`, and the signed Strength design is in `STRENGTH_ATTRIBUTION_SPEC.md`. Unattributed contribution remains uncredited instead of being guessed. RunStats writes only its own sidecars and dedicated mod artifacts; it does not alter vanilla game files or unrelated mods.

Future development, duplicate-install prevention, versioning, testing, packaging, Workshop updating, and rollback locations are documented in `MAINTENANCE.md`.
