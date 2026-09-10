# RunStats maintenance and release guide

## Canonical locations

- Source repository: `C:\Users\Mike Major\Desktop\RunStats`
- Historical v0.1.0 local Debug backup (previous machine): `C:\Users\Brad Beise\Documents\Repos\RunStatsLocalTestModFolder\RunStats`
- Game-local development destination: `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats`
- Workshop upload workspace: `C:\Users\Mike Major\Desktop\RunStats\workshop\RunStats`
- Subscribed production copy: `D:\SteamLibrary\steamapps\workshop\content\2868840\3797791393`
- Mega Crit uploader: `C:\Users\Mike Major\Desktop\RunStats\tools\mod-uploader-v0.2.0`
- Workshop item: `https://steamcommunity.com/sharedfiles/filedetails/?id=3797791393`
- Current Steam profile settings root: `C:\Users\Mike Major\AppData\Roaming\SlayTheSpire2\steam\76561198122724722`

Never delete `workshop\RunStats\mod_id.txt`. It binds future uploads to Workshop item `3797791393`; without it, the uploader creates a different item.

## Current v0.1.0 baselines

These hashes are the rollback baseline for the currently subscribed, disabled Workshop release. Do not replace this table with v0.2.0 hashes until the new Workshop package has been separately approved and published.

The subscribed Workshop copy contains exactly:

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

## v0.2.0 poison upgrade notes

- Snapshot and sidecar schema 2 add Poison Applied and three poison diagnostics. Schema-1 sidecars are accepted only with the complete legacy field set, then migrated by inserting zero for new fields. Existing totals are preserved; historical poison is not reconstructed.
- The dormant custom snapshot protocol constant is 2 because the fixed statistic/diagnostic wire layout changed. Release builds continue to exclude custom RunStats network message types.
- Poison contribution weights, exact fractional carries, per-cycle kill comparisons, and Accelerant sponsor order are combat-only. They are intentionally absent from sidecars and reset at combat end. Only finalized run totals persist.
- If rollback to v0.1.0 is required, close the game, remove the game-local v0.2.0 directory, and re-enable the untouched Workshop copy. A schema-2 sidecar is not readable by v0.1.0; preserve it for diagnosis or archive it inside the RunStats-owned data directory rather than editing vanilla saves.

### Phase 5 local Release candidate

Installed 2026-09-08 into `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\RunStats` after confirming STS2 was closed and the Workshop source was disabled. The candidate contains exactly:

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `DFAE6493E6F2329191AD65D854F1D0003FFBCFAC534FF32917EBBA589ED578CA` |
| `RunStats.dll` | `5A671E88783E690CD5259F2BA5C5F37B08A6B057AA19E70ACC300D0ECE44F53E` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

All hashes match workspace Release sources. The Workshop directory remains Steam-owned and untouched. The candidate is a Release-configuration build but intentionally retains the 0.1.0 manifest/assembly version until the coordinated Phase 7 version update.

### v0.2.0 Workshop-ready package

Prepared 2026-09-08 in `workshop\RunStats\content` after the approved single-player and multiplayer poison playtests. This is an upload-ready package for existing item `3797791393`; it has not been uploaded.

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `F4D3A9D3C0F06DAE6DA8CC6B9223763AE0B0BAEBD2BF50D0EB75D67CD36EE7C9` |
| `RunStats.dll` | `5C6D8FB363E2E38D67B392500AFCBDEE324D69B5C5975C6BCBF9A26A037D44C0` |
| `runstats.pck` | `F4A1A43C637230E2994F7D20FAA96DE5473A462DC653B59713328529EDF8379D` |

The manifest and assembly versions are both 0.2.0, `affects_gameplay` remains `false`, and the Release project excludes all custom RunStats network-message types. The source and packaged hashes match. The uploader workspace retains public visibility and `mod_id.txt` value `3797791393`, but the uploader must not be run without explicit publication approval.

The game-local `mods\RunStats` test installation was updated to this exact final package after confirming STS2 was closed; all three installed hashes match the table. Settings keep this local source enabled and the subscribed Workshop v0.1.0 source disabled.

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
