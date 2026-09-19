# RunStats v0.3.1 Workshop update draft

This is the reviewed copy synchronized into `workshop/RunStats/workshop.json` during Strength Phase 9. Version 0.3.1 was published to the existing public Workshop item `3797791393` on 2026-09-18.

## Proposed description

```text
[h1]RunStats[/h1]
Track detailed per-player statistics throughout your current Slay the Spire 2 run.

[h2]Features[/h2]
[list]
[*]Seven focused tabs with per-player and Team totals in co-op
[*]Poison Applied tracking, proportional poison damage and kill credit, and Accelerant assistance
[*]Doom Applied, proportional Doom-kill damage, Misery attribution, and Doom kill credit
[*]Proportional Weak and Vulnerable assistance for contributions from up to four players
[*]Signed Strength assistance from cards, potions, relics, powers, and player-triggered reactions
[*]Exact per-hit Strength handling for repeated and multi-target attacks, including zero-damage hits, Block, HP limits, overkill, Weak, Vulnerable, and self-benefit exclusion
[*]Block Lost counts Block absorbed by enemy damage without counting end-of-turn clearing
[*]Most Played Card, Steam player names, and defeat/victory screen access
[*]Safe save-and-continue support using separate RunStats sidecar files
[/list]

[h2]Assisted statistics[/h2]
Weak and Vulnerable credit is divided by cumulative applications while each enemy debuff remains nonzero. Indivisible points rotate fairly; self-benefit and unknown ownership are not credited.

Player-caused Strength changes are tracked separately for each affected creature and their actual lifetime. Helpful Strength adds to assisted totals and harmful Strength subtracts, so Assisted Damage and Damage Prevented can be negative. Strength credit is calculated independently for every resolved hit and target.

[h2]Multiplayer[/h2]
RunStats is client-optional and does not affect gameplay. Friends without RunStats can join your game. Attribution supports one through four players and is derived locally without custom network messages.

[h2]Compatibility[/h2]
Slay the Spire 2 public branch v0.107.1
RunStats v0.3.1

Slay the Spire 2 is in Early Access. A future game update may require a RunStats compatibility update.
```

## Proposed change note

```text
RunStats v0.3.1 adds proportional Weak and Vulnerable attribution for multiple contributors and signed Strength assistance. Reliably owned Strength changes now contribute per hit to Assisted Damage or Damage Prevented, including repeated hits reduced to zero, with self-benefit excluded and exact Block/HP/Weak/Vulnerable boundaries. Save data advances to schema 4 while preserving compatible v0.1.0, v0.2.0, and v0.3.0 totals.
```

## Required release review

- Preserve the recorded distinction between live passes, user-accepted automated verification, and deferred multiplayer cases.
- Keep project, assembly, manifest, description, and change note synchronized at version 0.3.1.
- Preserve `workshop/RunStats/mod_id.txt` with item ID `3797791393` and the published rollback package.
- Package exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`.
- The 2026-09-18 upload used the user's explicit release request and retained public visibility.
