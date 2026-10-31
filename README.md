# Still Life 2 - Reunion Black Screen Fix

[![Windows x64 download](https://img.shields.io/badge/download-Windows_x64-2563eb?style=flat-square)](https://github.com/pschaub/still-life-2-reunion-black-screen-fix/releases/latest)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white)
[![11 languages](https://img.shields.io/badge/languages-11-0f766e?style=flat-square)](#appearance-and-languages)
[![MIT license](https://img.shields.io/badge/license-MIT-64748b?style=flat-square)](LICENSE)

[Steam](https://store.steampowered.com/app/46490/Still_Life_2/) · [GOG](https://www.gog.com/en/game/still_life_2)

Fixes the stuck black screen when Victoria leaves the cellar laboratory and returns to the hallway in **Reunion**, after unlocking the East Hatch for the first time. Repair your savegame or patch your `SL2.exe` to continue playing. Choose one method; you do not need both.

![Repair tool](assets/gui-preview.png#gh-light-mode-only)
![Repair tool](assets/gui-preview-dark.png#gh-dark-mode-only)

## Download and use

Download the [Windows x64 release](https://github.com/pschaub/still-life-2-reunion-black-screen-fix/releases/latest) and extract the ZIP. No separate .NET installation is needed.

| Method | Effect |
|---|---|
| **Repair a savegame** | Repairs one stuck save. The game EXE stays unchanged. |
| **Patch the game EXE** | Recovers this transition when affected saves are loaded. Existing save files stay unchanged. |

Both options recover this one stalled transition. The original cause of the timer loss is unresolved; neither is a general fix for timers or other black screens.

1. Close the game and open `StillLife2Patcher.exe`.
2. Choose **Repair a savegame → Find my saves**, or **Patch the game EXE → Choose file** and select `SL2.exe`.
3. Choose **Fix original with backup** or **Save fixed copy**. For an untested file, select **Continue anyway** in the warning dialog.
4. Start the game normally and load your save.

To undo a repair, restore the `.bak` file under the original filename. A separate save copy needs a valid slot filename in `EXTDataSave` to appear in-game. For normal launch, a patched EXE must replace `SL2.exe` at the game's expected location.

## Compatibility

| Examined edition | Validation |
|---|---|
| **GOG 1.0 (spanish)** | Save repair and EXE patch tested in-game. |
| **Steam**, embedded EXE build stamp `Apr 6 2009 18:31:35` | Layout and static patch checks passed. Patched game launch untested; game version unreported. |

**Untested versions and unknown file hashes are allowed.** Both repair actions remain available when the required structures match. The warning dialog lets you **Continue anyway** or **Cancel**, identifies the file and explains that the result is untested. Confirmation applies to that repair attempt only.

![Warning for an untested file](assets/untested-warning.png#gh-light-mode-only)
![Warning for an untested file](assets/untested-warning-dark.png#gh-dark-mode-only)

Cancelling or closing the dialog leaves the files untouched; Esc and Enter cancel by default. The tool still needs valid patch locations in an EXE, or the matching scene and animation records in a save. It cannot apply this repair to unrelated scenes, malformed data or an incompatible layout. A save with an animation already active on the timer node is left unchanged.

The fix targets this cellar-to-hallway transition in Reunion, regardless of save filename.

## Save discovery

**Find my saves** checks Steam libraries, GOG installations and common save locations. You can also choose a game folder, an `EXTDataSave` folder or a file directly, or drag a file into the app.

The list shows filename, modification date, repair status and folder. Repairable saves appear first, followed by modification date. Scans can be cancelled. Replacing a file creates a backup; existing destinations and conflicting backups are not overwritten. The source is checked again before writing.

## Appearance and languages

The round appearance button offers System, Light and Dark. System follows the Windows app theme, including changes while the app is open. Dropdown arrows scale with DPI, and high-contrast mode uses Windows colors.

Languages: english, german, french, spanish, italian, portuguese, polish, russian, simplified chinese, japanese and korean. The app selects the Windows interface language automatically; you can change it in the language menu. Statuses, explanations and repair errors are translated. Theme and language overrides last for the session. Unsupported languages fall back to english.

Files are processed locally, without uploads or telemetry. The tool does not start the game. File details explain compatibility.

## Example save

[Example broken save](examples/example-broken.sav) is the original broken reproduction and contains story spoilers. To try the tool, select it in **Repair a savegame** and save a fixed copy elsewhere. Keep the fixture unchanged for checks. For an in-game test, use a valid save-slot filename in a separate save folder and back up any slot you replace. No game EXE, profile index or thumbnail is included.

## Technical details

### Missing transition timer

The broken save waits in state `5 / NOD / SynchTimer_1 / animation 95`, but its native active-animation list contains ten camera animations and no timer 95. Loading reproduces the missing timer while other game/script updates continue.

At frame 28, `animAct2AnimationsNodSynchTimer_1` calls `Endcurrentstate("NOD")`, advancing to state 6 (`BLACKTOFADE`) and then state 7. Restoring the missing timer lets this sequence continue. The healthy comparison save has the same ten camera animations but is not waiting for the timer, so a missing timer alone is insufficient to identify the fault.

### EXE patch

The patch appends a 569-byte x86 stub in an executable, non-writable `.sl2fix` section and replaces the VM handler pointer at `0x54fcf0`. The stub preserves registers and flags and always continues to the original handler at `0x42e330`.

Before starting animation 95, it checks:

- ACT2 bytecode call site `0xd2550`;
- zone `Act2ZonesTriggerdec232ZIT_dec232_TRIG_end`, state 5 and NOD synchronization;
- expected `SynchTimer_1` animation node and valid resource slot 95;
- an existing scene node named `SynchTimer`;
- no active animation 95 or other animation on that scene node.

Calls outside this specific scene and waiting state continue through the original handler without recovery. The stub does not write script state, fade opacity or save files. Its absolute references and relative calls are relocated for the appended section. Steam's `.text` and `.bind` bytes stay unchanged; launch protection is not removed.

The inspected GOG and Steam EXEs are 2,539,520 and 3,006,464 bytes respectively. Their relevant layouts and dispatch tables match, but the files differ. Other versions must pass the structural checks; language alone does not determine compatibility.

### Save repair

The parser verifies the same scene and waiting-state globals, including the scene understate marker. It locates the 248-entry resource table, checks that slot 95 is `nod/SynchTimer_1.xck`, and finds a unique native animation list followed by the path-finding block.

In the reference save, repair changes the animation count at byte offset `404300` from 10 to 11 and inserts a 64-byte entry at `405003`:

- `_TAD`;
- null-terminated `nod/SynchTimer_1.xck` and `SynchTimer`;
- six little-endian words: `[95, 0, 0, 0, 30, 0]`;
- `_DNE`.

All other bytes stay unchanged. The parser locates these structures dynamically; the example offsets are not hard-coded repair locations. The original save is 7,514,274 bytes; the repaired save is 7,514,338 bytes.

### Version and build identification

GOG's installer product metadata identifies package version `1.0` and build `51402240449261761`; the extracted GOG metadata contains the same build ID. Both inspected game EXEs contain the diagnostic build stamp `Apr 6 2009 18:31:35`, but neither has a Windows file/product version resource. The shared stamp does not establish that the complete GOG and Steam builds are identical.

The supplied `xcEngine.dll` files are byte-identical and report `Core Engine 1.2.0`. This identifies the engine component, not an official Still Life 2 game version. No game version number or Steam build ID was found in the supplied Steam files.

### Native functions and scope

Addresses refer to the analysed GOG image at base `0x400000`.

| Address | Role |
|---|---|
| `0x45cd50` | Start node animation; thiscall with level context in ECX; node, ID, reverse, loop and restart arguments |
| `0x4bd440` | Find scene node by ASCII name |
| `0x459e50` / `0x456260` | Serialize active animations / write one entry |
| `0x45f370` / `0x45a300` | Restore active animations / read one entry |
| `0x45b040` | Update animation; callback runs before eventual removal |
| `0x41fd60` | `_Framing` interval predicate |

No timer/one-shot exclusion was found in the serializer. Large frame times and competing starts on the same node remain unproven causes. A broader engine fix would require reproducing the timer loss and distinguishing it from an intentional stop.

The save's smartphone history names this section Reunion (`hst030`). The [MaGtRo walkthrough](https://www.gameboomers.com/wtcheats/pcSs/StillLife2/stilllife2.htm) and [second walkthrough](https://www.gameboomers.com/wtcheats/pcSs/stilllife2.html) place East Hatch under Reunion / Enter the fallout shelter. These guides contain later spoilers.

### Discovery limits

Discovery reads Steam library manifests, GOG/uninstall registry entries, common installation folders, matching game folders in Downloads, Documents and Saved Games, and VirtualStore paths. It checks installation roots and their `EXTDataSave` folders without a recursive disk search.

Reparse-point save files/directories are skipped. Scans stop at 500 saves and inspect files up to 64 MiB. Choose a specific folder if a location is missed or a limit is reached. Dates are filesystem modification times, not in-game progress.

### Verification

17 EXE checks and 26 save/discovery checks pass: known/unknown variants, explicit confirmation, malformed and ambiguous files, repeated repairs, backup conflicts, discovery/cancellation, preservation of unrelated save bytes, and unchanged Steam `.text`/`.bind` sections. Static checks also verify the stub's call targets and four absolute relocations.

All 11 languages × 66 strings pass completeness and placeholder checks, including system-language detection and fallback. The packaged app and warning dialog were rendered in light/dark themes, including japanese and korean.

The repaired save and generated GOG EXE match the files tested in-game. The EXE test used the unchanged broken save. Steam runtime validation and a broad regression playthrough remain outstanding.

### Build and reproduce checks

Build on Windows with the .NET 10 SDK and Windows Desktop reference packs. The app uses C# / WinForms. No external NuGet packages or complete game executables are included.

```powershell
./build.ps1                    # Framework-dependent app in dist/
```

`src/Core` contains patching, save repair, discovery and file transactions; `src/App` contains the interface and translations; `src/Checks` contains the checks. [tools/build_payload.py](tools/build_payload.py) generates the included x86 recovery stub and its relocation data. It is the source of the EXE patch, so developers can inspect or rebuild it using Python's standard library. Running the repair app does not require Python.

Supply your own game EXEs and a healthy save from before the transition:

```powershell
$checks = './src/Checks/bin/Release/net10.0/StillLife2Patcher.Checks.dll'
dotnet $checks test 'C:\Games\GOG\Still Life 2\SL2.exe' 'D:\SteamLibrary\steamapps\common\Still life 2\SL2.exe'
dotnet $checks test-save ./examples/example-broken.sav 'C:\Games\GOG\Still Life 2\EXTDataSave\men008_93.sav'
./dist/StillLife2Patcher.exe --validate-translations ./translation-check.txt
```

## License

The patcher source is licensed under [MIT](LICENSE). Game data and the example save are not covered by this license.
