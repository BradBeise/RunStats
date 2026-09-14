# RunStats maintenance and release guide

## Canonical locations

- Source repository: `C:\Users\Brad Beise\Documents\Repos\RunStats`
- Historical v0.1.0 local Debug backup: `C:\Users\Brad Beise\Documents\Repos\RunStatsLocalTestModFolder`
- Game-local development destination: `D:\Steam\steamapps\common\Slay the Spire 2\mods\RunStats`
- Workshop upload workspace: `C:\Users\Brad Beise\Documents\Repos\RunStats\workshop\RunStats`
- Subscribed production copy: `D:\Steam\steamapps\workshop\content\2868840\3797791393`
- Mega Crit uploader: `C:\Users\Brad Beise\Documents\Repos\RunStats\tools\mod-uploader-v0.2.0`
- Workshop item: `https://steamcommunity.com/sharedfiles/filedetails/?id=3797791393`
- Current Steam profile settings root: `C:\Users\Brad Beise\AppData\Roaming\SlayTheSpire2\steam\76561198407892354`

Never delete `workshop\RunStats\mod_id.txt`. It binds future uploads to Workshop item `3797791393`; without it, the uploader creates a different item.

## v0.1.0 rollback baseline

These hashes preserve the previously published v0.1.0 release for rollback.

The preserved v0.1.0 package contains exactly:

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `6E697AA7AFF5A50A6C3576EF14797AEB6E9F52DB23E7464874460D886F40EAB5` |
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

The current machine has the Workshop item installed but disabled in `settings.save`, and no game-local `mods\RunStats` directory existed at the Phase 4 checkpoint. Phase 5 must reconfirm both facts and that the game is closed before installing the Release candidate.

## v0.2.0 upgrade notes

- Snapshot and sidecar schema 2 add Poison Applied and three poison diagnostics. Schema-1 sidecars are accepted only with the complete legacy field set, then migrated by inserting zero for new fields. Existing totals are preserved; historical poison is not reconstructed.
- The dormant custom snapshot protocol constant is 2 because the fixed statistic/diagnostic wire layout changed. Release builds continue to exclude custom RunStats network message types.
- Poison contribution weights, exact fractional carries, per-cycle kill comparisons, and Accelerant sponsor order are combat-only. They are intentionally absent from sidecars and reset at combat end. Only finalized run totals persist.
- Block Lost now records only Block absorbed by enemy damage. End-of-turn clearing and other non-enemy Block reductions are excluded; existing saved totals are preserved rather than reconstructed.
- If rollback to v0.1.0 is required, close the game and use the preserved `workshop\RunStats\rollback\v0.1.0` package through a separately approved release or local-test workflow. A schema-2 sidecar is not readable by v0.1.0; preserve it for diagnosis or archive it inside the RunStats-owned data directory rather than editing vanilla saves.

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
| `mod_manifest.json` | `22DF6826723093AE7335EA18D707CB555A824F1DDFCDCFB28D8B2A81CD2E4941` |
| `RunStats.dll` | `C3AA0F15380843302F745316BCFE08CCBB7981465B3AB241A51A3FB5415D0637` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

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
   .\ModUploader.exe upload -w "C:\Users\Brad Beise\Documents\Repos\RunStats\workshop\RunStats"
   ```

10. Verify the existing item ID remains `3797791393`, inspect the public page, wait for Steam to update the subscribed copy, and verify its files/hashes before launching the game.

## Safety and compatibility invariants

- `affects_gameplay` remains `false` so unmodded friends can join.
- The Release assembly must contain no custom `INetMessage` implementations.
- RunStats writes only its own `com.bradbeise.runstats` sidecars and dedicated mod artifacts; never edit vanilla saves or unrelated mods.
- Ambiguous assisted-stat attribution remains omitted rather than estimated.
- Keep the external Stage 9 save backups documented in `RUNSTATS_SPEC.md` until they are intentionally retired.
