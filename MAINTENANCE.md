# RunStats maintenance and release guide

## Canonical locations

- Source repository: `C:\Users\Mike Major\Desktop\RunStats`
- Historical v0.1.0 package: `C:\Users\Mike Major\Desktop\RunStats\workshop\RunStats\rollback\v0.1.0`
- Game-local development destination: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats`
- Workshop upload workspace: `C:\Users\Mike Major\Desktop\RunStats\workshop\RunStats`
- Subscribed production copy: `D:\SteamLibrary\steamapps\workshop\content\2868840\3797791393`
- Mega Crit uploader: `C:\Users\Mike Major\Desktop\RunStats\tools\mod-uploader-v0.2.0`
- Workshop item: `https://steamcommunity.com/sharedfiles/filedetails/?id=3797791393`
- Current Steam profile settings root: `C:\Users\Brad Beise\AppData\Roaming\SlayTheSpire2\steam\76561198407892354`

Never delete `workshop\RunStats\mod_id.txt`. It binds future uploads to Workshop item `3797791393`; without it, the uploader creates a different item.

## v0.1.0 rollback baseline

These hashes preserve the previously published v0.1.0 release for rollback.

The preserved v0.1.0 package contains exactly:

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA` |
| `RunStats.dll` | `7283EB1E106E160305EF355D67AC711FE80D7BABF9618C34D1BCE4671C6CF070` |
| `runstats.pck` | `2E075CFCFAE4EE3CBDC824CB0040CFD3FFDF4920666C93C7165324C7797E58D1` |

The historical external local-test backup from the previous machine was the final Debug build. Its DLL SHA-256 is `06D3B1B222CE30BABAE848972B365917DEA573118CCB53F7F27CD72AC90B43B0`; its PDB SHA-256 is `E3F6E3E82EFDDC45D6753B32C4685A31D92C853C433E08E57CC58EB7B0749C7A`. Its presence on this machine is not assumed; the Steam Workshop hashes above are the verified rollback baseline.

The complete published v0.1.0 three-file package is also preserved in `workshop\RunStats\rollback\v0.1.0`. Its hashes match the subscribed production baseline above. Do not pass the `rollback` directory to the uploader.

## Avoiding duplicate installations

STS2 must never see both the local and Workshop copies of the `runstats` manifest at the same time.

- For normal play, keep the game-local `mods\RunStats` path absent and stay subscribed to item `3797791393`.
- Before local Debug testing, close the game, unsubscribe from or fully disable the Workshop copy, confirm it will not load, and deploy the Debug build with `tools\Deploy-RunStats.ps1`.
- After testing, close the game and move the entire local `RunStats` directory back outside the game's recursively scanned `mods` tree before re-enabling or resubscribing to the Workshop item.
- Do not edit files inside Steam's `steamapps\workshop\content` directory. Steam owns and may replace them.

The current machine has the Workshop item installed but disabled in `settings.save`. The game-local `mods\RunStats` directory contains the earlier Weak/Vulnerable Release candidate. Strength Phase 7 must reconfirm that STS2 is closed, the local/folder source is enabled, and the subscribed Workshop source is disabled before replacing that local candidate.

## v0.2.0 upgrade notes

- Snapshot and sidecar schema 2 add Poison Applied and three poison diagnostics. Schema-1 sidecars are accepted only with the complete legacy field set, then migrated by inserting zero for new fields. Existing totals are preserved; historical poison is not reconstructed.
- The dormant custom snapshot protocol constant is 2 because the fixed statistic/diagnostic wire layout changed. Release builds continue to exclude custom RunStats network message types.
- Poison contribution weights, exact fractional carries, per-cycle kill comparisons, and Accelerant sponsor order are combat-only. They are intentionally absent from sidecars and reset at combat end. Only finalized run totals persist.
- Block Lost now records only Block absorbed by enemy damage. End-of-turn clearing and other non-enemy Block reductions are excluded; existing saved totals are preserved rather than reconstructed.
- If rollback to v0.1.0 is required, close the game and use the preserved `workshop\RunStats\rollback\v0.1.0` package through a separately approved release or local-test workflow. A schema-2 sidecar is not readable by v0.1.0; preserve it for diagnosis or archive it inside the RunStats-owned data directory rather than editing vanilla saves.

## v0.2.1 Weak/Vulnerable upgrade notes

- Weak/Vulnerable attribution originally retained schema 2 because no finalized field changed. The combined Strength update advances v0.2.1 to schema 3 so the existing assisted totals can safely become signed.
- Weak and Vulnerable cumulative weights, reverse-application remainder cursors, unattributed shares, and Weak self-split fractional carries are combat-only. STS2 does not restore an active combat checkpoint, so these values reset at combat/run boundaries and are intentionally absent from new sidecars.
- The schema-2 `assisted_ownership` property remains structurally accepted for backward compatibility. Valid legacy records are logged and discarded after their accumulated totals restore; they are never converted into weighted contributions. New active, pending, and archive sidecars always emit an empty collection.
- Vulnerable allocates each actual Block/HP/overkill-aware assist event once. Weak calculates prevention per attacked player, sums one command pool, allocates it once, and then removes each contributor's proportional self-protection share with exact carry across the active Weak cycle.
- The Weak/Vulnerable Phase 6 candidate remains the installed local copy until Strength Phase 7. Workspace builds must not replace either it or the disabled subscribed copy before that approval gate.

## v0.2.1 Strength and schema-3 upgrade notes

- Schema 3 permits negative totals only for Assisted Damage and Assisted Damage Prevented. All other totals remain nonnegative. Complete schema-2 sidecars migrate all existing nonnegative totals unchanged; complete schema-1 sidecars additionally initialize Poison Applied and its diagnostics to zero.
- RunStats v0.2.0 cannot read schema-3 sidecars. Before any rollback, close the game and preserve schema-3 active/pending files inside the RunStats-owned data directory for diagnosis or future restoration; never edit vanilla saves to force compatibility.
- Signed Strength awards are committed atomically across all credited players for each hit. Overflow or malformed allocation rejects the whole Strength award without partially changing player totals.
- Strength impact events, source links, lifetimes, and ordering are combat-only. They reset on entity/reset/combat boundaries and are not reconstructed from sidecars or network traffic. Only finalized signed assisted totals persist.
- Weak and Vulnerable keep their proportional cumulative-weight behavior. Strength uses separate signed events and earliest-event allocation; these models must not be merged or serialized as shared ownership metadata.
- The Release assembly remains client-optional: `affects_gameplay` is `false`, and custom `INetMessage` implementations remain excluded.

### Phase 5 local Release candidate

Installed 2026-09-08 into `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats` after confirming STS2 was closed and the Workshop source was disabled. The candidate contains exactly:

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA` |
| `RunStats.dll` | `5A671E88783E690CD5259F2BA5C5F37B08A6B057AA19E70ACC300D0ECE44F53E` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

All hashes match workspace Release sources. The Workshop directory remains Steam-owned and untouched. The candidate is a Release-configuration build but intentionally retains the 0.1.0 manifest/assembly version until the coordinated Phase 7 version update.

### Published v0.2.0 Workshop package

Published 2026-09-09 from `workshop\RunStats\content` to existing public item `3797791393` after the approved poison playtests and Block Lost correction. The existing item ID was retained; no duplicate was created.

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974` |
| `RunStats.dll` | `E47DE7491555156FE952739E89006C82E75733F8357962C649C2D61852172CB8` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

The manifest and assembly versions are both 0.2.0, `affects_gameplay` remains `false`, and the Release project excludes all custom RunStats network-message types. The uploader workspace retains public visibility and `mod_id.txt` value `3797791393`. Steam reported a successful 158,085-byte update; its public API confirmed the item remains public with both new features in the description, and the public change-notes page contains both entries.

The earlier game-local `mods\RunStats` test installation was the pre-publication poison candidate. Immediately after publication, Steam's local subscribed cache still contained v0.1.0; allow Steam to download v0.2.0 before using the subscribed copy, and do not enable it alongside a local test installation.

## Published v0.3.0 Doom update

- Doom Applied appears immediately below Poison Applied. Misery's copied Doom is credited to its player. A Doom kill adds only the HP actually removed to Damage Dealt, divided by applied-Doom shares; the largest Doom contributor gets the kill, with damage to that enemy and then deterministic selection breaking ties.
- Snapshot and sidecar schema 3 adds Doom Applied. Complete schema-1 and schema-2 sidecars migrate with Doom Applied set to zero and all earlier totals preserved. The dormant fixed-layout snapshot protocol is 3; Release still excludes custom network-message types.
- The user confirmed the local Doom playtest passed. The game-local `mods\RunStats` test folder was removed after that confirmation, with the game closed.
- `workshop\RunStats\rollback\v0.2.0` preserves the published three-file v0.2.0 package, including its hashes. Keep `mod_id.txt` unchanged so the v0.3.0 upload updates item `3797791393`.
- Mega Crit's uploader successfully updated the existing public Workshop item `3797791393` on 2026-09-14. Steam's public item-details API reported success, public visibility, file size 168,836 bytes, updated time 20:04:02 UTC, and a description containing v0.3.0 and Doom Applied. The public change-notes page also shows the v0.3.0 Doom entry. The subscribed cache still held v0.2.0 immediately afterward; wait for Steam to download the updated copy before re-enabling it.

The published v0.3.0 package contains exactly these three files. The Release build has 0 warnings/errors and the executable suite passes 88/88. The assembly version is `0.3.0.0`, the manifest version is `0.3.0`, the existing item ID is `3797791393`, and the thumbnail is 735,516 bytes.

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `0B2EE0AAB26CB7F41DAD124630B4F7F82FDBD953B8589E30A6E577C5C0D2CD98` |
| `RunStats.dll` | `C3AA0F15380843302F745316BCFE08CCBB7981465B3AB241A51A3FB5415D0637` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

### Strength Phase 6 validated workspace candidate

Validated 2026-09-10 without installing, packaging, or changing Workshop content. Debug and Release each pass all 137 tests; both solution builds finish with zero warnings and errors. The Release assembly contains no custom network-message implementation, and the source manifest still declares `affects_gameplay: false`.

| Future package source | SHA-256 |
| --- | --- |
| `RunStats.dll` | `81B1BB3994AA2C01656D99BCE1B5A4168C0192644066B90779FF5070E5ED94BA` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974` |

This candidate predated the v0.3.0 Doom merge and is retained only as historical validation evidence. The next candidate must be rebuilt from the combined branch before testing resumes. `tools\Package-RunStatsWorkshop.ps1` names exactly three package sources and rejects an unexpected output count. The existing Workshop content, `workshop.json`, and `mod_id.txt` remain unchanged.

### Strength Phase 7 local Release candidate

Installed 2026-09-10 after confirming STS2 was closed, local `runstats` was enabled, and Workshop `runstats` was disabled. The local directory contains exactly the three files below, with installed hashes matching the Phase 6 workspace sources:

| File | SHA-256 |
| --- | --- |
| `RunStats.dll` | `81B1BB3994AA2C01656D99BCE1B5A4168C0192644066B90779FF5070E5ED94BA` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `83A482429AF9F103D674B8FACF6DB92C7271437C0F061EA163A4136FDA5BF974` |

At installation time the Steam-managed Workshop DLL remained the published v0.2.0 artifact. This local candidate predates the v0.3.0 Doom merge and must be replaced by a combined build before further testing. Live Strength and deferred multiplayer Weak/Vulnerable evidence is pending; do not treat automated coverage as a passed playtest.

### Strength Phase 7 v0.3.0 merge refresh

On 2026-09-14, `WeakVulnTweaks` was fast-forwarded to `origin/main` commit `b1bdf4c`, all Doom/Strength/Weak/Vulnerable conflicts were resolved, and snapshot/sidecar schema 4 was introduced to distinguish signed-assisted files from public v0.3.0 schema 3. Debug and Release builds completed with zero warnings/errors and all 141 tests passed in both configurations. With STS2 closed, the combined Release candidate replaced the local test copy; source and installed hashes match:

| File | SHA-256 |
| --- | --- |
| `RunStats.dll` | `0C54F35369228DAA17194D4310FC562F75200028DADBCA0328A6A476CEF521D7` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `0B2EE0AAB26CB7F41DAD124630B4F7F82FDBD953B8589E30A6E577C5C0D2CD98` |

The local candidate deliberately retains version 0.3.0 during testing. Workshop upload content and `mod_id.txt` were not changed.

### Strength Phase 8 zero-clamp correction

Implemented and validated 2026-09-17 after the live multiplayer test found that repeated enemy hits reduced to zero were omitted from Strength prevention. The damage hook now captures each hit's original amount and reconstructs an exact zero-clamped counterfactual only when Strength is the sole additive modifier. A four-player regression requires the reported `18 + 48 = 66` Assisted Damage Prevented result, including per-target self exclusion. Debug and Release builds have zero warnings/errors and all 142 tests pass in both configurations.

| Candidate file | SHA-256 |
| --- | --- |
| `RunStats.dll` | `7178884B6F496EB870A68E7AA80D243051DF920A0B31D83D6577F8FC79B7BB72` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `0B2EE0AAB26CB7F41DAD124630B4F7F82FDBD953B8589E30A6E577C5C0D2CD98` |

The first deployment check found the game running and made no changes. After the process closed, the candidate was installed through the safe Release deployment path. The local directory contains exactly the DLL, PCK, and manifest shown above, and every installed hash matches its workspace source. Workshop content is unchanged.

The user could not recreate the four-player scenario after installation and accepted the exact 66-point automated regression as sufficient confirmation. Record this as an automated pass with no live retest, not as live multiplayer evidence.

### Prepared v0.3.1 Workshop package

Prepared 2026-09-17 after explicit Strength Phase 9 approval and refreshed 2026-09-18 with the initialization hotfix. Project/package version is `0.3.1`, assembly version is `0.3.1.0`, snapshot/sidecar schema remains 4, `affects_gameplay` remains `false`, and the Release assembly contains no custom network-message implementation. Debug and Release builds have zero warnings/errors and all 143 tests pass in both configurations.

The published v0.3.0 package was preserved first under `workshop\RunStats\rollback\v0.3.0`. `mod_id.txt` remains `3797791393`, the thumbnail remains 735,516 bytes, and the uploader content contains exactly these verified v0.3.1 files:

| File | SHA-256 |
| --- | --- |
| `RunStats.dll` | `BD82B7706A408704969724F14988699044FB54BB7944359D5C4BC9E706D9C5FA` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |
| `mod_manifest.json` | `322D17AF10E56DBCED064ABE3812BE097890364A96FBD6DA623CF403077C840B` |

The visible `Assisted Damage Prevented` row was renamed to `Damage Prevented` before commit; the internal persisted identifier remains unchanged. The 2026-09-18 hotfix updates the `Hook.ModifyDamage` Harmony prefix to bind the game's `damage` parameter (instead of the obsolete `amount` name), preventing `PatchAll()` from aborting and restoring RunStats initialization and its top-right menu. A regression test locks that parameter contract. The local Release installation is refreshed from the final package before commit. No Steam uploader command was run and no visibility/publication state changed.

## Future change workflow

1. Check the currently installed STS2 version and assembly compatibility before changing Harmony patches or UI paths.
2. Make the source change and add/update deterministic tests.
3. Keep these versions synchronized:
   - `Version` and `AssemblyVersion` in `src\RunStats\RunStats.csproj`
   - `version` and, when required, `min_game_version` in `src\RunStats\mod_manifest.json`
   - version text and `changeNote` in `workshop\RunStats\workshop.json`
4. Build and test:

   ```powershell
   dotnet build .\src\RunStats\RunStats.csproj -c Debug --configfile .\NuGet.config
   dotnet run --project .\tests\RunStats.Tests\RunStats.Tests.csproj -c Debug --no-restore
   dotnet build .\src\RunStats\RunStats.csproj -c Release --configfile .\NuGet.config
   ```

5. Test the Debug build locally with the Workshop copy unavailable. Recheck single-player, save/continue, the map visibility rule, defeat/victory access, and unmodded-friend co-op compatibility when multiplayer code changes.
6. Preserve a versioned copy of the currently published `workshop\RunStats\content` before packaging a replacement.
7. Package the verified Release build:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Package-RunStatsWorkshop.ps1
   ```

8. Confirm Workshop content contains exactly `RunStats.dll`, `runstats.pck`, and `mod_manifest.json`. Never upload PDB, `.deps.json`, `.runtimeconfig.json`, saves, sidecars, logs, or test data. Keep `image.png` below 1 MB.
9. Review `workshop.json`, especially visibility and `changeNote`, then upload from the uploader directory:

   ```powershell
   .\ModUploader.exe upload -w "C:\Users\Mike Major\Desktop\RunStats\workshop\RunStats"
   ```

10. Verify the existing item ID remains `3797791393`, inspect the public page, wait for Steam to update the subscribed copy, and verify its files/hashes before launching the game.

## Safety and compatibility invariants

- `affects_gameplay` remains `false` so unmodded friends can join.
- The Release assembly must contain no custom `INetMessage` implementations.
- RunStats writes only its own `com.bradbeise.runstats` sidecars and dedicated mod artifacts; never edit vanilla saves or unrelated mods.
- Unattributed Weak/Vulnerable applications remain in their denominators but produce no player credit.
- Unknown or ambiguous Strength source, lifetime, restoration, reset, or damage counterfactual remains uncredited; never infer ownership from local-player identity or timing alone.
- Keep the external Stage 9 save backups documented in `RUNSTATS_SPEC.md` until they are intentionally retired.
