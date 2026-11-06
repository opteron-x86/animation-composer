# Animation Composer

A native Linux and Windows desktop application for choosing **complete Skyrim weapon movesets**, previewing their animations, assigning Open Animation Replacer conditions, and exporting a BFCO animation override mod or migrating DAR replacements to native OAR.

An imported OAR replacer keeps its submods, variants, settings, and shared condition presets. Named For Honor BFCO families are separate complete selections even though that pack places them under one shared root. You can select a sword set from one pack and an axe set from another. The application does not splice attacks, reauthor combos, or change animation timing or movement.

## Run

Download the Linux x64 tarball or Windows x64 ZIP from the [latest release](https://github.com/opteron-x86/animation-composer/releases/latest). Extract the full archive and launch `AnimationComposer` on Linux or `AnimationComposer.exe` on Windows. Both downloads include the .NET runtime.

For the local Amaranth installation, `./run-amaranth.sh` opens the Linux build with `/mnt/mods/Amaranth` selected. The application reads the installation and creates separate exports. It does not enable/disable mods or run a behavior generator.

## Workflow

1. Browse to a mod-manager instance, `mods` folder, or extracted animation pack. Select **All installed** or a profile from its `profiles` folder, then **Scan**. Profiles use MO2/Fluorine `modlist.txt`. Disabled mods are available in All installed mode. Profile scans apply loose configuration overrides in mod priority order, including config-only Animated Armoury patches. All installed mode keeps each source's own configuration because it has no chosen load order.
2. BSA animation assets are imported automatically into a separate cache. **Import ZIP / BSA** adds downloaded packs. Extract 7z/RAR archives with your archive manager before adding their folder. FOMOD downloads should be installed/extracted to the desired option first; this application does not choose installer options.
3. Select a complete source moveset. Choose the matching `skeleton.hkx`; a humanoid skeleton is selected automatically when found. Use the folder selector above the preview to inspect a particular conditional subfolder. Pick a clip, play, scrub, change playback speed, or orbit the preview. **Play normal chain** and **Play power chain** play a sequence from that folder.
4. Clicking a source fills a draft with its name, detected weapon class/keyword, hand, skill and priority. Review the detection explanation and edit the rules, then **Add selected set**. Your edits are retained when adding. Select an added rule to edit it; valid edits also save when switching selections, saving or exporting. Add another complete set for another weapon or condition range.
5. Save the project. **Export mod** shows a dependency/conflict report and writes a new folder and installable ZIP in the chosen destination. Install it through your mod manager and verify the selected replacements in OAR in game.

Drag a column header’s right edge in **Added sets** to adjust its width. Drag the grip below the table to show more sets or give the rule editor more space. The dividers between the three main panes and above the preview tabs also drag. Closing the app saves the window size, maximized state, pane proportions, Added sets height and column widths for the next launch. This layout applies across projects. Linux stores it under `$XDG_CONFIG_HOME/AnimationComposer/layout.json` (usually `~/.config/AnimationComposer/layout.json`), and Windows uses `%LOCALAPPDATA%\AnimationComposer\layout.json`.

## Conditions and export behavior

- The weapon list includes Animated Armoury rapier, pike, spear, halberd, quarterstaff, claws, cestus, whip and katana, plus a custom keyword EditorID. Native OAR keywords and legacy NewArmoury.esp keyword FormIDs are recognized. Literal aliases such as WeapTypeQuarterstaff are preserved. Categories add weapon-keyword conditions; the required weapon mod or keyword distribution must actually supply those keywords in game.
- Detection uses positive enabled source conditions, including condition presets. Negated/excluded keywords do not identify the weapon. Ambiguous alternatives keep **source weapon conditions**. Name-only guesses are labeled **Name hint — review**; HKX motion does not contain a reliable weapon-class tag. Mixed weapon packages remain complete selections: choose a weapon class manually if you want to narrow the package to that weapon.
- Source gameplay/equipment conditions remain in place. Your conditions are ANDed with them. Selecting a different weapon type does not override an incompatible source weapon restriction or mirror its motion.
- Simple top-level player selectors (`IsActorBase` with Skyrim.esm:7, or `IsForm` with Skyrim.esm:14, including their negations) are replaced by your Player/NPCs/Both target. **Actors: Source** retains them. More complex nested actor restrictions remain intact. Check **Source rules** for those cases. Hand **Either** uses OR; **Both** requires the selected weapon class in both hands.
- Skill comparisons use the actor's **base** One-handed, Two-handed, Archery, or Block value. Empty bounds impose no restriction. FormIDs are local hexadecimal IDs paired with their plugin filenames, not load-order-prefixed runtime IDs.
- Source submods keep their relative priority order. A bundle occupies a priority range beginning with your chosen priority. Potentially conflicting output ranges are checked; the application does not claim to solve every custom plugin condition offline.
- Advanced conditions accept a JSON array in native OAR format. This permits additional conditions without introducing a custom stance framework. Complex random selection/persistent combat styles must be configured and verified in OAR separately.
- **BFCO + native OAR** maps supported MCO slot filenames and variant folder names to BFCO; all HKX bytes and annotations remain unchanged. The **Conversion** tab shows the mappings and blockers before export. This follows the [MCO-to-BFCO converter author's current filename-based workflow](https://www.nexusmods.com/skyrimspecialedition/mods/119926). Use a BFCO release supporting MCO annotations; original effects, payloads and behavior dependencies can still matter. Unknown slots and output collisions block conversion. Vanilla-slot Animated Armoury animations are not automatically authored BFCO chains.
- Root presets, effective submod configuration and animation variants are retained. OAR `user.json` replaces author data while retaining author metadata, matching OAR's loader. Source rules show the files that supplied the effective configuration. Per-animation configuration paths and known self-references are adjusted to the new output folders. Sources using `overrideAnimationsFolder` redirects currently block export and require manual resolution.
- Keep supporting source plugins, sounds, effects and other dependencies installed. The ZIP contains animation replacements and configuration, not arbitrary supporting assets or behavior graphs. Its README lists dependencies found in configuration; annotations and scripts may require additional dependencies.
- The manifest records every source path, destination, and SHA-256 checksum. A separate `moveset-forge.conversion.json` records file mappings and migration notes. Source assets remain untouched. Configure output priority against other active OAR replacers; file overwrite order alone does not decide conditional animation selection.

## DAR to native OAR

[OAR already supports DAR animation mods in their existing folder layout](https://www.nexusmods.com/skyrimspecialedition/mods/92109). To use those mods in game, replace the DAR plugin with OAR and keep the animation mods. Do not run the DAR and OAR plugins together. Native migration is optional, useful when assembling a new named OAR replacer with editable JSON rules.

To migrate with Animation Composer:

1. Choose **OAR · keep original slots** above the output list. This reveals non-combat sets too; the desktop selections remain third person.
2. Select a DAR source. New drafts in this mode default to **Keep source weapon conditions** and **Actors: Source**, so they add no equipment or actor restriction. Changing the export target does not rewrite rules already added to a project; review those separately.
3. Review the **Source rules** and **Conversion** tabs, then add the selection and export. The app creates native `OpenAnimationReplacer/.../config.json` files, retains original HKX filenames/bytes, and preserves source condition logic. Relative submod priority order is retained within your chosen output priority range. MCO animations exported in this mode still require their original combat framework.

Supported legacy conditions include equipment types/forms/keywords, actor/race/form restrictions, perks/spells/effects, worn forms/keywords, actor values and globals, level/faction comparisons, movement direction, random choices and common actor-state checks. Plugin/actor-base DAR directories are supported as well as `_CustomConditions` folders. AND/OR grouping and NOT are preserved. An unknown clause blocks the whole affected selection; it is never silently omitted. Existing native OAR overrides can supply a valid replacement condition tree.

Migrated Random conditions require OAR 2.3+ and use OAR's default legacy setting that keeps random results across loops. If you changed that OAR setting, review the exported Random state. Native migration requires an existing DAR/OAR folder structure. It does not convert LE animation formats, behavior graphs, paired rigs or arbitrary supporting assets. The profile importer resolves loose configuration overlays, not a complete simulation of MO2's runtime asset conflicts or archive load order.

## Preview scope

The renderer displays the actual skeleton motion and a generic weapon-direction guide, with playback, scrubbing, orbit, zoom, and an event list. It does not render Skyrim's character/weapon meshes, simulate hit detection or NPC AI, execute payloads, or reproduce AMR/game-driven root motion. The in-place toggle controls the skeleton's existing root translation. Final gameplay validation takes place in Skyrim.

Chain playback starts at normal attack 1 or power attack 1 when present. **Follow annotations** reads the BFCO next-attack events and MCO next-attack variables, takes the earliest corresponding input window, and follows that branch. It stops before revisiting a slot or crossing a missing link. If there are no recognized chain annotations, it uses filename order and reports that fallback. **All numbered clips** plays every numbered attack in numeric order at its full duration. Neither mode edits source files or changes exported timing. Charged loops/outros and input-dependent branches are not automatically simulated.

The **Sequence** tab lists the chosen route, transition times, filename-order fallbacks, and missing links. Scrubbing and playback speed apply to the whole sequence; **Loop** repeats the preview. Conditional subfolders remain separate during previews. An OAR variant is chosen deterministically, preferring the selected clip where applicable; other variants remain selectable in the clip list. Preview-folder selection does not narrow the complete bundle used for export. A chain preview does not reproduce Skyrim's behavior blending, runtime OAR choices, or root-motion transitions. The BFCO annotation reference is [the framework author's documentation](https://www.nexusmods.com/skyrimspecialedition/mods/117052).

Supported decoding includes Skyrim HKX spline animations using Polar32, ThreeComp40, ThreeComp48 and uncompressed quaternions, plus interleaved animations. Multi-block sampling, bindings, unanimated reference-pose bones, and **nonuniform bone scale** are supported. Version 0.2.0 preserves all three scale axes and composes full affine bone matrices, fixing the earlier "Nonuniform animated scale" rejection. Additive animation, other compression modes, paired/creature-specific setups and behavior/physics HKX files may be indexed but are not promised playable previews. Choose the correct skeleton; matching track counts alone cannot prove skeletal compatibility. A 156-track clip cannot be mapped safely to the installed 126-bone XP32 skeleton without the correct rig; the preview now explains that mismatch directly.

## Build from source

Requires .NET 8 SDK. From this directory:

```sh
dotnet build src/MovesetForge.Desktop -c Release -m:1
dotnet build tests/MovesetForge.Tests -c Release -m:1
dotnet tests/MovesetForge.Tests/bin/Release/net8.0/MovesetForge.Tests.dll
dotnet publish src/MovesetForge.Desktop -c Release -r linux-x64 --self-contained true -o dist/linux-x64 -m:1
dotnet publish src/MovesetForge.Desktop -c Release -r win-x64 --self-contained true -o dist/win-x64 -m:1
```

Optional collection verification: append your `mods` folder to the test executable command. The collection test uses the For Honor in Skyrim, BFCO Elden Rim and XP32 folders when present. No game assets are included in the source or releases.

The CLI supports `scan <folder> [library.json]`, `preview <animation.hkx> <skeleton.hkx>`, `check <project.json>`, and `build <project.json> <output-parent>`.

The import cache defaults to the user's local application data folder under `MovesetForge/imports`. Set `MOVESETFORGE_CACHE` to choose another folder. Deleting the cache is safe when no project is exporting; archives are imported again on the next scan. Saved projects currently use absolute paths. After moving to a different computer, scan the new source paths and re-add the selected sets; automatic path rebinding is not implemented.

Projects saved with Moveset Forge open in Animation Composer. See `docs/VALIDATION.md` and `THIRD_PARTY_NOTICES.md`.

## Releases

Every push to `master` runs the fixture tests, builds both self-contained downloads and launches each packaged app on its own operating system. When the checks pass, GitHub tags the pushed commit and publishes the downloads, their SHA-256 checksums and the corresponding source. The first release is `v0.4.1`. Each subsequent successful push increments the patch number. Rerunning a release for the same commit reuses its tag.

To build the same packages locally, check out a release tag and run `bash tools/publish.sh` with Python 3.9+ and the .NET 8 SDK installed. The archives and `SHA256SUMS.txt` are written to `dist/packages/`.
