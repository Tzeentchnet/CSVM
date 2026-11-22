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

1. ☐ Typed precipitation editing
2. ☐ Per-zone light and clip editing
3. ☐ Weather diagnostics and preview

### Wave B, Load dependencies and existing projects

11. ☐ Read-only mission dependency bundle
12. ☐ Continuous cross-reader reference diagnostics
13. ☐ Existing complete mod-scope import

### Wave C, Author remaining mission readers

21. ☐ Target flags and labels
22. ☐ Zeppelin placement and behavior
23. ☐ Enemy-generator timing and references
24. ☐ Danger-zone mission overrides
25. ☐ Named mission locations
26. ☐ Objective graph and directive editing
27. ☐ Remaining roster fields and AI-net references
28. ☐ Start-animation editing and read-only map/net summaries

### Wave D, Finish the Godot authoring workflow

31. ☐ Mission preview handles and overlays
32. ☐ Undo and redo for typed edits
33. ☐ End-to-end campaign authoring validation

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

## A1 ☐ Typed precipitation editing

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

**Model recommendation.** <TODO: choose a capability tier after confirming the full shipped-field
coverage and optional-key shapes.>

**Verify.** Add invented-reader tests for exact paths, absent and malformed fields, duplicate and
tail preservation, validation-before-mutation, and `WeatherState.Precip` parity. Run the focused
tests, StyleCop rebuild, ordinary solution build, content checks, Godot editor smoke through
`RunProbe.ps1`, and `RunTests.ps1 -Quick`.

**⚠ Traps.** The precipitation fields are top-level bare siblings, not one list-valued block.
Do not route them through `ZrdrDict` or create a missing optional `PARTICLES` value.

## A2 ☐ Per-zone light and clip editing

**Goal.** Edit the runtime-consumed clip range, sunlight scalars, colors, flags, and sun
orientation for each discovered weather zone without changing zone order or software-renderer
twins.

**Evidence (confidence: lead-only).** `docs/formats/weather.md` and `WeatherState.Load` identify
the runtime fields, while the existing fog adapter proves dynamic `ZONE<digit>` discovery and
exact-index writes. <TODO: census which lighting keys and vector shapes are optional in every
campaign mission before freezing the typed boundary.>

**Approach.** Extend the per-zone weather model or add a sibling lighting adapter after the
census. Reuse shared color encoding, keep `SW_ZONE*` blocks raw, and expose only fields with a
settled runtime interpretation.

**Model recommendation.** <TODO: choose a capability tier after the optional-field census.>

**Verify.** Add invented-reader tests for C1-C4 `ZONE1`/`ZONE2`, C5 `ZONE1`/`ZONE3`, independent
optional keys, exact paths, color encoding, validation-before-mutation, and `WeatherState.Zone`
parity. Run the standard targeted, editor-smoke, and Quick gates.

**⚠ Traps.** Sun bearing is a shading direction, not the sun billboard position. Keep authored
zone order and the `SW_*` twins distinct from runtime zone selection.

## A3 ☐ Weather diagnostics and preview

**Goal.** Show the derived cloud band, whiteout ramps and colors, selected zone, fog ranges,
precipitation, wind, and lighting values without claiming to render chapter geometry the dock has
not loaded.

**Evidence (confidence: lead-only).** The typed weather adapters and `WeatherState` expose the
derived values, while `docs/formats/weather/atmosphere.md` records the midpoint regime transition
and whiteout interpolation. The current dock has diagnostics but no weather preview.

**Approach.** Build a read-only weather summary and simple altitude/range visualization from the
typed state. Keep world-deck and horizon geometry out until B11 and D31 provide the actual chapter
dependencies.

**Model recommendation.** <TODO: choose a capability tier after selecting the preview UI shape.>

**Verify.** Unit-test the derived labels and plotted thresholds from invented states, smoke the
dock headlessly, and compare the preview values with `WeatherState` for representative weather
fixtures. Run `RunTests.ps1 -Quick`.

**⚠ Traps.** A numeric band preview cannot stand in for the chapter's deck or horizon meshes.
State the geometry boundary in the UI instead of drawing an invented cloud deck.

# Wave B, Load dependencies and existing projects

## B11 ☐ Read-only mission dependency bundle

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

## B12 ☐ Continuous cross-reader reference diagnostics

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

## B13 ☐ Existing complete mod-scope import

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

## C21 ☐ Target flags and labels

**Goal.** Edit mission target starting flags and labels with resolved message and world-node
references.

**Evidence (confidence: lead-only).** The target surface and mission-local-copy boundary are
recorded in `docs/formats/missions/campaign-authoring.md`. <TODO: re-read `MissionTargets` and
census mission versus inherited chapter tables before defining writable records.>

**Approach.** Add exact-index target views over the mission-local reader. When a mission inherits
the chapter table, require an explicit copy action after showing the wider-scope effect.

**Model recommendation.** <TODO: choose a capability tier after the inheritance census.>

**Verify.** Test exact paths, duplicate names, inherited-table refusal, explicit-copy output, and
`MissionTargets` parity. Run cross-reference diagnostics, editor smoke, and Quick.

**⚠ Traps.** Editing an inherited chapter table changes more than one mission. Never make that
scope widening implicit.

## C22 ☐ Zeppelin placement and behavior

**Goal.** Edit each mission zeppelin's placement, orientation, motion limits, team, target, and
damage records without rebuilding unknown fields.

**Evidence (confidence: lead-only).** `docs/formats/missions/campaign-authoring.md` identifies the
zeppelin authoring surface and the existing `Zeppelins` consumer. <TODO: census variable-width
records and optional fields before fixing the typed prefix.>

**Approach.** Follow the roster adapter pattern: bind exact indexes for a verified stable prefix,
leave variable tails raw, and resolve target and world references through B12.

**Model recommendation.** <TODO: choose a capability tier after the record census.>

**Verify.** Add invented-reader tests for malformed and variable-width records, exact writes,
unknown-tail preservation, validation-before-mutation, and `Zeppelins` parity.

**⚠ Traps.** Do not infer a fixed-width record from one mission. Motion bounds and orientation
must remain in their authored units and axis order.

## C23 ☐ Enemy-generator timing and references

**Goal.** Edit generator timing, capacity, templates, paths, and mission references while keeping
the host attached to an existing chapter-world node.

**Evidence (confidence: lead-only).** The generator boundary is recorded in
`docs/formats/missions/campaign-authoring.md` and the runtime consumer is `EnemyGenerators`.
<TODO: re-read the generator format and enumerate optional directives before implementation.>

**Approach.** Add exact-index generator views and reference records for hosts, AI templates, nets,
and objectives. Present the origin as an offset from its resolved host, not as a free-standing
world object.

**Model recommendation.** <TODO: choose a capability tier after the directive inventory.>

**Verify.** Test timing and range validation, exact paths, unknown directives, unresolved hosts,
and `EnemyGenerators` parity. Exercise the diagnostics and Quick gates.

**⚠ Traps.** The host is a chapter-world node. A free-position editor would author a state the
runtime does not consume.

## C24 ☐ Danger-zone mission overrides

**Goal.** Edit mission danger-zone enablement, objective association, and snapshot controls while
keeping chapter-world gate geometry read-only.

**Evidence (confidence: lead-only).** The writable override versus read-only geometry split is
recorded in `docs/formats/missions/campaign-authoring.md`; `CampaignDangerZones` is the runtime
consumer. <TODO: census the override shapes and inherited cases.>

**Approach.** Resolve gates through B11/B12, display their ribbons read-only, and bind only
mission-local override fields at exact indexes.

**Model recommendation.** <TODO: choose a capability tier after the override census.>

**Verify.** Test inherited and mission-local cases, missing gates, exact writes, unknown-field
preservation, and `CampaignDangerZones` parity.

**⚠ Traps.** Gate geometry belongs to gamez. The editor must not imply that moving a ribbon writes
mission data.

## C25 ☐ Named mission locations

**Goal.** Preserve and edit existing named `location.zrd` points while clearly identifying their
limited current runtime use.

**Evidence (confidence: lead-only).** The campaign authoring reference records these as cockpit-map
or developer-bookmark points and notes that the current flight UI has no authoring consumer.
Earlier editor work deliberately deferred them in favor of live weather data.

**Approach.** Census the shipped records, confirm whether any current consumer remains, then add
exact-index point views only if the data has a stable meaning. A read-only summary is an acceptable
outcome if editing cannot be justified.

**Model recommendation.** <TODO: choose a capability tier after consumer tracing.>

**Verify.** Prove the consumer or the absence of one, test exact preservation, and add typed edits
only with a runtime-backed interpretation. A disproof closes the item successfully.

**⚠ Traps.** Do not promote stale developer bookmarks into gameplay semantics merely because they
look like useful waypoints.

## C26 ☐ Objective graph and directive editing

**Goal.** Edit ordered objectives and supported directives while preserving contiguous numbering,
raw directive order, unknown directives, target paths, and objective identity.

**Evidence (confidence: lead-only).** `docs/formats/missions/campaign-authoring.md` and
`docs/formats/objectives.md` define the current safety boundary; `ObjectiveScript` is the runtime
consumer. <TODO: inventory every directive shape and identity rule before selecting the first
writable subset.>

**Approach.** Build an exact-index objective model with a raw fallback for every unsupported
directive. Add a graph/list view only after the model can round-trip unknown directives and
renumbering can be validated as one operation.

**Model recommendation.** <TODO: choose a capability tier after the directive inventory; this item
has the roadmap's largest local data-model risk.>

**Verify.** Test numbering, identity, duplicate and unknown directives, target references,
validation-before-mutation, no-edit round-trip, and `ObjectiveScript` parity. Run the full editor
and Quick gates.

**⚠ Traps.** Reconstructing objectives from typed nodes can discard directives or change their
order. Never renumber incrementally if a later validation can fail.

## C27 ☐ Remaining roster fields and AI-net references

**Goal.** Extend roster editing beyond the existing spawn prefix only where `AiSkills` and the
chapter net data provide a settled interpretation.

**Evidence (confidence: lead-only).** The current adapter intentionally preserves names, net IDs,
enabled/template flags, and variable-width tails as raw data. <TODO: map each remaining field to
its runtime consumer and chapter-net dependency.>

**Approach.** Expand the adapter in small runtime-backed groups, keep the human `player` placement
boundary, and resolve nets and templates through B12.

**Model recommendation.** <TODO: choose a capability tier after the remaining-field map.>

**Verify.** Add parity tests for each newly exposed group, mixed-width preservation, unresolved-net
diagnostics, and unchanged player placement. Run editor smoke and Quick after each group.

**⚠ Traps.** A shared tail shape is not proof of shared semantics. `PLAYER_INIT`, not the roster's
human block, remains the placement authority.

## C28 ☐ Start-animation editing and read-only map/net summaries

**Goal.** Edit the ordered mission start-animation names and provide useful read-only summaries of
`map.zrd`, `net.zrd`, and animation readers without promising unsupported effects.

**Evidence (confidence: lead-only).** The surface and preservation boundary are recorded in
`docs/formats/missions/campaign-authoring.md`; `AnimProgram` establishes compiled-first animation
precedence. <TODO: confirm duplicate-name and missing-animation behavior before allowing reorder.>

**Approach.** Add an exact-index ordered-name adapter for `startanims.zrd`, resolve names through
B11/B12, and use raw or read-only typed summaries for the other readers.

**Model recommendation.** <TODO: choose a capability tier after tracing start-animation failure
behavior.>

**Verify.** Test reorder and duplicate preservation, missing-name diagnostics, exact export, and
`AnimProgram` lookup parity. Smoke the summaries and run Quick.

**⚠ Traps.** A reader name does not prove the corresponding compiled animation exists. Do not
rewrite compiled archives or claim an unconsumed map/net field affects campaign flight.

# Wave D, Finish the Godot authoring workflow

## D31 ☐ Mission preview handles and overlays

**Goal.** Show player and roster starts, targets, generators, zeppelins, danger zones, and named
locations against the selected chapter world using derived Godot nodes that never become mission
source data.

**Evidence (confidence: lead-only).** The document-model boundary in
`docs/formats/missions/campaign-authoring.md` defines preview nodes as disposable views rebuilt from
typed values. B11 supplies the required read-only world dependencies.

**Approach.** Build preview nodes from typed adapter states, retain exact source paths in the
selection model, and rebuild only affected overlays after edits.

**Model recommendation.** <TODO: choose a capability tier after selecting the minimum preview
scene and interaction model.>

**Verify.** Use invented transforms plus targeted user-owned mission smoke tests to compare handle
positions with runtime consumers. Run the full 8-chapter regression because this item loads world
geometry in the editor.

**⚠ Traps.** Preview transforms are derived UI. Saving `.tscn`, `.tres`, or duplicated retail
geometry as mission data violates the document boundary.

## D32 ☐ Undo and redo for typed edits

**Goal.** Make each typed operation undoable and redoable through Godot's editor history while
restoring exact raw values, dirty state, diagnostics, and preview state.

**Evidence (confidence: lead-only).** The campaign authoring reference states that undo should
apply one typed edit to raw locations and rebuild affected views; raw scalar edits currently have
no editor undo integration.

**Approach.** Represent each operation as immutable before/after raw leaf sets. Register it with
Godot's editor undo manager and route direct raw scalar edits through the same operation layer.

**Model recommendation.** <TODO: choose a capability tier after confirming the Godot 4.7 editor
undo API and plugin lifecycle.>

**Verify.** Test multi-leaf atomic edits, repeated undo/redo, failed validation, dirty-state
restoration, mission switching, and plugin unload. Smoke the live dock and run Quick.

**⚠ Traps.** Recomputing displayed floats on undo can lose authored bits. Restore captured raw
values, not a newly converted typed state.

## D33 ☐ End-to-end campaign authoring validation

**Goal.** Demonstrate that a user-owned mission can be opened, edited across supported panels,
exported as a complete scope, reopened, and loaded by a real campaign flight with actionable
diagnostics and no retail-derived repository output.

**Evidence (confidence: lead-only).** Unit tests, Quick gates, and editor smokes cover the current
slices independently. The authoring reference still requires a real campaign load for the complete
workflow.

**Approach.** Define a minimal legal edit matrix covering each adapter, run it against selected
user-owned missions including C5's zone layout, reopen the export through CSVM, and capture only
logs and metadata that contain no game assets.

**Model recommendation.** <TODO: choose a capability tier after defining the manual playtest
matrix and available retail-data environment.>

**Verify.** Run every targeted suite, `RunTests.ps1 -Quick`, the complete
`.\scripts\RunTests.ps1`, the Godot editor smoke, and the documented real campaign load. Confirm
the repository contains no generated mission archive or extracted data.

**⚠ Traps.** Parser success alone is not a campaign-load proof. Keep generated full scopes in the
external modding folder and never attach or commit them as test fixtures.
