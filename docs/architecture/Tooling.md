# Tooling

The runtime and editor tooling over CSVM's readers: the `--dump-*` probes and their wrappers,
captures, golden-image hash, glTF export, synthetic-data tree and campaign mission editor. The game
and `CSVM.Testing` both depend on this namespace; the one call from here into the harness is
`--run-tests`' dispatch in `ProbeRunner.cs`.

One `## src/...` entry per module, body at most 8 lines, 12 for the highest-traffic modules.

Traps do not live here; the rule is in `docs/architecture.md`.

## src/Tooling/Probes.cs
The assertion cores behind the `--dump-markers` / `--dump-weapons` / `--dump-loadout` /
`--dump-flight` / `--dump-mips` / `--dump-ai` / `--dump-moonlit` / `--damage-test` / `--effects-test` reports. Each
probe does the work once and returns both halves: the report text a flag prints and writes, and the
structured verdict a `--run-tests` suite asserts on, so a dump and the suite reading it cannot
disagree. The envelope, effects, damage and debris-shading sweeps each carry their thresholds, row
order and sources at their own members. The reachability half of a flight row is
`EnvelopeMargins.cs`, the suites asserting on these verdicts are the `Testing/*Suites.cs` modules,
and the branch vocabulary is [../org/flightModel.md](../org/flightModel.md).

## src/Tooling/EnvelopeMargins.cs
The reachability half of the flight-envelope report. `Sample` reads one completed `FlightModel`
step's public state and keeps, per scenario, the distance to each term that could have bounded it
(the G clamp, the C_L ceiling, the AOA window, the stall speed, the altitude band, the dive cap);
`Take` formats that as the row's `margins:` line, and across an airframe it also records which of
`Branches` the scenarios reached. Nothing here feeds a force. Read `Probes.cs` for the report it
lands in; the branch names and which instrument drives each unreached one are in
[../org/flightModel.md](../org/flightModel.md), "Parity ledger".

## src/Tooling/GoldenShot.cs
The engine half of the golden-image tripwire: `PixelHash(Image)` (md5, lower-case hex) and
`Adapter()` (`"<gpu> / <api>"`). Called at the `--screenshot` save site, which prints
`[core] shot pixmd5=… size=… gpu=…` on every capture; `RunTests.ps1`'s `goldens` stage parses that
line and compares against `analysis/goldens/manifest.json`. The Launcher's `[perf] gpu=` line
reads `Adapter()` too.

## src/Tooling/MemoryAdmission.cs
The engine's side of the machine-wide memory ledger `MemoryLedgerCore.ps1` keeps in
`%TEMP%\csvm-mem` (docs/tooling.md, "The memory ledger"). `Refuses`, called once from
`Launcher._Ready` after the log opens (editor builds on Windows only), judges a non-interactive
launch: below the floor it logs the reason and the Launcher quits with `TripwireExitCode` (75);
started without `CSVM_MEM_RESERVATION` it holds a delete-on-close reservation file for the process
lifetime. `Kind`, `IsNonInteractive` and the floor mirror the script's rules.

## src/Tooling/ProbeRunner.cs
The `--dump-markers`/`--dump-weapons`/`--dump-flight`/`--dump-loadout`/`--dump-mips`/`--run-tests`/
`--effects-test`/`--damage-test`/`--destroy=` probe wrappers, constructed once in `Launcher._Ready`
after the base paths settle: the Launcher dispatches the early quits itself and hands the runner to
each session node. Each method reads a `SessionSpec` passed per call rather than storing one. It
also holds `TriggerDestroy` (`--destroy=`) and `ForceObjective` (`--debug-objective=`), the two
scripted world forces belonging to no session. `RunTestSuites` is the one call into `CSVM.Testing`
from outside it. Read `Probes.cs` for the work each wrapper calls into.

## src/Tooling/ShaderDiagnostics.cs
`--debug-shaders`: a census of the distinct shaders the running tree draws, by `SceneBuilder`
family or owning node type, at sim frame 240 and after every live switch; the wall time and
pipeline compilations of the twelve frames after a switch; and every frame over 33 ms with the
pipelines it compiled. The pipeline counts are published from the render thread, since reading
them on the main thread waits for the previous draw. Off unless the flag is given.

## src/Tooling/CaptureDirector.cs
The `--screenshot=`/`--shots=`/`--frames=` capture state machine plus F11's pose print and F12's
ad-hoc save, built in `Launcher._Ready` from the spec and `Tick()`ed from its `_Process`, so
`--menu --screenshot` captures the launchscreen with no session alive. That capture reads
synchronously, since the process exits on its file; under `--xr-sim` it reads each eye's layer and
saves them side by side or alone. `SaveScreenshot`, the F12 save every screen shares, writes
`PaneReadback`'s frame into `ShotDir()` (`ShotDirFor`: the repo's `Screenshots/`, or one beside an
export's executable) on the worker it lands on. F11 reads each pane's own camera through the pure
`PlacementLines`: a `--freecam` line, `--fov=` off the external base, each aircraft's `--fly` line.

## src/Tooling/ControlChannel.cs
`--control=<port>`: a loopback line protocol an agent drives a live run through, built in
`Launcher._Ready` and `Tick()`ed from its `_Process` after the capture. A worker thread owns the
socket and queues each line; the main thread runs one command per frame and answers it, so nothing
touches the tree off it. It holds the sim through `GameClock.ScriptHeld` from the first frame a
clock exists, skips or fast-forwards the session's cutscene, and parks a camera of its own as the
viewport's current one, drawing the layers of the camera it stood down. `scripts/Control.ps1` is the client and lists the verbs.

## src/Tooling/GltfExporter.cs
Exports any `Node3D` subtree to glTF: mesh, live material state, no animation and no emitters.
`Export(node, path)` works on a throwaway `Utils/SceneCopy`, frees hidden `Node3D`s (damage is baked by
`Visible`) and the point-sprite `"lights"`, and converts every shader skin to a `StandardMaterial3D` over
reversed winding (`docs/formats/gotchas.md`: unreversed, this data exports inside out). File nodes and
materials take their gamez and texture names past the writer's renaming, so a model pack matches back.
`allStates` (the `--node=` one-shot) keeps hidden gamez nodes, written hidden, and shows the subject, so a
destructible's states reach the file. `.glb` by default. `ExportSet` writes several subtrees under one root at world transforms; `Export*ToExports` names a timestamped `Exports/` GLB, the writer `--export-gltf=`, F10 and NodeLab's export actions share.
A plane's mounted `cockpit1` (`PlaneBuilder`'s direct child) never rides in its file; given `cockpitPath`, it is written there all states, root at identity, in interior units (`CockpitPathBeside`; F10 suffixes `_cockpit1`). An untextured `lighting: false` skin (`DrawsUnlit`) is written `KHR_materials_unlit`.

## src/Tooling/CampaignRawList.cs
The shared exact-index lookup over raw alternating reader lists. It mirrors `ZrdrDict`'s
last-duplicate and bare-key behavior while retaining selected value indexes for typed editors. It
also locates first immediate strings, scalars, lists and vectors, with finite variants where needed,
for readers that use raw walkers. `CampaignPlayerInit.cs`, `CampaignWeatherCloudBand.cs`,
`CampaignWeatherWind.cs`, `CampaignWeatherPrecipitation.cs`, `CampaignWeatherFog.cs`,
`CampaignWeatherLighting.cs`, `CampaignMissionZeppelins.cs` and `CampaignMissionGenerators.cs`
are its consumers.

## src/Tooling/CampaignMissionDocument.cs
One mission's complete raw `ZrdrFile` set as an editable deep copy. Raw scalars and all typed
adapters publish atomic operations; objective keys only change through validated swaps. Operations
also cover start-animation moves, target replacement or chapter copy, and danger-zone edits.
Revision follows history, a monotonic generation rejects stale bindings, and the clean marker follows
the last export. Export writes every reader in source order to the canonical external-mod ZIP,
refuses a preferred unpacked sibling, and reopens the temporary ZIP to compare names, trees, nulls
and float bits before replacement. Read `CampaignMissionEdit.cs` and
`CampaignMissionEditorPlugin.cs` next.

## src/Tooling/CampaignMissionEdit.cs
Immutable operations retain document identity, before/after state and exact raw snapshots, including
float bits and structural reader changes. A document-bound session registers completed edits and
replays only matching operations; failed registration cannot leave a replayable edit. Callbacks
refresh after successful history transitions or rebind after registration rollback. The document
validates all affected slots before writing and preserves identity for start-animation permutations.

## src/Tooling/CampaignMissionUndoBridge.cs
The Godot 4.7 editor bridge records already-applied operations in global editor history with
`CommitAction(false)`. A bound reference keeps the callback target alive; mission replacement or
plugin exit invalidates its session and snapshots without clearing other editor history. Stale
actions are inert. A publication signal rejects nested assigned actions and rolls their edits back.
Undo and redo call the dock's refresh after the document notifies projections.

## src/Tooling/CampaignMissionDependencyBundle.cs
The selected campaign mission's read-only dependency set: shared readers, chapter world, highest
available chapter texture tier, chapter readers, chapter and mission compiled animations, global
messages, interpreter scripts, and parsed mission setup. Each dependency carries its exact source
path and a ready, missing, unavailable, empty-or-unavailable, or not-checked state. The bundle
materializes models without owning export, resources, Godot nodes, or the source, and a fresh
generation identifies each selection. Read `Spec/GameData.cs` for its lightweight retail loader.

## src/Tooling/CampaignMissionImport.cs
Strict import of an existing complete mission `zrdr.zip` or unpacked `zrdr` directory. It rejects
nested, unsupported, duplicate, malformed, partial and precedence-conflicted layouts before
building a deep-copied document. Retail reader names establish completeness only; retail values
never fill omissions. Mod-only readers and observed order remain in the imported scope, while the
result retains whether the effective owner is a ZIP or directory.

## src/Tooling/CampaignReferenceRecords.cs
Immutable mission-reference occurrences, exact raw locations, field-specific resolution rules,
verdicts, coverage issues and snapshots. A snapshot distinguishes resolved, newly missing,
inherited missing, unverified and duplicate-suppressed references so an unchanged retail or
existing mod scope remains exportable without hiding its inherited caveats.

## src/Tooling/CampaignReferenceExtractor.cs
Source-preserving projections for objective, roster, target, generator, zeppelin, pickup,
animation, setup and ancillary references. Startup names follow the selected reader's exact entry
leaves. Target paths follow mission or chapter scope; zeppelins follow selected prefixes and parts.
Roster, generator, danger-zone and objective references follow runtime readers and directive slots.
Net IDs follow runtime integer conversion, and marker messages follow its flag threshold. Other
readers retain their own duplicate rules. Runtime-staged or asset-backed coverage remains
unverified. `location.zrd` receives a nonblocking family issue while the generic
`ANIMATION_DEFINITION_FILE` scan still applies to it.

## src/Tooling/CampaignObjectiveReferences.cs
Exact-case, first-directive objective references follow the contiguous selected runtime blocks,
while briefing messages include every case-insensitive keyed identity the briefing reader uses.
Each target argument is a separate bare node or scoped path, and each `INACTIVEn` is one world
path. Raw leaf paths, shadowed links and unresolved or staged references remain visible.
`CampaignReferenceExtractor.cs` retains the separate generator, danger-zone and generic animation
projections.

## src/Tooling/CampaignReferenceIndex.cs
Field-specific indexes resolve mission references against the document and dependency bundle.
Startup names use the compiled-first `AnimProgram` inspection and its scope gates; unavailable
inputs remain unverified. Target definitions use the world matcher, generator origins the host
subtree, and roster net IDs nonempty chapter graphs. Zeppelin gasbags without a matching healthy
node remain unverified. Danger gates follow the world, AI ribbon and selected objective consumers;
disabled objective gates are suppressed and danger slots are not objective-graph references.

## src/Tooling/CampaignReferenceDiagnostics.cs
The selection-bound owner of continuous cross-reader diagnostics. It snapshots one document
revision, extracts and resolves all occurrences, publishes one immutable aggregate, and marks
itself stale on subsequent edits. The first snapshot records inherited misses; target and danger-zone
identities survive their structural edits, while startup entries retain identity across reorders.
Objective danger references retain raw leaf identity across number swaps. Only new confirmed misses
and blocking dependency or parse errors prevent export.

## src/Tooling/CampaignMissionTargets.cs
Exact-index target-entry views follow `MissionTargets`' whole-file scope selection, last-pair
rules and case-insensitive last-node replacement. Label edits preserve unknown pairs and tails;
flag removal refuses extra payload. Chapter tables are read-only until an explicit, revision-bound
deep copy creates a mission-local override. World and message references publish through
`CampaignReferenceDiagnostics.cs`.

## src/Tooling/CampaignMissionZeppelins.cs
Exact-index views of mission zeppelin poses, motion, behavior and existing damage-list prefixes.
The adapter follows the reader's last-pair and variable-width rules, identifies shadowed or
unmatched parts, and validates a complete selected row before a document batch. Absent and
unsupported fields stay raw. `CampaignReferenceExtractor.cs` projects their references and
`Mech3/Zeppelins.cs` owns the runtime reader.

## src/Tooling/CampaignMissionGenerators.cs
Exact-index views of existing mission generator timing, origins, templates, nets and door fields.
The adapter validates each selected row before a document batch and leaves unknown or
unselected data raw. Origin offsets are derived in the existing chapter host's source frame.
`Mech3/EnemyGenerators.cs` owns the reader and `CampaignReferenceDiagnostics.cs` checks joins.

## src/Tooling/CampaignMissionObjectives.cs
Revision-bound, exact-index views of selected `OBJECTIVEn` blocks retain their raw directive
order, duplicates and unknown data. Existing identity, timing, objective-link, inactive-path,
target-path and help-message leaves validate as complete rows before a document batch. Number
swaps validate every block and known link before one atomic batch, refusing gaps, unknown
directives and ambiguous links. They do not preserve rotating runtime completion order or verify
external consumers. `Session/Objectives/ObjectiveScript.cs` owns runtime selection;
`CampaignObjectiveReferences.cs` supplies reference diagnostics.

## src/Tooling/CampaignMissionStartAnimations.cs
Revision-bound views select each runtime-consumed start-animation entry by exact raw index.
Renaming changes only its first string; reordering moves complete entries among selected slots in one
document revision. Unknown entries, tails and unrelated reader data remain in the canonical tree.
`CampaignReferenceDiagnostics.cs` supplies the per-entry verdicts.

## src/Tooling/CampaignMissionReaderSummaries.cs
Read-only structural map information, multiplayer spawn values from `Flight/Modes/SpawnPoints.cs`,
animation-index paths and effective program counts. Animation inspection uses the mission's
read-only dependency bundle and reports unavailable inputs instead of inventing a complete program.
`CampaignMissionEditorPlugin.cs` presents these projections alongside startup editing.

## src/Tooling/CampaignMissionDangerZones.cs
Revision-bound mission-only danger-zone override views follow the selected reader, last alternating
keys, direct membership strings and last valid numeric pair. The adapter validates one row before
replacing its raw root, preserving unknown data and untouched tails. Objective blocks and chapter
gate verdicts are read-only; ribbon samples retain the winning gamez source index and use the
runtime's numeric-index selection before setup.

## src/Tooling/CampaignDangerRibbonPreview.cs
A non-interactive top-down X/Z plot of the selected chapter ribbon's sampled source geometry.
The dock owns labels and selection; the plot does not edit gamez or apply mission setup.

## src/Tooling/CampaignMissionPreview.cs
Revision-bound player, roster, target, generator, zeppelin and danger-ribbon projections over typed
readers and the preview world. Each selection retains its scope, reader or world source, and raw
path, with related mission paths where applicable. Reader snapshots coalesce changes, replace only
changed overlay layers, and invalidate structurally displaced selections. Named locations have no
established point consumer. The model unsubscribes on disposal.

## src/Tooling/CampaignMissionPreviewWorld.cs
Builds a collision-free, transient chapter world, applies mission setup and indexes original gamez
names and indices for scoped target and host-relative generator resolution. Zeppelin poses are
placed on temporary hosts and restored before reapplication. Editor-only shader defaults are shared
while previews are open, without overwriting existing globals. Disposal frees the world and textures.
Preview-owned materials are removed from the shared shader tracking table on disposal.

## src/Tooling/CampaignMissionPreviewPanel.cs
The opt-in viewport owns derived handles, ribbons, camera controls and selection rows. It identifies
mission, chapter and world sources and can navigate the first mission source in the raw tree. It
rebuilds changed layers only and frees the viewport and world on close, rebind or plugin exit.

## src/Tooling/CampaignPlayerInit.cs
The typed view over `objectives.zrd`'s complete `PLAYER_INIT` record. It follows `ZrdrDict`'s
last-duplicate rule, retains each field's exact raw-list index, and writes through
`CampaignMissionDocument` only. Position and authored attitude remain floats, throttle is a
fraction, and speed is exposed in metres per second through `SpawnPoints.SpeedScale`. The pending
flag is read-only.
The preview uses the selected file and position path directly.

## src/Tooling/CampaignRosterStarts.cs
Exact-index typed views over the complete spawn prefix and independent existing AI fields of
variable-width `aiv.zrd` blocks. Supported net choices, durability, flags, ratings, maneuver mask,
target and marker text, scripted path and voice write in validated atomic groups through
`CampaignMissionDocument`. Names, enabled flags, template headers, unsupported values and
unknown tails remain raw. The human `player` block is read-only because campaign placement reads
`PLAYER_INIT`. Read `Mech3/AiSkills.cs` for the runtime interpretation.

## src/Tooling/CampaignWeatherColor.cs
The shared authored-color rule for typed weather editors. It identifies normalized versus 0–255
triples with the runtime's strict component-above-1 test and validates edits without silently
changing that encoding. `CampaignWeatherCloudBand.cs`, `CampaignWeatherPrecipitation.cs`,
`CampaignWeatherFog.cs` and `CampaignWeatherLighting.cs` use it.

## src/Tooling/CampaignWeatherCloudBand.cs
The exact-index typed view over `weather.zrd`'s `CLOUD_COVER` geometry and optional colors. It
follows the outer alternating dictionary's last-list selection, the first valid immediate
geometry scalars, and the first immediate top/bottom color lists, matching `Effects/Weather.cs`.
It validates the band, opaque core, and each existing color before writing. Missing colors,
duplicate and unknown fields, and every other weather block stay in the raw mission document.

## src/Tooling/CampaignWeatherPrecipitation.cs
The exact-index typed view over `weather.zrd`'s top-level precipitation siblings. It follows the
runtime's first-match rules for type, color, drift, gravity, optional particle count and alpha
gradient. Writes keep absent particles absent, retain raw color encoding and list tails, and leave
duplicates, unknown values and unrelated weather data in the mission document.

## src/Tooling/CampaignWeatherWind.cs
The exact-index typed view over `weather.zrd`'s bare-scalar `WIND` block. It follows the outer
alternating dictionary's last-duplicate selection, then binds the first complete immediate value
for static velocity, random maximum speed, random acceleration and random turn rate, matching
`Effects/Weather.cs`. Writes preserve duplicate and unknown fields and every other weather block.

## src/Tooling/CampaignWeatherFog.cs
Dynamic exact-index views over `weather.zrd`'s runtime `ZONE<digit>` fog blocks, in file order and
without their `SW_*` twins. Each view edits fog color, distance range and altitude range while
retaining the source's normalized or 0–255 color encoding. Unknown and duplicate values remain in
the raw mission document. Read `Effects/Weather.cs` for the runtime interpretation.

## src/Tooling/CampaignWeatherLighting.cs
Dynamic exact-index views over each runtime zone's selected clip and sunlight lists. Each view
edits clip far, diffuse and ambient scalars, both colors, the bicolored flag, and existing shading
orientation components. Runtime-fallback fields remain independently optional and are never
created. Last-list selection, source color encoding, vector tails, duplicates, unknown fields,
zone order, and `SW_*` twins stay intact. Read `Effects/Weather.cs` for the runtime interpretation.

## src/Tooling/CampaignWeatherDiagnostics.cs
The engine-free, read-only weather summary over the editable mission document. It rebuilds
`Effects/WeatherState`, formats cloud, probe, zone, wind, precipitation and lighting labels, and
produces normalized cloud-band and fog-range plot values. It deliberately has no chapter geometry
or fog-volume dependency, so its camera-state result is labelled band-only. Read
`CampaignWeatherPreview.cs` for its Godot view.

## src/Tooling/CampaignWeatherPreview.cs
The Godot `Control` that draws `CampaignWeatherDiagnostics` as a vertical cloud-band plot and a
horizontal selected-zone fog-range plot. It draws no text or chapter geometry; the dock owns the
labels and the explicit dependency boundary. Read `CampaignMissionEditorPlugin.cs` for refresh and
selection ownership.

## src/Tooling/CampaignMissionEditorPlugin.cs
The enabled Godot dock over `CampaignMissionDocument` loads retail and existing-mod mission
scopes, dependencies and raw scalar cells. Typed tabs cover starts, roster AI fields, targets,
zeppelins, generators, objectives, danger-zone overrides, startup animations and weather. It shows
continuous reference verdicts, read-only reader summaries and explicit chapter-target copying.
The read-only Preview tab presents disposable world overlays and mission source navigation. The
dock refreshes typed views and diagnostics on editor-history replay and refuses export on new
confirmed reference errors; other structural edits remain outside it.
Format and workflow: [../formats/missions/campaign-authoring.md](../formats/missions/campaign-authoring.md).

## src/Tooling/SyntheticData.cs
`--synthetic-data`'s tree: an `extracted/` written into `.scratch/synthetic-data/<pid>/`, which
`Launcher._Ready` then reads as the data root. Engine-free, so `CSVM.Tests/SyntheticDataTests.cs`
builds the same tree. Every other synthetic model is built in memory (`SyntheticGameData.cs`), so
the one record family left is the Original shell's, `UI/Menu/Original/SyntheticShell.cs`'s
`TreeFamilies`, the list `Build` is handed. Its records are `CSVM.Tests/fixtures/` files read from
the checkout, so an export has none and refuses the switch. The stamp carries `schema` and a
`synthetic` field, which `Marks` reads for the log and `test-report.json`'s `syntheticData`.
Read `SyntheticGameData.cs` for the models, `Extraction/PngWriter.cs` and `WavWriter.cs` for the encoders.

## src/Tooling/SyntheticTextures.cs
The synthetic run's C1 texture archive, built in memory as a `TextureSet` that
`SyntheticGameData`'s chapter loader hands C1 (every other chapter gets none): one opaque checker
record per `Entries` row at that row's size, so a texture is added by adding a row. Every name is
`probe_*` except `smoke101`..`103`, the exhaust trail's pool, which `ExhaustSmoke` names in code.
The tree writes no texture file.

## src/Tooling/SyntheticPlane.cs
The synthetic tree's stand-in aircraft, every record invented: `probe_plane`, the default, and nine under
shipped airframe names the suites fly (`player_pfighter`, the lobby door's starter, the custom-plane
pair and six more), each carrying what the code keys on (`is_autogyro`, a turret mount). `Gamez`
builds their archive in memory from `SyntheticPlaneRecords.cs`, one outward-wound box per entry,
for `SyntheticGameData`'s `Planes`. Their `vehicle`, `engines` and `player` readers, the maneuver library,
the weapons, the `ai` turret table and `shakes` are `SyntheticZrdr.cs`'s; the names are in `SyntheticMessages.cs`. `Launcher`
sets the fits as `StockLoadouts.Supplement` and the probe as `SessionSpec.DefaultPlane` under the switch.
Shapes: [gamez](../formats/gamez.md), [markers](../formats/markers.md), [vehicle](../formats/vehicle.md), [weapons](../formats/weapons.md), [turrets](../formats/turrets.md).

## src/Tooling/SyntheticPlaneRecords.cs
The stand-in aircraft archive as code: `Nodes`, every node in flat order (children by flat index,
rotations zero, no flags), and `Boxes`, one box per mesh index with its material. Each airframe has
`geometry` → `healthy` → one LOD, the `markers` rig (eight firepoints in mirror pairs, eight pylons
odd to port, `target`, `cockpit_camera`, two exhausts), `dontmove` props, `destroyed` pieces, `pdpN`
panels and a `cockpit1` interior; the autogyro adds a `rotor1` blade, the kestrel and the fbrand a
turret rig. Shapes: [gamez](../formats/gamez.md), [markers](../formats/markers.md).

## src/Tooling/SyntheticGameData.cs
The game data (`Spec/GameData.cs`) `BasePaths.Resolve` hands a `--synthetic-data` run: every model
built in memory from records written in code, with no game-file source, so nothing in the run can
reach an install. A format switched to `GameData` adds its builder here and drops its tree family
and fixture. Engine-free, so the units build the same models. Read `SyntheticMessages.cs` and
`SyntheticZrdr.cs`.

## src/Tooling/SyntheticMessages.cs
The synthetic run's message table: the `MSG_*` keys the stand-in records name and the ones the HUD,
the match lines and the dock prompt read directly, every value invented, built through
`Messages.Of`.

## src/Tooling/SyntheticImages.cs
The synthetic tree's JPEG and TGA writers, for menu art a layout or a script names by those
extensions, since a loader picks its decoder by the name: a baseline greyscale JPEG whose 8x8
blocks are flat, so each is one DC term, and an uncompressed top-down 24-bit TGA. Both draw a
checker shaded from the name. PNGs stay with `Extraction/PngWriter.cs`.

## src/Tooling/SyntheticSounds.cs
The synthetic run's sounds. The definitions are `SyntheticZrdr.cs`'s `sounds` reader, its `SETS`
and `SOUND_GROUPS`. `Zbd()` builds the WAVs in memory for
`SyntheticGameData`'s `Sounds`: one generated mono MS ADPCM WAV at 22050 Hz per `Wavs` row (a
length and a tone, or seeded noise), packed into the `ZbdArchive` container and opened with
`SoundZbd.Read`, the install's reader. `SyntheticSoundsTests` fails when the rows and the `SETS` WAV
names differ, so a sound is added by a definition and a row together. Every definition is `snd_probe_*` except the music cues `MusicPlayer` looks up, the propeller start the spawn suite reads, and the voice sets, named `snd_id<N>_*` as `CombatVoice` builds them for the ids `PilotVoices` seats.
The `voice` reader is the accent table dealing those ids. Shapes:
[../formats/sounds.md](../formats/sounds.md), [../formats/combat-voice.md](../formats/combat-voice.md).

## src/Tooling/SyntheticMission.cs
The synthetic run's invented mission addresses, `C1/PROBE1` (the weather, sun and net-table
suites' mission) and `C1/M09` (the campaign's one story position), whose readers are
`SyntheticZrdr.cs`'s.

## src/Tooling/SyntheticZrdr.cs
The synthetic run's reader scopes, built in code for `SyntheticGameData`: `Root()` holds the
stand-in aircraft, armament, effect, sound, voice, campaign and briefing records; `Chapter("C1")`
is an existing empty scope, the speed cue's; `Mission` gives `C1/PROBE1` its `weather` (two zones
with different sun bearings under a cloud band) and `net` (one 16-entry block), `C1/M09` its
`objectives`, and the `IA1` of C1B, C3, C4 and C5 the `ia` record, C1 having none since a suite
reads `C1/IA1` as an install's spawn lists. Every other scope is `ZrdrSet.None`, so a suite gated on
a shipped mission skips. What each record exercises: `CSVM.Tests/fixtures/README.md`. Shapes are in
[../formats/](../formats/README.md).

## src/Tooling/SyntheticEffects.cs
The synthetic run's effect models. `Gamez` builds the template gamez in memory, one node per
template root and child with a box per meshed node. `Anims` builds the compiled archive in memory (label `probe_effects`,
`AnimArchive.FromDecoded`, a fresh one per call): 25 OnCall definitions as the property trees `AnimDefinition.Parse` reads, no SI
scripts. They cover the HE burst, a gun hit, the seeker's flare, the sonic rings, the panel tears' flakes, the water crash's
`wait_for_completion` call, a two-sequence building death, a call in a first-person `If`/`Else`, the propeller and autogyro spins
and the torpedo's folded `reset_state`. The reader destructibles, `flame_ball` and `pufftrails` are `SyntheticZrdr.cs`'s.
No session loader reads any of it; `Testing/EffectStageSuiteHelper.cs` does, under the switch. Names the engine or a
suite looks up are kept over invented content.

## src/Tooling/WavWriter.cs
Encodes mono 16-bit samples as a RIFF/WAVE file, plain PCM or MS ADPCM (the seven standard
coefficient pairs, 256-byte blocks, a `fact` chunk with the true length), the layout
`Mech3/WavFile.cs` decodes. Each ADPCM block is encoded under every coefficient pair and the closest
kept. Engine-free, so `CSVM.Tests/SyntheticSoundsTests.cs` round-trips it through the reader.

## src/Tooling/DumpGameFiles.cs
`--dump`: `strings.dll` and every `ZbdManifest` archive (one chapter's with `--chapter=`) through
the readers, each where `extracted/` kept it: `messages.json`, `interp.json`, `zrdr/<key>.json` (the
root list as `Zrdr` reads it), `<C>/gamez/{nodes,meshes,materials,textures}.json`, `cam_anim/`
and `mis_anim/` as `definitions.json` plus `scripts.json` in pool order, a texture archive as
`<key>.png` plus `manifest.json`, a sound archive as `<key>` WAVs. JSON is the models' own field
names; a Godot vector, colour or quaternion is an array, a `Transform3D` twelve floats (basis
columns, then origin), NaN the string `"NaN"`. ⚠ The engine never reads a dump; nothing falls back onto one.
