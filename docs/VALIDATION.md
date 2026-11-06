# Animation Composer 0.4.1 validation

For each GitHub release, the workflow extracts the Linux and Windows downloads on their respective runners and requires the packaged executable to render a headless snapshot. The desktop interaction tests below record separate checks against the local mod collection.

Two separate Linux processes at 125% display scale were used to set and restore the layout. Native mouse drags changed both main dividers, both horizontal dividers and the table headers. Reopening restored a 1392 × 873.6 logical-pixel window, a 203.2-unit Added sets viewer, and all three column widths. A second run resized only the neighboring headers and confirmed the Name column still fits the pane automatically. Both runs used isolated settings directories.

Smaller work areas constrained the restored window to fit. A 920 × 624 window let the user scroll the whole editor. Invalid or incomplete settings fell back to usable defaults, and a headless snapshot left the preferences file intact. Win32 tests under Wine covered normal, maximized and minimized closes, including minimizing a maximized window and recovering its former normal size. The core suite passed all 4,949 assertions.

## Resizable workspace in 0.4.0

On 6 November 2026, a native Linux window at 125% display scale accepted mouse drags on all three column headers, the project divider and the bottom edge of **Added sets**. The weapon dropdown kept the resized selector’s width and left edge at five scroll positions. The Windows x64 release opened its Win32 window under Wine 11.20. Testing on a Windows machine is still pending.

A separate desktop interaction check added 12 complete movesets from the installed For Honor BFCO pack. It exercised all four pane dividers, all three table headers, adding and removing rows, editing name, actors, priority and keyword, recovering selection after an invalid edit, and loading rules from an older project. Dragging to the limits at 1040 × 740 kept the panel controls reachable. A preview with the installed XP32 skeleton also rendered successfully.

The core suite passed 4,949 assertions, including 1,614 installed attack previews. The CLI validated the earlier Warlord sword and Elden Rim war axe project.

## Weapon menu in 0.3.2

Version 0.3.2 constrains both the native popup host and its visible list to the weapon selector's width and anchors the popup to its left edge. The previous 0.3.1 check used an embedded headless popup and missed the native host centering its content at the changing preferred width of the visible items.

The fault was reproduced in an actual Linux desktop popup at the host's display scale: the list changed between 101.53 and 219.63 layout units wide, and its left edge moved between screen x=1685 and x=1770, while the selector remained 306.65 units wide at x=1622. After the fix, nine scroll samples from the top to the bottom and back kept both the selector and visible list at 306.65 units wide and x=1622. Native popup images were also inspected. The temporary verification window closed automatically and did not load or modify a user project.

Linux and Windows builds were rebuilt. Native Windows execution remains untested. The 0.3.0 functional validation below remains applicable; animation and export logic were not changed by this patch.

Performed on Linux, 5 November 2026. Version 0.3.0 adds Animated Armoury weapon conditions, selection prefilling, explicit MCO/BFCO conversion reports and native DAR/OAR migration. Gameplay and Windows desktop validation remain outstanding. The preview and chain evidence from 0.2.0 is retained below.

## Import, detection and migration in 0.3.0

- The full collection still indexes 33,841 HKX files in 3,036 source folders. The expanded importer recognizes 503 DAR folders. Seven malformed/incomplete DAR folders remain blocked because they have no usable conditions or actor selector. Unknown conditions are also blockers; parsing never returns a partial rule tree.
- Animated Armoury's nine keyword categories were checked against local `NewArmoury.esp` keyword records. Fixtures cover those FormIDs, native EditorIDs, aliases, negation, disabled clauses, mixed OR alternatives, presets, left/right/either/both hands and two-handed katana skill selection. Existing numeric weapon rules remain compatible.
- The 361 subfolders in the installed For Honor BFCO pack become 23 complete named families, retaining every subfolder within its family. Other OAR roots remain complete packages; mixed weapon packages may require a manual weapon selection.
- The enabled `SKSE TEST` profile indexed 9,455 HKX files and applied the config-only BFCO Animated Armoury patch to eight animation folders. Fixture checks cover case-insensitive virtual paths, profile ordering, DAR overrides and OAR's user-data replacement semantics. No mod-manager files were changed.
- A real BFCO export combined the seven MCO Warlord subfolders and two Elden Rim axe subfolders. All **110 exported animation files** matched their sources byte for byte. MCO slot mappings, variant asset names, configuration path rewrites, collision detection and unsupported-slot rejection are covered by fixtures.
- A separate native OAR export migrated all **14 third-person DAR folders / 606 animation files** from the installed Animated Armoury mod. Every filename, animation byte and condition tree matched its source representation. No gameplay test is implied by those checks. Native migration preserves original slots and uses the selected output priority range.
- Headless desktop checks confirmed katana/keyword/two-handed skill prefilling, retaining edited name and level before adding, and keeping an added rule intact while browsing another weapon. Native mode defaulted to source equipment/actor conditions and displayed the third-person rapier preview. Screenshots were inspected for layout.
- The 0.3.0 test executable passes **4,949 assertions**, including the 1,614 real attack previews below. Linux and Windows releases are self-contained; Windows execution and native file-dialog interaction remain untested.

Native migration requires a DAR/OAR structure. It does not manufacture BFCO chains from vanilla attack filenames. Redirected OAR animation folders (`overrideAnimationsFolder`) are detected and block export pending manual resolution. Migrated random conditions use OAR 2.3+ serialization and its default legacy loop-state behavior. Profile support resolves loose configuration overlays, not complete runtime asset or BSA load-order conflicts.

## Collection scan

The updated `/mnt/mods/Amaranth/mods` scan indexed **33,841 HKX files**, **3,036 source folders**, and **86 skeleton files**. The collection grew after the 0.1.0 scan (28,981 files). These source-folder counts are not the UI's complete-bundle count; related folders are grouped together there.

BSA imports completed without archive errors. The 15 remaining notices were skipped symlinks in a development project's `node_modules/.bin` folder. Both duplicate JSON keys and legitimate shared BSA member offsets were encountered in the collection and are covered by regression checks. The scanner does not follow symlinks.

## Animation decoding

All **1,614 attack-named HKX files** in For Honor in Skyrim and BFCO Elden Rim decoded with the installed XP32 humanoid skeleton. Beginning, midpoint and end samples had finite bone positions. This does not certify every indexed HKX file as a playable animation.

A broader check of **6,557 files** in recognized BFCO/MCO folders decoded **6,527**, with finite midpoint bone positions. Compared with the initial renderer on the same collection, **1,544 nonuniform-scale failures** and **36 empty-array HKX header failures** were resolved. Tests cover independent scale axes, rotated child transforms, interpolation, and static spline scale decoding.

The remaining **30 files** have 156 transform tracks and no explicit remapping table, while the installed humanoid XP32 HKX skeletons have 126 bones. They comprise 18 SkyFX dodge files and 12 Nightreign Duchess files. The app reports the mismatch and asks for the matching skeleton instead of dropping tracks or guessing their mapping. The scan found no usable 156-bone humanoid HKX skeleton among the installed candidates.

The original decoder cross-check from 0.1.0 compared two complete decoded clips against PyNifly's separate Python reader at the revision in `SOURCES.json`:

| Source | Frames / tracks | Maximum local-position difference | Maximum quaternion-component difference |
| --- | --- | --- | --- |
| Warlord / MCO_Attack1, ThreeComp48 | 49 / 99 | 0.00000285 | 0.000000130 |
| BFCO Elden Rim / 15007 / BFCO_Attack1, ThreeComp40 | 90 / 97 | 0.0000292 | 0.000000134 |

The decoder derives from PyNifly's algorithms, so this comparison checks the C# implementation against that reader, not independently established Skyrim ground truth. Polar32 and interleaved paths have less real-collection coverage. Additive animations remain unsupported. Nonuniform scale is now preserved rather than rejected.

## Chain playback

Regression checks cover annotated branch order, independent normal/power input windows, timing, cycles, missing destinations, whole-sequence seeking, numeric ordering, and OAR variant slot identification. Real-source tests verify Warlord's normal route 1 → 2 → 3 → 4 → 5 → 6 and its power branch from 1 to 7.

Headless desktop sessions exercised live timer-driven advancement across multiple clips, repeating a completed chain, seeking within a ten-clip numbered power sequence, and the explicit 156/126 skeleton error display. A Kensei power chain that previously hit the nonuniform-scale guard rendered successfully. The normal and scaled power layouts were visually inspected.

Annotated preview assumes input is available at the earliest recognized window. It does not emulate input-dependent branch choice, OAR condition evaluation, charged holds, root-motion transitions, or Skyrim's blending. Missing or repeated destinations end a pass; numbered fallback is reported in the Sequence tab.

## Complete-moveset export

A scratch project combined all seven Warlord subfolders with both Elden Rim war-axe subfolders. All **110 exported HKX files** matched their source bytes and recorded SHA-256 hashes. The ZIP passed an integrity check. NPC targeting, actor level bounds, and the base-skill condition were checked in the generated OAR configuration. Source relative priorities and shared condition presets are also covered by automated fixture checks.

The test executable passed **4,888 assertions**, including real animation samples, nonuniform transforms, chain sequencing, DAR OR-block semantics, condition preservation, actor targeting, whole-bundle membership, variant filename mapping, priority collisions, archive boundaries, traversal rejection, duplicate-member JSON behavior, and exact BSA payload aliases.

No test output was installed into the mod manager. The exporter does not replace BFCO behavior graphs or package source effects/plugins. Source dependencies remain required.

## Desktop and releases

- Linux x64 and Windows x64 self-contained builds were published with .NET 8 and Avalonia 11.3.10.
- The Linux application scanned Amaranth and rendered the application and chain previews using Avalonia's headless Skia backend. The layout and live playback were checked. Native file dialogs and every mouse interaction have not been manually exercised.
- The Windows package was cross-built on Linux. It has not been launched on Windows.
- No in-game Skyrim test was performed. Previewing skeletal poses does not execute behavior graphs, motion extraction, hit detection, payloads, conditions, or effects.

## Reproduce

Run `tools/check.sh /path/to/mods` after making `dotnet` available, or set `MOVESETFORGE_DOTNET` to the SDK executable. The optional collection argument expects the named source mods and XP32 skeleton described above.

Use `tools/publish.sh /path/to/packages` to produce both platform archives and the corresponding source archive. Publishing needs the .NET SDK and NuGet access; packaging also needs Python 3. Runtime game assets are never bundled.
