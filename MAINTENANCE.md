# RunStats maintenance and release guide

## Canonical locations

- Source repository: `C:\Users\Brad Beise\Documents\Repos\RunStats`
- Local Debug test backup: `C:\Users\Brad Beise\Documents\Repos\RunStatsLocalTestModFolder\RunStats`
- Game-local development destination: `D:\Steam\steamapps\common\Slay the Spire 2\mods\RunStats`
- Workshop upload workspace: `C:\Users\Brad Beise\Documents\Repos\RunStats\workshop\RunStats`
- Subscribed production copy: `D:\Steam\steamapps\workshop\content\2868840\3797791393`
- Mega Crit uploader: `C:\Users\Brad Beise\Documents\Repos\RunStats\tools\mod-uploader-v0.2.0`
- Workshop item: `https://steamcommunity.com/sharedfiles/filedetails/?id=3797791393`
- Modded profile data: `C:\Users\Brad Beise\AppData\Roaming\SlayTheSpire2\steam\76561198407892354\modded\profile1`

Never delete `workshop\RunStats\mod_id.txt`. It binds future uploads to Workshop item `3797791393`; without it, the uploader creates a different item.

## Current v0.1.0 baselines

The subscribed Workshop copy contains exactly:

| File | SHA-256 |
| --- | --- |
| `mod_manifest.json` | `6E697AA7AFF5A50A6C3576EF14797AEB6E9F52DB23E7464874460D886F40EAB5` |
| `RunStats.dll` | `7283EB1E106E160305EF355D67AC711FE80D7BABF9618C34D1BCE4671C6CF070` |
| `runstats.pck` | `2E075CFCFAE4EE3CBDC824CB0040CFD3FFDF4920666C93C7165324C7797E58D1` |

The external local-test backup is the final Debug build. Its DLL SHA-256 is `06D3B1B222CE30BABAE848972B365917DEA573118CCB53F7F27CD72AC90B43B0`; its PDB SHA-256 is `E3F6E3E82EFDDC45D6753B32C4685A31D92C853C433E08E57CC58EB7B0749C7A`.

## Avoiding duplicate installations

STS2 must never see both the local and Workshop copies of the `runstats` manifest at the same time.

- For normal play, keep the game-local `mods\RunStats` path absent and stay subscribed to item `3797791393`.
- Before local Debug testing, close the game, unsubscribe from or fully disable the Workshop copy, confirm it will not load, and deploy the Debug build with `tools\Deploy-RunStats.ps1`.
- After testing, close the game and move the entire local `RunStats` directory back outside the game's recursively scanned `mods` tree before re-enabling or resubscribing to the Workshop item.
- Do not edit files inside Steam's `steamapps\workshop\content` directory. Steam owns and may replace them.

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
