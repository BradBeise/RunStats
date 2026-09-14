# Changelog

## 0.3.0 — 2026-09-14

- Add Doom Applied immediately below Poison Applied. Positive Doom from cards, relics, potions, and other sources is credited to the applying player. Doom copied by Misery is credited to the player who played Misery.
- When Doom kills an enemy, split the HP it actually removes among contributors in proportion to Doom applied. Unattributed Doom remains in the denominator. The largest Doom contributor receives the kill; ties use damage dealt to that enemy, then a deterministic selection.
- Migrate complete v0.1.0 and v0.2.0 sidecars to schema 3, initializing Doom Applied to zero while preserving existing totals.

## 0.2.0 — Poison tracking and Block Lost correction

- Add Poison Applied, contributor-based Poison Damage Dealt and kill credit, and Accelerant Assisted Damage.
- Count Block Lost only when enemy damage absorbs Block.

## 0.1.0 — Initial release

- Add per-player run statistics, the in-game statistics screen, and RunStats sidecar persistence.
