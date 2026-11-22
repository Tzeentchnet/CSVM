# Campaign Mission Editor Roadmap

**ACTIVE PLAN** (written 2026-11-21). It sits at the repository root by explicit request;
`PROJECT_CONTEXT.md` names it as the live campaign-editor plan. When every item lands, the closing
commit deletes this file, records completion in its message, and clears the status pointer. Any
live prose linking this file by path is unlinked in the same commit.

This plan extends the existing lossless campaign mission document, complete-scope exporter, raw
tree, player and roster starts, and bounded weather editors into a practical mission-authoring
workflow. It covers the remaining runtime-backed mission readers, read-only dependency loading,
reference diagnostics, preview tools, existing-mod import, and editor undo.

The editor continues to target CSVM's external JSON mod scope. Binary archive writing, chapter
world authoring, compiled animation authoring, and redistribution of retail-derived mission
scopes remain out of scope. No checklist item comes from a backlog issue, so issue-state
re-verification does not apply.

## Milestone goal

- Every supported typed field writes its exact existing raw location without rebuilding unknown
  reader structure.
- Mission-local references are checked against the same chapter, shared, animation, and message
  dependencies that a flight loads.
- Existing complete mod scopes can be reopened, edited, validated, and exported safely.
- The Godot dock provides useful diagnostics, preview handles, and undoable typed edits.
- Each landed slice has runtime-parity tests, a clean editor smoke, and the repository verification
  gates required by `PROJECT_CONTEXT.md`.

**Retail data remains an input, never project content.** The roadmap adds authoring over a user's
own files, not a distributable mission-data pack or a writer for original binary archives.

## Ground rules

- **Original-game data drives everything.** Read the reader/compiled JSON before writing a handler;
  never guess a value. Inventing content is the mistake this project makes most often.
- **Evidence is a lead to verify, not a finding to implement.** Confirm every claim against the
  data/code before building on it; **a correct disproof that lands no code is a success here**, not a
  failure. Mark each item's Evidence with its confidence (traced-to-code / direction-sound-magnitude-
  TUNE / lead-only).
- **`PROJECT_CONTEXT.md` + the module's entry in `docs/architecture/<Namespace>.md` (plus its index
  bullet in `docs/architecture.md`) / `docs/formats/` are updated in the same turn** as each landed
  item; a landed item gets its record in the landing commit's message and its issue is **closed**. New decodes land with their `docs/formats/` page.
- **Read `docs/verification.md` before measuring anything**, the instruments here mislead; cite the
  rule that bites per item.
- **Verify against a full 8-chapter `--freecam --chapter=<X>` regression** (zero errors, same
  mesh/node counts unless the change is meant to add coverage) plus a targeted capture at the
  location the report came from.
- **Read the module's entry in `docs/architecture/<Namespace>.md` (found through the index in
  `docs/architecture.md`) before modifying it,** then the comments on the members you touch; dead
  ends are in the landing commits (`git log --grep=<ID>`), so search those before re-chasing one.

## Checklist

Statuses: ☐ open · ◐ in progress · ☑ done · ❌ closed/disproven. **Keep this in sync as items land.**

### Wave A, Complete weather authoring

1. ☑ Typed precipitation editing
2. ☑ Per-zone light and clip editing
3. ☑ Weather diagnostics and preview

### Wave B, Load dependencies and existing projects

11. ☑ Read-only mission dependency bundle
12. ☑ Continuous cross-reader reference diagnostics
13. ☑ Existing complete mod-scope import

### Wave C, Author remaining mission readers

21. ◐ Target flags and labels
22. ◐ Zeppelin placement and behavior
23. ◐ Enemy-generator timing and references
24. ◐ Danger-zone mission overrides
25. ◐ Named mission locations
26. ◐ Objective graph and directive editing
27. ◐ Remaining roster fields and AI-net references
28. ◐ Start-animation editing and read-only map/net summaries

### Wave D, Finish the Godot authoring workflow

31. ◐ Mission preview handles and overlays
32. ◐ Undo and redo for typed edits
33. ◐ End-to-end campaign authoring validation

## Dependency and parallelism notes

Wave A runs in order because all three items touch the weather adapters, the same dock panel, and
the same format pages. B11 enables B12 and D31. B12 should land before the reference-bearing Wave C
items, while B13 is independent of B12 but contends on `CampaignMissionDocument` and the dock.
Wave C items should run serially in this checkout because each changes the same plugin and
authoring page; C26 also consumes the diagnostics from B12. D31 waits for B11 and the relevant
Wave C adapters, D32 waits until the typed panels stabilize, and D33 closes the plan. Do not run
items that edit `CampaignMissionEditorPlugin.cs` in parallel worktrees.

---

# Wave A, Complete weather authoring

## A1 ☑ Typed precipitation editing

**Goal.** Edit an existing mission precipitation definition through typed controls while
preserving absent optional values, raw list tails, duplicate data, and every unrelated weather
field.

**Evidence (confidence: traced-to-code).** `WeatherState.ParsePrecip` in
`CSVM/src/Effects/Weather.cs:446` consumes the top-level bare precipitation keys documented in
`docs/formats/weather/atmosphere.md`. The current weather adapters establish exact-index editing
and runtime-parity testing.

**Approach.** Add a `CampaignWeatherPrecipitation` adapter over the first runtime-selected
`TYPE`, `COLOR`, `WIND_DIR`, `WIND_VEL`, `GRAVITY`, optional `PARTICLES`, and
`ALPHA_GRADIENT` values. Reuse shared color validation, expose an optional Precipitation tab, and
leave absent definitions absent.

**Model recommendation.** high.

**Verify.** Add invented-reader tests for exact paths, absent and malformed fields, duplicate and
tail preservation, validation-before-mutation, and `WeatherState.Precip` parity. Run the focused
tests, StyleCop rebuild, ordinary solution build, content checks, Godot editor smoke through
`RunProbe.ps1`, and `RunTests.ps1 -Quick`.

**⚠ Traps.** The precipitation fields are top-level bare siblings, not one list-valued block.
Do not route them through `ZrdrDict` or create a missing optional `PARTICLES` value.

## A2 ☑ Per-zone light and clip editing

**Goal.** Edit the runtime-consumed clip range, sunlight scalars, colors, flags, and sun
orientation for each discovered weather zone without changing zone order or software-renderer
twins.

**Evidence (confidence: traced-to-code).** `docs/formats/weather.md` and `WeatherState.Load`
identify the runtime fields, while the existing fog adapter proves dynamic `ZONE<digit>`
discovery and exact-index writes. The 24-reader campaign census finds 48 runtime zones, each with
canonical clip, sunlight scalar, color, bicolored, and three-component orientation lists.
`WeatherState` still defines independent fallbacks, so modified readers keep each list optional.

**Approach.** Extend the per-zone weather model or add a sibling lighting adapter after the
census. Reuse shared color encoding, keep `SW_ZONE*` blocks raw, and expose only fields with a
settled runtime interpretation.

**Model recommendation.** high.

**Verify.** Add invented-reader tests for C1-C4 `ZONE1`/`ZONE2`, C5 `ZONE1`/`ZONE3`, independent
optional keys, exact paths, color encoding, validation-before-mutation, and `WeatherState.Zone`
parity. Run the standard targeted, editor-smoke, and Quick gates.

**⚠ Traps.** Sun bearing is a shading direction, not the sun billboard position. Keep authored
zone order and the `SW_*` twins distinct from runtime zone selection.

## A3 ☑ Weather diagnostics and preview

**Goal.** Show the derived cloud band, whiteout ramps and colors, selected zone, fog ranges,
precipitation, wind, and lighting values without claiming to render chapter geometry the dock has
not loaded.

**Evidence (confidence: lead-only).** The typed weather adapters and `WeatherState` expose the
derived values, while `docs/formats/weather/atmosphere.md` records the midpoint regime transition
and whiteout interpolation. The current dock has diagnostics but no weather preview.

**Approach.** Build a read-only weather summary and simple altitude/range visualization from the
typed state. Keep world-deck and horizon geometry out until B11 and D31 provide the actual chapter
dependencies.

**Model recommendation.** high.

**Verify.** Unit-test the derived labels and plotted thresholds from invented states, smoke the
dock headlessly, and compare the preview values with `WeatherState` for representative weather
fixtures. Run `RunTests.ps1 -Quick`.

**⚠ Traps.** A numeric band preview cannot stand in for the chapter's deck or horizon meshes.
State the geometry boundary in the UI instead of drawing an invented cloud deck.

# Wave B, Load dependencies and existing projects

## B11 ☑ Read-only mission dependency bundle

**Goal.** Load the selected mission's chapter world, textures, shared and chapter readers,
compiled animation archives, messages, and setup script as read-only dependencies beside the
editable mission scope.

**Evidence (confidence: lead-only).** `docs/formats/missions/campaign-authoring.md` maps this
bundle and distinguishes its editable mission scope from wider read-only inputs. The dock
currently performs only the first three import steps described there.

**Approach.** Add a plain C# dependency-bundle model loaded through existing `GameFileSource`,
`GameData`, reader, and animation APIs. Keep Godot nodes and imported resource files out of the
model.

**Model recommendation.** <TODO: choose a capability tier after mapping the existing loader APIs
and ownership lifetimes.>

**Verify.** Use invented or metadata-only fixtures to prove dependency addressing, missing-input
diagnostics, and disposal behavior. Smoke selection changes in the editor and run the Quick gate.

**⚠ Traps.** Wider scopes are not mission-local save targets. Loading them must not imply they can
be rewritten or copied into the exported mission archive.

## B12 ☑ Continuous cross-reader reference diagnostics

**Goal.** Report unresolved world nodes, message keys, AI nets, animations, sound groups, targets,
and other typed references while the author edits, before export is offered as ready.

**Evidence (confidence: lead-only).** The validation contract in
`docs/formats/missions/campaign-authoring.md` lists the references that must resolve in one mission
bundle. The current dock reports malformed local records but does not build cross-file indexes.

**Approach.** Build immutable indexes from B11's dependency bundle and the editable document.
Have typed adapters publish reference records, then aggregate all failures into one diagnostic
surface rather than failing at the first missing name.

**Model recommendation.** <TODO: choose a capability tier after defining the shared reference
record and diagnostic ownership.>

**Verify.** Add invented-bundle tests with valid, missing, duplicate, and wrong-scope references.
Prove diagnostics refresh after an edit and clear after repair, then run editor smoke and Quick.

**⚠ Traps.** A name that exists in the wrong scope is still unresolved for the mission. Do not
silently fall back across complete replacement archives.

## B13 ☑ Existing complete mod-scope import

**Goal.** Reopen an existing complete external mission `zrdr.zip` or unpacked scope, edit it, and
export it again without mixing missing entries from retail.

**Evidence (confidence: traced-to-code).** `CampaignMissionDocument` already verifies complete
scope export, while `docs/formats/missions/campaign-authoring.md` states that an imported mod scope
must be treated as complete because runtime replacement has no per-reader fallback.

**Approach.** Add a separate import path through `ZrdrSet.FromExtracted`, retain the selected
source kind and ownership boundaries, and run the same deep-copy and reopen verification used for
retail-loaded documents.

**Model recommendation.** <TODO: choose a capability tier after deciding how the source picker
distinguishes retail inputs from mod scopes.>

**Verify.** Round-trip invented complete ZIP and directory scopes, reject partial or ambiguous
layouts, preserve entry order and float bits, and smoke both source modes in the editor.

**⚠ Traps.** Never fill a missing mod entry from retail. Refuse an unpacked sibling that would
take precedence over the archive being edited.

# Wave C, Author remaining mission readers

## C21 ◐ Target flags and labels

**Goal.** Edit mission target starting flags and labels with resolved message and world-node
references.

**Evidence (confidence: traced-to-code; retail census lead-only).** `MissionTargets.Load`
chooses the mission reader whole, then the chapter only when it is absent. A present empty reader
throws rather than falling back. Its entry walker selects the last label and nodes pairs, treats
each flag independently, and lets the last case-insensitive node key replace earlier ones.
`ObjectiveSites` resolves slash-separated node paths and gives `objective` priority when both flags
are set. The existing 23 mission-local / 1 chapter-inherited census is documented in
`docs/formats/missions/campaign-authoring.md`; the install-backed test still needs to run here.

**Approach.** Exact-index target views edit mission-local entries, publish target and message
references, and preserve unknown data. An inherited chapter table is read-only until an explicit
confirmation creates a deep-copied mission-local override.

**Model recommendation.** high, because duplicate semantics, cross-scope ownership, and
validation before structural mutation must agree with the runtime.

**Verify.** Invented-data tests cover exact paths, duplicate names and pairs, inherited refusal,
explicit-copy output, pre-mutation validation, preservation, diagnostics refresh and
`MissionTargets` parity. Run those tests, the install-backed census, editor smoke, and Quick
before marking C21 complete. The shell hook currently denies the focused test and build commands.

**⚠ Traps.** Editing an inherited chapter table changes more than one mission. Never make that
scope widening implicit.

## C22 ◐ Zeppelin placement and behavior

**Goal.** Edit each mission zeppelin's placement, orientation, motion limits, team, target, and
damage records without rebuilding unknown fields.

**Evidence (confidence: traced-to-code; story census pending).** `Zeppelins.Load` selects the
first reader and its first root list, skips non-list records, and requires a slot-0 `node` and
`net` string plus exactly three position floats. Its alternating dictionary selects the last
case-insensitive key, including a bare key. Scalars read slot 0; targets and engines accept direct
strings, healthy entries need two strings, gasbags need a string and float, broadside cannons
need three strings, and cannon-health entries need seven slots with stage pairs. Optional
animations use the first nested string, while malformed entries and extra tails remain raw.
`ZeppelinRuntime` counts duplicate healthy entries, uses their first state child, and chooses the
last gasbag record for a matching healthy node. `ZeppelinMotion` takes authored metres and degrees
without applying `min_pitch` or `max_pitch`; the damage consumer does not use cannon-health
`gunback` or `frame`. The existing install-wide format census reports 58 instances, 57 with
gasbags, 48 with cannons, 24 with cannon health, 16 with a team and three with inaccuracy. The
new 24-story-mission width and optional-field census has not run.

**Approach.** Bind the selected runtime-consumed leaves and variable-width prefixes by exact raw
indexes, validate each complete displayed row before one document revision, and retain every
unknown or unselected value. The dock separates placement, behavior, targets and part categories;
B12 projects hull, net, actor, part and animation references with duplicate and staging verdicts.

**Model recommendation.** high, because case-insensitive bare-key overrides, per-part duplicate
selection, conditional damage binding, authored units and atomic multi-leaf writes must agree.

**Verify.** Invented-reader tests cover malformed and variable-width records, exact writes,
unknown-tail and float-bit preservation, validation-before-mutation, duplicate behavior,
references, and `Zeppelins` and motion parity. The pre-tool-use hook exits with code 2 before
the focused `RunTests.ps1` invocation and before `RunTests.ps1 -Quick`, so neither the tests nor
the install-backed census have run. The build is unverified, so editor smoke must wait for a
successful build rather than testing an older binary (METHOD-16). Keep C22 in progress until
the focused tests, census, editor smoke and Quick complete.

**⚠ Traps.** Do not infer a fixed-width record from one mission. Motion bounds and orientation
must remain in their authored units and axis order.

## C23 ◐ Enemy-generator timing and references

**Goal.** Edit generator timing, capacity, templates, paths, and mission references while keeping
the host attached to an existing chapter-world node.

**Evidence (confidence: traced-to-code; story census pending).** `EnemyGenerators.Load`
selects the first mission reader and its first outer list, skips non-list records, and requires a
slot-0 `node` string and a `vehicle` key. The last case-insensitive alternating key wins,
including bare overrides. `vehicle.params` and `choose_nets` read slot 0; `vehicle.nets` reads
direct strings. Counts are float-to-int casts, periods are seconds, and the inter-wave gap is
their sum. `zeppelin` and `moving_path` are presence flags. `origin`, door names and `healthy`
are optional strings; `rotation` requires exactly three floats and `min_altitude` is a metre
gate with negative values disabled. `AiGeneratorRuntime` resolves the host and origin in the
chapter world, takes chapter nets and mission-local planned roster templates, and derives surface
path names under the host. `GeneratorCycle` starts with zero credit regardless of `capacity`.
`ObjectiveScript` selects first-occurrence `WAKEUP_GENERATOR` and `DEDG` references in contiguous
mission objectives. The new story-reader directive and width census has not run.

**Approach.** Add exact-index generator views and reference records for hosts, AI templates, nets,
and objectives. Present the origin as an offset from its resolved host, not as a free-standing
world object.

**Model recommendation.** high, because reader-selected duplicates, host-subtree coordinates,
mission template planning, chapter net scope, objective uses and atomic ranges must agree.

**Verify.** Invented-reader tests cover timing and ranges, first-reader selection, variable and
malformed records, exact paths, duplicate and bare overrides, preservation, atomic validation,
host-relative offsets, reference scopes and refresh, `EnemyGenerators` and `GeneratorCycle`
parity. The pre-tool-use hook exits with code 2 before the focused `RunTests.ps1 -UnitFilter`
invocation and before `RunTests.ps1 -Quick`, so the tests and install-backed story census have not
run. The build remains unverified; editor smoke must wait for a successful build (METHOD-16).
The eight-chapter freecam regression and a targeted generator capture also remain unrun. Keep C23
in progress until these checks complete; use the repository probe admission path (SHELL-10).
The hook also denies `CheckDocEntries.ps1`, `CheckUidSidecars.ps1`, `CheckCommentCaps.ps1` and
`CheckEncoding.ps1` with code 2. `git diff --check` passes for tracked changes only.

**⚠ Traps.** The host is a chapter-world node. A free-position editor would author a state the
runtime does not consume.

## C24 ◐ Danger-zone mission overrides

**Goal.** Edit mission danger-zone enablement, objective association, and snapshot controls while
keeping chapter-world gate geometry read-only.

**Evidence (confidence: traced-to-code).** `CampaignDangerZones.ReadOverrides` reads only the
first mission `dzones.zrd`; chapter readers are not a fallback. The last case-insensitive
alternating key, including a bare key, selects each direct-string `disable` and `nosnapshot` list
and each `objective_numbers` list. The last valid `[name, float, ...]` pair for a name supplies
the completion/scrapbook slot, not an `OBJECTIVEn` graph reference. The objective parser selects
first exact-case contiguous blocks and first directives, accepting names directly or one list
deep. Player gates use the first case-insensitive chapter node; AI ribbons use the last valid
numeric index. Existing format documentation reports local readers in 22 of 24 story missions;
the added install-backed shape and scope census has not run in this session (SRC-19).

**Approach.** Resolve gates through B11/B12, display source-world ribbons read-only before setup,
and bind only mission-local override fields at exact indexes. One row validates before one
document revision; absent readers require explicit mission-only creation.

**Model recommendation.** high, because the two geometry consumers, duplicate selection,
objective scripts, scrapbook slots and preservation rules must agree.

**Verify.** Invented-data tests cover absent and chapter-only readers, local overrides, missing
and wrong-scope gates, duplicate and bare-key selection, exact writes, atomic validation,
unknown/tail/float-bit preservation, disabled-to-enabled gate diagnostics, and `CampaignDangerZones` and
`DangerZoneRibbons` parity. The pre-tool-use hook exits with code 2 before the focused
`RunTests.ps1 -UnitFilter 'FullyQualifiedName~CampaignMissionDangerZone' -SkipEngine -SkipGoldens`
and `RunTests.ps1 -Quick` commands, so neither the tests nor the install-backed census have run.
The build is unverified; editor smoke must wait for a successful build (METHOD-16). The
eight-chapter freecam regression and a targeted danger-zone capture remain unrun; use the
repository probe admission path (SHELL-10), compare source identity (WORLD-8) and inspect actual
engine logs (LOG-17). The hook also denies `CheckDocEntries.ps1`, `CheckUidSidecars.ps1`,
`CheckCommentCaps.ps1` and `CheckEncoding.ps1` with code 2. `git diff --check` passes for tracked
changes only. A retry also needs workspace-local temporary output without isolating the shared
memory-admission ledger. Keep C24 in progress until its verification succeeds.

**⚠ Traps.** Gate geometry belongs to gamez. The editor must not imply that moving a ribbon writes
mission data.

## C25 ◐ Named mission locations

**Goal.** Determine whether the named `location.zrd` records support a runtime-backed editor.

**Evidence (confidence: traced-to-code).** No CSVM runtime selects this reader for named points.
`SpawnPoints.LoadPlayerInit` and `ObjectiveScript.Load` read `objectives.zrd`; travelers resolve
literal positions or live world/roster names, and named warps resolve scripted world paths.
`CampaignBriefing` takes its dialog from shared `Briefing.zrd` and its note from `objectives.zrd`;
`MissionMap.Icon` projects positions supplied by callers. These findings do not establish the
original executable's consumer. Existing format documentation reports 24 story readers and one
empty root, but the install- and extraction-backed structural censuses have not run in this session.

**Verdict.** Named-point editing and a point summary have no current runtime-backed meaning, so
no typed panel or waypoint semantics are added. The existing raw tree and full-scope export retain
the reader, and diagnostics mark the family informational and unverified. This does not make
arbitrary contents inert: `AnimDefs.LoadArchive` scans every nonempty reader, `MissionCutscenes`
can load a reader named by the animation index, and editor reference scans also inspect generic
animation keys. `AnimProgram` applies its compiled-manifest rules to mission definitions.

**Verify.** `CampaignMissionLocationTests` adds invented-data checks for the reader inputs,
positive generic animation and reference controls, diagnostic refresh, null versus missing roots,
and bit-exact no-edit full-scope export. Separate install- and extraction-backed censuses report
only structural types, order and widths. The pre-tool-use hook exits with code 2 before the focused
`RunTests.ps1 -UnitFilter 'FullyQualifiedName~CampaignMissionLocation' -SkipEngine -SkipGoldens`
and partial `RunTests.ps1 -Quick -SkipEngine` commands, so tests, build and census remain unrun.
The same hook denies `CheckDocEntries.ps1`, `CheckEncoding.ps1`, `CheckCommentCaps.ps1` and
`CheckUidSidecars.ps1`. The full Quick engine run also needs workspace-local temporary output
without isolating the shared memory-admission ledger. Editor smoke and the eight-chapter freecam
regression remain unrun until the build succeeds (METHOD-16, SHELL-10, LOG-17). There is no
point-specific change or capture target. Keep C25 in progress until verification succeeds.

**⚠ Traps.** Similar-looking records do not establish gameplay waypoints or their original-game use.

## C26 ◐ Objective graph and directive editing

**Goal.** Edit ordered objectives and supported directives while preserving contiguous numbering,
raw directive order, unknown directives, target paths, and objective identity.

**Evidence (confidence: traced-to-code).** `ObjectiveScript` selects the first exact-case
`OBJECTIVEn` from 1 through the first gap, including a bare block. Its alternating directive walk
also selects the first exact-case key, including a bare key, and the long wake and timer forms win
over their aliases. `ObjectiveGraph` scans by number and completes at most one objective per tick.
`IDENTITY` priority groups the runtime display and reward mask, not the graph. The separate
`BriefingObjectives` reader includes every keyed case-insensitive identity, including duplicates
and blocks after a gap, and sorts by priority and file position.

The runtime directive inventory has file timer, player start, animation and sound keys; dormant,
dependency and hide timing; `INACTIVEn` world paths; animation-state, danger-zone, DEDG and
traveler conditions; wake, sound, generator and animation actions; target, taxi, AI, help, warp
and timer completion actions; wake, kill, sleep and nap objective links; identity and ending flags.
`INACTIVEn` reads one path. Each direct target string is a separate target and each nested list
is one path; help uses its first target and last message. The format reference also documents
original-only or unauthored keywords such as `COUNTER`. Its existing 53-reader census is not a
new run here. `CampaignObjectiveCensusTests` can report story directive shapes and duplicate/gap
counts through user-owned install or extraction APIs without copying their payloads.

**Approach and model.** `CampaignMissionObjectives` binds selected raw indexes and never rebuilds
an objective from typed nodes. The dock lists numbered blocks and ordered directives, summarizes
links, retains a visible raw fallback, and edits only existing identity, dormant-timing, known
objective-link, inactive-path, target-path and help-message leaves. Each row validates before one
document batch. Reference diagnostics follow selected objective and briefing consumers, preserving
target boundaries and showing objective, world, target and message verdicts.

An explicit number swap first validates the complete contiguous block set, known file keys,
directive families and objective links, including duplicate and shadowed occurrences. It then
changes all affected key and link leaves in one revision. Unknown directives, gaps, noncanonical
blocks, malformed identities, objective-bearing nested data, and ambiguous or unresolved links
prevent the swap. Unknown data, tails, duplicate order, nulls and untouched float bits remain raw.
The swap cannot promise the same rotating completion order, priority winner, or behavior of external
consumers. Raw edits cannot change the selected reader's numbered block keys one leaf at a time.

**Verify.** Invented-data tests define numbering, runtime and briefing identity, duplicate and bare
selection, unknown and malformed records, target paths and references, validation-before-mutation,
atomic batches and swaps, no-edit complete-scope export, and `ObjectiveScript`/`ObjectiveGraph`
parity. The pre-tool-use hook exits with code 2 before the focused runner, the non-engine Quick
runner, a no-restore solution build, and the documentation, encoding, comment, UID and item-ID
checks, so none ran and the new census has no measured result.
The attempted test commands redirect their temporary files into the workspace because the default
temporary location is prohibited for this session. A complete Quick engine run cannot use that
redirection without isolating the shared Godot memory ledger, and is not run. Editor smoke and the
eight-chapter freecam regression remain unrun until a build succeeds (METHOD-16, SHELL-10, LOG-17).
No location-specific capture target is identified for this authoring slice. Keep C26 in progress
until the focused tests, census, editor smoke, Quick and required regression gates complete.

**⚠ Traps.** Reconstructing objectives from typed nodes can discard directives or change their
order. Never renumber incrementally if a later validation can fail.

## C27 ◐ Remaining roster fields and AI-net references

**Goal.** Extend roster editing beyond the existing spawn prefix only where `AiSkills` and the
chapter net data provide a settled interpretation.

**Evidence (confidence: traced-to-code; story census pending).** The existing format reference
records 414 blocks in 53 rosters with widths `{42:20, 65:1, 66:33, 67:14, 68:39, 81:307}`; the new
story-only shape and duplicate census has not run. `ZrdrZbd.Find` selects the first matching reader,
and `AiSkills.LoadRoster` keeps duplicate block names in raw order. `CampaignDirector` subsequently
indexes spawned rigs by name. `LoadGeneratorRoster` pairs each disabled block with its positional
header label, and `CampaignRosterPlan.GeneratorTemplates` keeps the last successfully planned
duplicate label when planning completes. A planning exception leaves the reference unverified.
Names, enabled flags, header IDs and labels are not typed-editable.

The runtime-consumer inventory follows `AiSkills`, `AiVolumeSet`, `CampaignRosterPlan`,
`CampaignDirector`, `OppositionStage`, and `AiGeneratorRuntime`:

- Slot 0 is a scalar or list of chapter net IDs. Nonnegative numeric choices are converted to
  integers, preserving order and duplicates. Initial spawns draw a choice, and a netless wingman
  escorts while another netless enabled block is skipped. Templates use the first eligible choice
  while planning range defaults; generators choose their own patrol net. Slots 1–5 are the
  existing position, yaw, team, group and enabled prefix.
- Slot 6 supplies an escort leader or primary target. Slot 7 overrides health only above zero.
  Slots 8–19 feed roster volumes over net volumes, but only their radii reach CSVM's range gates
  and the original field alignment remains uncertain. Slot 20 supplies a display message, slot
  21 activation, slots 22–30 pilot ratings, slot 32 the maneuver mask, and slot 34 nitro.
- Slot 31's engagement altitude is stored in the spawn plan without a found downstream CSVM
  consumer. Slot 33 feeds target ranking but may contain an unknown third list element. Slots
  35–36 and 41–64 have no established CSVM roster-tail consumer.
- Slots 37–39 mark an objective actor and carry its optional message keys. Slot 39 can instead be
  designer text when the flag is off. Slot 40 names a scripted world path; slot 65 selects voice,
  slot 66 overrides armor at zero or above, and slot 67 marks an ace. Slots 68–80 have no
  established CSVM roster-tail consumer; the runtime obtains livery from definitions.

**Approach.** The roster adapter exposes only canonical existing leaves in independent net,
durability, activation/nitro, ratings/ace, maneuver-mask, target/title, marker, scripted-path and
voice groups. It retains each raw index, validates a group before one revision and leaves absent,
malformed and unsupported values untouched. No list, name, template header or enabled flag is
created or reordered. The `player` block is read-only and `PLAYER_INIT` remains the human placement
authority. B12 diagnostics resolve IDs against nonempty chapter graphs and template labels against
the selected mission roster, distinguishing new misses, inherited misses and unavailable inputs.
Marker strings are editable only while the marker is enabled.

**Model recommendation.** high, because field meaning, generator selection, scope ownership and
atomic raw-tree preservation must agree across the adapter, runtime and diagnostics.

**Verify.** Invented-data tests define parity and exact-leaf preservation for each group, all six
widths, selected-reader and duplicate-label rules, absent or malformed values, net-list order,
wrong-scope and unavailable references, diagnostics refresh, atomic failures, full-scope float-bit
preservation and unchanged player placement. The pre-tool-use hook exits with code 2 before the
focused runner, `RunTests.ps1 -Quick -SkipEngine`, a no-restore test-project build, and the documentation,
encoding, comment, UID and item-ID checks. None ran, and the optional story census has no measured
result. Editor diagnostics report no problems in the changed C# files; `git diff --check` passes
for tracked changes only. A complete Quick engine run cannot redirect temporary output into this
workspace without isolating the machine-wide Godot memory ledger, so it remains unrun. Editor
smoke and the eight-chapter freecam regression remain unrun until a build succeeds and the shared
ledger can be used without prohibited temporary-file operations (METHOD-16, SHELL-10, LOG-17).
No location-specific capture target is identified for this authoring slice. Keep C27 in progress
until these gates and the census complete.

**⚠ Traps.** A shared tail shape is not proof of shared semantics. `PLAYER_INIT`, not the roster's
human block, remains the placement authority.

## C28 ◐ Start-animation editing and read-only map/net summaries

**Goal.** Edit the ordered mission start-animation names and provide useful read-only summaries of
`map.zrd`, `net.zrd`, and animation readers without promising unsupported effects.

**Evidence (confidence: traced-to-code).** `AnimProgram.LoadStartAnims` selects the first matching
reader, scans every case-insensitive `NEW_GAME_START` group in its first root list, and appends the
first string of each nested entry in order. Duplicates are retained; malformed entries and tails are
not dispatched. `AnimRuntime.RunAmbientPasses` dispatches each occurrence after `ON_STARTUP`, can
reset repeated definitions, and logs and skips names absent from the case-insensitive lookup.
Chapter compiled definitions precede mission compiled definitions; identity is the case-insensitive
`(NAME, ANIMATION_NAME)` pair. A present mission compiled archive gates mission reader definitions
and shared/chapter reader files, while its absence allows reader-only behavior.

`map.zrd` has no established CSVM map-specific consumer. `SpawnPoints.LoadNetTable` reads valid
four-float multiplayer spawn prefixes from `net.zrd`; it is neither the campaign player start nor
the chapter AI-net graph. An empty root is rejected by that reader. `mis_anim.zrd` is a reader-path
index, not its compiled namesake.

**Approach.** The revision-bound startup adapter renames exact first-string leaves and moves whole
entry lists among selected slots in one document revision, retaining unknown slots, tails and
duplicates. Reference diagnostics use the same compiled-first program and gates without mutating
cached compiled definitions. Inherited missing verdicts follow individual entries through moves;
new missing duplicates do not inherit them. Missing dependencies remain unverified. The dock
provides startup controls, reference verdicts and read-only structural map, multiplayer-net and
animation summaries; compiled archives are never exported or edited.

**Model recommendation.** High, because this slice joins runtime ordering, duplicate ownership,
scope gates, shared cached archives and lossless raw mutation.

**Verify.** Invented-data tests cover exact selection, reorders, duplicate ownership and renames,
invalid and stale operations, malformed or absent readers, scoped and unavailable diagnostics,
summary values, no-edit and edited complete-scope export, and `AnimProgram` lookup parity. The
pre-tool-use hook exits with code 2 before the no-restore build, focused runner, non-engine Quick
runner, and content checks; no tests or build ran. Editor diagnostics report no errors in the
changed C# files. `git diff --check` and no-index checks on the touched untracked C# files report no
whitespace errors. Engine-inclusive Quick, the editor smoke, eight-chapter freecam regression and a
targeted editor capture remain unrun. The engine's shared memory ledger uses system temporary
storage, which cannot be redirected without isolating the ledger and is prohibited in this session
(METHOD-16, SHELL-10, LOG-17). Keep C28 in progress until the focused tests, editor smoke, Quick and
required regression gates complete.

**⚠ Traps.** A reader name does not prove the corresponding compiled animation exists. Do not
rewrite compiled archives or claim an unconsumed map/net field affects campaign flight.

# Wave D, Finish the Godot authoring workflow

## D31 ◐ Mission preview handles and overlays

**Goal.** Show player and roster starts, targets, generators, zeppelins, danger zones, and named
locations against the selected chapter world using derived Godot nodes that never become mission
source data.

**Evidence (confidence: traced-to-code).** `SpawnPoints` and `CampaignDirector` establish XYZ
metres, yaw-only player placement and world-node precedence for roster starts. `ZeppelinRuntime`
places authored pitch/yaw poses; `EnemyGenerators` and `AiGeneratorRuntime` resolve origins under
the host and lift surface launches in world-up. `ObjectiveSites` prefers literal traveler points
then world-site bounds. `DangerZoneRibbons` chooses the winning numeric route before setup. C25
establishes no CSVM named-location point consumer. The B11 bundle supplies the chapter world,
textures and mission setup without granting a world writer (WORLD-8, WORLD-15).

**Approach.** The opt-in collision-free viewport applies available setup to a disposable chapter
world. Derived handles and ribbons retain exact scoped sources; reader snapshots coalesce changes
and replace only changed layers. Selection and framing inspect sources rather than dragging them.
Animations, staging and live movement remain explicit limits.

**Model recommendation.** high, because setup, source provenance and host-local transforms cross
the editor, world builder and several runtime consumers.

**Verify.** Invented pure tests and an in-engine suite cover transforms, scoped references,
malformed geometry, selective layer replacement and disposal. Execution is pending: the normal
focused `RunTests.ps1`, `RunTests.ps1 -Quick`, `dotnet build CSVM\CSVM.sln --no-restore` and
the content-check scripts are denied before execution by the pre-tool-use hook (exit 2). The
build, focused tests, Godot editor and mission smoke, Quick and eight-chapter world regression
therefore remain unverified. `git diff --check` passes for tracked changes. Do not smoke an
unverified binary; the engine's shared memory ledger also uses prohibited system temporary
storage and must not be isolated (METHOD-9/10, METHOD-16, SHELL-10, LOG-17). Keep D31 in
progress until all gates complete.

**⚠ Traps.** Preview transforms are derived UI. Saving `.tscn`, `.tres`, or duplicated retail
geometry as mission data violates the document boundary.

## D32 ◐ Undo and redo for typed edits

**Goal.** Make each typed operation undoable and redoable through Godot's editor history while
restoring exact raw values, dirty state, diagnostics, and preview state.

**Evidence (confidence: traced-to-code).** `CSVM.csproj` and the local Godot API metadata identify
Godot .NET 4.7. `EditorUndoRedoManager.CreateAction` takes a name, `UndoRedo.MergeMode`, context,
`backwardUndoOps` and `markUnsaved`; `AddDoMethod` and `AddUndoMethod` take a `GodotObject`,
`StringName` and `Variant[]`, and `CommitAction(false)` registers without executing the do method.
The 4.7 manager routes a non-node, non-resource context to global history and binds the supplied
arguments to its callbacks. Its C# script-path generator recognizes a class whose name matches
its file, so the callback bridge has its own script file. The synchronous `history_changed`
signal confirms an outermost publication; a nested assigned action emits no signal.
`EditorUndoRedoManager` exposes no handle for an uncommitted action, so the bridge follows the
editor API's synchronous create/add/commit transaction instead of queueing work across callbacks.

**Approach.** Immutable operations retain exact before/after raw snapshots and history state for
typed edits, structural changes and raw scalars. The document validates and applies them
atomically, tracks the exported clean state, and uses a separate generation for stale bindings.
The bridge records each action synchronously in shared editor history, invalidates old document
sessions on replacement or unload, and refreshes diagnostics and typed views while previews
observe changes. `CommitAction(false)` records the operation without applying it twice. A nested
or failed publication rolls back the unchanged edit and leaves any appended callbacks inert,
while a divergent document is reported without being overwritten.

**Model recommendation.** high, because raw-bit preservation, atomic history, editor callbacks,
projection refresh and document lifetime cross the pure model and Godot's shared history.

**Verify.** Focused pure tests cover single and grouped edits, raw float bits, structural snapshots,
registration failure, stale and divergent sessions, saved-state branching, and callbacks.
Interleaving regressions exercise nested registration, multiple documents, exact bits and rollback.
An in-engine bridge suite covers Godot method dispatch, history traversal, nested registration,
replacement and unload; it does not replace a live editor-manager smoke.
These tests are written but not executed. Target
`CampaignMissionEditTests`, `CampaignMissionPreviewTests`, `CampaignReferenceDiagnosticsTests`,
`CampaignMissionStartAnimationsTests` and `campaign-mission-editor-undo`, then run Quick.
The pre-tool-use hook denies `dotnet --version` and the encoding, UID, comment, architecture and
item-ID checks before execution (exit 2). The build, focused tests and Quick remain unverified;
D31 records the same denial for the build and `RunTests.ps1 -Quick`. Engine runs additionally
require the shared memory ledger in prohibited system temporary storage, which must not be
isolated. The editor smoke and eight-chapter regression remain blocked until a verified build and
permitted shared ledger are available. `git diff --check` passes for tracked changes, which does not
establish a working build (METHOD-9/10, METHOD-16, SHELL-10).

**⚠ Traps.** Recomputing displayed floats on undo can lose authored bits. Restore captured raw
values, not a newly converted typed state. Do not defer editor-history registration: it can reverse
shared-history order and can run while another plugin owns an uncommitted action. A nested
publication is rejected and rolled back instead of being merged into that action.

## D33 ◐ End-to-end campaign authoring validation

**Goal.** Demonstrate that a user-owned mission can be opened, edited across supported panels,
exported as a complete scope, reopened, and loaded by a real campaign flight with actionable
diagnostics and no retail-derived repository output.

**Evidence (confidence: traced-to-code, execution pending).** The new invented-scope harness uses
the editor's import, typed adapters, reference diagnostics, preview projections, edit history and
complete ZIP export, then reopens the ZIP through `CampaignMissionImport`, `ZrdrSet` and the
`GameData` mod-selection path. It specifies independent before and after trees and checks the
runtime readers, but it has not been compiled or run. A real user-owned mission, editor viewport
and campaign flight still require separate verification.

**Approach.** `CampaignMissionAuthoringEndToEndTests` defines this legal edit matrix for invented
complete scopes with both `ZONE1`/`ZONE2` and C5-style `ZONE1`/`ZONE3` layouts:

| Surface | One controlled edit or read-only check |
|---|---|
| Player and roster | Last player-start X `1` to `101`; roster-start X `5` to `55`; roster health `0` to `180`. |
| Targets | Repair the description message; turn off `objective` and turn on `other_target`. |
| Zeppelin and generator | Zeppelin position X `10` to `110`; generator wave period `8` to `4`. |
| Danger and objectives | Enable `dzpath1`, allow its snapshot and change its slot `18` to `29`; objective identity priority `1` to `2`. |
| Start animations | Reorder `alpha`, `beta` to `beta`, `alpha`. |
| Weather | Cloud top `1200` to `1400`; static wind X `1` to `-4`; rain to snow; second-zone fog near `100` to `120` and sun diffuse `0.9` to `1.1`. |
| Raw and read-only | Change one raw leaf; retain the location reader as unverified, and inspect map, net, animation and weather summaries. |

The harness also checks duplicate readers and keys, order, unknown fields, tails, nested nulls,
negative zero and subnormal bits; an unchanged apply is a no-op. It introduces a missing message,
checks the blocking diagnostic, undoes, redoes and repairs it, and checks an inherited missing
message and unavailable animation separately. It checks preview updates and selection, then undoes
and redoes all 15 successful edits. The opt-in external theory writes only invented content in a
uniquely owned folder, reopens and loads the complete scope, and deletes its own outputs.

**Model recommendation.** High, the item crosses lossless serialization, runtime consumers and
Godot integration.

**Verify.** Keep D33 and Wave D open until all of these are observed:

- Run the end-to-end theory for both layouts, including the external cases with an existing
  non-temporary `CSVM_AUTHORING_OUTPUT_ROOT` outside the checkout. Run the focused Wave C and D
  unit surfaces and their applicable install-backed censuses.
- Run format, StyleCop rebuild, repository content checks, the preview and undo engine suites,
  editor smoke, and `.\scripts\RunTests.ps1 -Quick`. Use the shared memory ledger and inspect its
  admission results.
- Run the complete `.\scripts\RunTests.ps1` and the eight-chapter freecam regression. The current
  full runner writes golden screenshots below the checkout, so its output location must be made
  compliant with the no-screenshots-in-repository rule before this gate can run; skipping goldens
  is not the full gate.
- Export a user-owned mission to an external mod folder and launch its matching campaign sequence
  with an isolated seeded profile. Confirm the mod-selection log, the live mission and its edited
  behavior; retain only asset-free logs or metadata, and keep screenshots outside the repository.

The pre-tool-use hook has denied the attempted test, build, content-check and probe commands with
exit code 2 before execution. No compile, focused suite, engine, editor smoke, Quick, full, world
regression or real campaign-flight result is available. The previously attempted campaign command
did not name an existing mod scope and is not a real-load attempt. `--frames` needs `--screenshot` to
exit, and every scripted campaign launch needs its own `--profiles` store (SHELL-12, LOG-20).
Do not treat skipped external cases, parser checks or a denied command as a passing gate.

**⚠ Traps.** Parser success alone is not a campaign-load proof. Keep generated full scopes in the
external modding folder and never attach or commit them as test fixtures.
