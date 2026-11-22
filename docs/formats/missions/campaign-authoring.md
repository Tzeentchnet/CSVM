Part of: [Mission objectives and stunt zones](../missions.md).

# Campaign mission bundles and Godot authoring

A campaign mission is not one file. It is assembled from shared, chapter, and mission reader
scopes, a chapter world, two compiled animation archives, and one per-mission engine setup script.
This page maps that bundle and defines a safe authoring boundary for a Godot editor. Detailed field
references remain on the linked family pages.

The format facts cover the 24 story missions in the English retail data. The Godot project
includes the lossless raw document, scalar editing, complete-scope export, and a typed
`PLAYER_INIT` panel, typed `aiv.zrd` roster starts and supported AI fields, target flags and labels, zeppelin and generator
controls, objective directives and links, mission danger-zone overrides, ordered startup-animation
names, and typed `weather.zrd` cloud-band, wind, and precipitation controls, plus per-zone fog
and lighting controls. Read-only summaries cover weather, map and net reader shapes, multiplayer
spawns, and the effective animation program. The selected mission also has a read-only dependency
bundle with exact-path availability states, continuous cross-reader diagnostics, and strict
existing-scope ZIP or directory import. An opt-in read-only chapter-world viewport shows derived
mission handles and ribbons.

## Contents

- [At a glance](#at-a-glance)
- [Conceptual model](#conceptual-model)
- [The campaign mission reader census](#the-campaign-mission-reader-census)
- [Bundle reference](#bundle-reference)
- [Cross-file joins](#cross-file-joins)
- [Reader rules and edge cases](#reader-rules-and-edge-cases)
- [Godot editor workflow](#godot-editor-workflow)
- [Validation](#validation)
- [Evidence & limits](#evidence--limits)

## At a glance

| Question | Answer |
|---|---|
| What identifies a mission? | A zero-based campaign `seq` resolves through shared `cm_sequence.zrd` to a chapter and mission address such as `C3/M01`. |
| What is the editable unit? | The complete mission reader scope, `ZBD/<chapter>/<mission>/zrdr.zbd`. It contains 11 to 22 readers in the shipped campaign. |
| What does a preview also need? | The chapter `gamez.zbd`, textures, chapter readers, `cam_anim.zbd`, mission `mis_anim.zbd`, shared readers, messages, and the mission's `support\<chapter>\<mission>.gw` setup script. |
| Where should an editor save? | A local external modding folder, normally `<mod>\<chapter>\<mission>\zrdr.zip`, with every retained reader stored as `*.zrd.json`. The generated full scope is not a distributable artifact. |
| What should remain read-only first? | The chapter world, shared and chapter reader scopes, setup scripts, and compiled animation archives. They have wider effects or no current writer. |
| Can the retail archive be overwritten? | No. CSVM reads extracted JSON from a modding folder; it has no `zrdr.zbd` writer, and the retail game does not read this override format. |
| What can the editor change now? | Existing string and float leaves; typed player and roster starts, supported roster AI fields, target flags and message keys, zeppelin and generator fields, bounded objective directives and number swaps, mission-local danger-zone overrides, existing startup-animation names and their whole-entry order, and bounded weather fields. The target panel can add or remove safe flag pairs and explicitly copy an inherited chapter table into the mission. Other structure, nulls, wider scopes, and compiled data remain read-only. |
| Does the project include a Godot editor plugin? | Yes. The **Campaign Missions** dock loads a user-owned source and mission, materializes read-only dependencies, exposes the raw tree, diagnostics, typed controls and read-only weather, map, net and animation summaries, then exports the complete retail or imported scope. |

## Conceptual model

The campaign sequence supplies a storage address, not a self-contained mission:

```text
shared zrdr.zbd
  cm_sequence.zrd -> seq -> C3/M01
  vehicle, sound, message-key, briefing, and shared animation definitions

chapter C3
  gamez.zbd + textures              static world and node names
  zrdr.zbd                          AI nets and chapter definitions
  cam_anim.zbd                      compiled chapter animations

mission C3/M01
  zrdr.zbd                          objectives, roster, weather, entities, animation readers
  mis_anim.zbd                      compiled mission animations

interp.zbd
  support\c3\m01.gw                 world activation and placement
```

These inputs keep their scopes. A mission reader does not become a chapter reader because both
are visible during a flight, and a generic "nearest file wins" rule is incorrect. Each consumer
chooses its own scope. For example, objectives come from the mission scope, AI nets come from the
chapter scope, and target labels use the mission's `targets.zrd` first with an explicit chapter
fallback.

The chapter world is a superset. It contains scenery and entities for several modes and missions.
The `.gw` setup script switches and moves the subset used by the selected mission before startup
animations run. Reader records then refer to those world nodes by name. A preview that loads only
the mission readers therefore cannot show the mission accurately.

## The campaign mission reader census

Every story-mission archive contains nine common readers. Optional mission systems and
mission-specific animation definitions raise the archive total to between 11 and 22 readers.

| Reader | Story missions | Role | Detailed reference |
|---|---:|---|---|
| `aiv.zrd` | 24/24 | AI vehicle roster, spawn poses, teams, groups, skills, and generator parameter templates | [AI rosters](../ai-rosters.md) |
| `egen.zrd` | 24/24 | Enemy-generator hosts and launch cycles; an empty mission still carries the reader | [Enemy generators](../mission-entities/enemy-generators.md) |
| `location.zrd` | 24/24 | Named records with position- and rotation-shaped values; some roots carry no named records, and no CSVM named-point consumer is established | [Briefing](../briefing.md) |
| `map.zrd` | 24/24 | Describes an in-flight cockpit-map transform, animations, and player marker; CSVM has no established map-specific reader | [Briefing](../briefing.md) |
| `mis_anim.zrd` | 24/24 | Index of animation-definition reader files visible to the mission; not the compiled archive of the same stem | [Animation definitions](../anim-definitions.md) |
| `net.zrd` | 24/24 | Multiplayer spawn table carried by the story folders; it does not place a campaign player | [Network spawns](../net-spawns.md) |
| `objectives.zrd` | 24/24 | Player start and the ordered objective state machines | [Objectives](../objectives.md), [spawns](../spawns.md) |
| `startanims.zrd` | 24/24 | Ordered `NEW_GAME_START` animation names | [Animation definitions](../anim-definitions.md) |
| `weather.zrd` | 24/24 | Fog, sunlight, cloud, wind, and precipitation parameters | [Weather](../weather.md) |
| `targets.zrd` | 23/24 | Initial target flags and display-string keys. `C1C/M01` takes the chapter reader instead. | [Target display keys](../missions.md#target-display-keys) |
| `dzones.zrd` | 22/24 | Per-mission danger-zone disables, objective numbers, and snapshot policy | [Zone overrides](../missions.md#zone-overrides) |
| `zeppelins.zrd` | 22/24 | Mission zeppelin instances, poses, damage zones, teams, and weapons | [Mission entities](../mission-entities.md) |
| `zepstate.zrd` | 8/24 | Zeppelin animation definitions and state wiring | [Animation definitions](../anim-definitions.md) |
| `pickups.zrd` | 4/24 | Proximity sensors used by mission cutscenes | [Cutscenes](../anim-definitions/cutscenes.md) |
| Other named readers | varies | Mission-local animation definitions such as `ladder.zrd`, `objcomplete.zrd`, and cutscene files named by `mis_anim.zrd` | [Animation definitions](../anim-definitions.md) |

An absent file and a present empty reader are different. The binary decoder and extracted-JSON
reader normalize an empty top-level root to an empty list in memory, and export writes that root as
JSON `null`. A top-level JSON `[]` and `null` therefore cannot be distinguished after loading.
Nonempty trees and nested nulls remain part of the reader data.

Every one of the 24 addresses also resolves to a chapter `gamez.zbd`, chapter `cam_anim.zbd`,
mission `mis_anim.zbd`, and per-mission `.gw` setup script in the checked install. The chapter
companions are shared by several story missions.

## Bundle reference

### Shared inputs

| Source | Mission use | Authoring boundary |
|---|---|---|
| `ZBD/zrdr.zbd` | `cm_sequence.zrd`, vehicle and weapon definitions, sounds, shared animation definitions, and shared briefing data | Read-only for a mission editor. Replacing it affects the whole game and requires a complete 221-reader scope in this install. |
| `strings.dll` | Resolves the `MSG_*` keys used by objectives, targets, rosters, and briefings | Read-only lookup. A text edit belongs to the separate `messages.json` mod output. |
| `ZBD/interp.zbd` | Supplies `support\<chapter>\<mission>.gw`, which activates, removes, translates, and rotates chapter-world entities | Read-only first. A modded `interp.json` replaces all scripts, not one mission script. |

### Chapter inputs

| Source | Mission use | Authoring boundary |
|---|---|---|
| `<chapter>/gamez.zbd` | Static world, named nodes, danger-zone geometry, scripted paths, target sites, and mission entity hosts | Preview and validation source. Editing it changes every mission on the chapter and has no current writer. |
| `<chapter>/rtexture<N>.zbd` or `texture.zbd` | World textures selected by the chapter's highest available tier | Preview source. |
| `<chapter>/zrdr.zbd` | AI net index and paths, chapter animation readers, target fallback, clutter templates, and weather-volume data | Read-only first. It is a separate archive-level override shared by the chapter. |
| `<chapter>/cam_anim.zbd` | Compiled chapter animation definitions and SI scripts | Read-only. Compiled definitions outrank reader definitions on collision. |

### Mission inputs

| Source | Mission use | Authoring boundary |
|---|---|---|
| `<chapter>/<mission>/zrdr.zbd` | The reader set in the census above | The first writable unit. Export the whole scope, even when one reader changes. |
| `<chapter>/<mission>/mis_anim.zbd` | Compiled mission animation definitions and SI scripts | Read-only first. It is distinct from the `mis_anim.zrd` reader inside the reader scope. |

## Cross-file joins

Most authoring errors are valid JSON with an unresolved name. An editor should validate these joins
as the value changes:

| From | Resolves against | Meaning |
|---|---|---|
| `cm_sequence.zrd` `campaign` and `mission` | Fixed chapter-folder mapping and `M01` to `M05` folders | Selects the mission bundle. `seq`, the one-based `CM` ordinal, and the storage address are different identifiers. |
| `objectives.zrd` target directives | `targets.zrd`, roster names, and gamez node paths | Adds, removes, labels, wakes, or tests the named mission object. Objective blocks must remain contiguous from `OBJECTIVE1`. |
| `objectives.zrd` animation and sound directives | The assembled animation program and shared sound groups | Starts animations and mission audio by name. Unknown directive names stay inert. |
| `aiv.zrd` vehicle names and `netids` | Shared vehicle definitions and the chapter `neindex.zrd`/`ne0NNNNN.zrd` graph | Builds and routes each roster member. Position and yaw are authored directly in the roster. |
| `egen.zrd` `vehicle.params` | Designer labels in the same mission's `aiv.zrd` header | Selects the generated aircraft template. |
| `egen.zrd` host, origin, nets, and animations | Gamez nodes, chapter AI nets, and the animation program | Places and drives the generator. A generator record does not carry a free-standing world position. |
| `zeppelins.zrd` node, net, targets, and part names | Gamez nodes, chapter AI nets, and targetable world or roster objects | Places and drives one zeppelin and binds its damage model. |
| `targets.zrd` nodes and message keys | Gamez or roster names and `strings.dll` | Supplies the starting target sets and HUD text. |
| `dzones.zrd` path names | Gamez `dzpathN` geometry and `objectives.zrd` danger-zone conditions | Narrows and numbers the chapter's authored stunt routes. The geometry is not stored in `dzones.zrd`. |
| `startanims.zrd` names | The assembled animation program | Starts existing definitions in authored order. Order matters when definitions write the same state. |
| `mis_anim.zrd` paths | Shared, chapter, and mission animation-definition readers | Selects reader files visible to the mission. The compiled mission archive still decides which compiled definitions win. |
| `weather.zrd` zones | The chapter horizon and fog-volume geometry | Selects the environmental state applied to the world. |

## Reader rules and edge cases

### A mod replaces an archive, not one reader

The modding folder is selected archive by archive. If
`<mod>\<chapter>\<mission>\zrdr.zip` or its unpacked `zrdr\` sibling exists and reads, that output
is the entire mission reader scope. Missing entries do not fall back to the retail
`zrdr.zbd`.

This makes a single-file export unsafe. A folder containing only an edited
`objectives.zrd.json` also removes `aiv.zrd`, `weather.zrd`, `startanims.zrd`, and every other
mission reader from CSVM's view. A mission editor must begin from a complete scope and write every
retained entry.

### Preserve the raw list tree

The canonical editable model should remain the reader's ordered tree of strings, `float`s, nested
lists, and nulls:

- Do not convert the document to JSON objects. The format is arrays, and duplicate keys and bare
  flags are meaningful.
- Do not use `ZrdrDict` as the backing store. It is a convenient consumer view that collapses
  duplicate keys and does not preserve key order.
- Preserve list order, unknown directives, unsupported keys, stray values, nulls, and file names.
  Typed editor panels should point back to exact locations in the raw tree.
- Both integer and float binary tags enter CSVM's model as `float`. A JSON override can preserve
  the runtime value and the stored float bits, but it cannot reconstruct which binary number tag
  the retail reader used.
- An empty nested binary list becomes null. Treating null as an empty `Godot.Collections.Array`
  changes the extracted representation even where a consumer gives both the same meaning.
- An empty top-level reader writes as JSON `null`, matching the extracted form. An empty JSON array
  also reads as an empty root, but the writer does not introduce that second spelling.

The binary reader keeps archive table order. A generated ZIP can retain that order explicitly;
directory enumeration does not promise it. A tool should therefore write `zrdr.zip` in the source
entry order unless folder-mode gains an ordering manifest. The ZIP entries still use the unzbd
names, such as `objectives.zrd.json`. An entry named only `objectives.json` is ignored.

### Keep scope and fallback decisions explicit

Do not merge shared, chapter, and mission files into one dictionary. The same stem can have a
different role in each scope, and most consumers do not fall back. Target labels are an explicit
exception: mission `targets.zrd` wins as a whole, otherwise the chapter's file is used.

An existing mod output is also a complete scope. Opening it and silently filling absent readers
from retail would change its meaning. A separate "rebase from retail" action may create a new
complete document, but ordinary open and save must preserve intentional absence.

### Compiled animations mask reader edits

`AnimProgram` loads `cam_anim.zbd` and `mis_anim.zbd` first. A compiled definition wins when a
reader definition has the same identity, and the compiled mission manifest filters reader-only
definitions that are not part of that mission. Editing a mission's animation reader can therefore
produce valid output with no visible change.

The startup panel renames and reorders existing `startanims.zrd` entries. The index, `zepstate.zrd`,
and mission-specific animation definitions have no typed authoring controls; their mission-scope
string and float leaves remain available in Raw. Writable animation authoring needs an explicit
compiled-archive strategy rather than a JSON panel alone.

## Godot editor workflow

The project enables `addons/campaign_mission_editor/plugin.cfg`. Open the `CSVM` project in the
Godot 4.7 .NET editor and use the **Campaign Missions** dock. Its source picker accepts the same
install folders, discs, `.iso` files, and raw-disc descriptors as a game launch. The mod-folder
picker names an external output root; export is refused under either the source or the checkout.

### Document model

The Godot scene should be a view of a plain C# mission document, not the source of truth:

| Layer | Responsibility |
|---|---|
| `CampaignMissionDocument` | Holds the campaign address and a deep copy of every mission `ZrdrFile` in source order. Its exporter owns complete-scope output and round-trip verification. |
| Raw reader model | Keeps each `List<object?>` tree exactly, including unknown and duplicate entries. |
| Typed adapters | `CampaignPlayerInit` exposes the complete player start; `CampaignRosterStarts` exposes complete roster spawn prefixes and independently supported existing AI fields; `CampaignMissionTargets` exposes runtime-selected flags and label keys; `CampaignMissionZeppelins` exposes existing placement, behavior, and part fields; `CampaignMissionGenerators` exposes existing generator timing and references. The weather adapters expose bounded weather fields. All retain raw indexes. |
| Edit operations | Capture before/after raw values and history state for a complete typed or raw edit. Structural operations retain the corresponding raw snapshots. |
| Preview nodes | Derived `Node3D` handles and overlays rebuilt from typed values. They are not saved as `.tscn` or `.tres` mission data. |
| Diagnostics | Resolves world nodes, messages, AI nets, animation names, and cross-reader references continuously. |

The document, raw-tree dock, typed adapters, continuous diagnostics and world preview are present.
Raw cells accept only same-type string or finite-float replacements. The target adapter has
separate, controlled operations for flags, absent label keys, and an explicitly copied chapter
reader.

Godot resources are useful for editor UI state, but they should not replace the raw reader model.
A heterogeneous reader tree contains representations that an exported inspector property does not
retain reliably, including duplicate keys, null nested lists, and unknown list shapes.

Each successful typed operation or raw scalar edit is one Godot editor-history action. Undo and
redo restore captured raw values and float bits, refresh diagnostics and typed views, and rebuild
affected preview overlays. Validated multi-leaf changes are atomic; failed validation and
unchanged edits create no action. A registration failure rolls the document back and leaves any
already-published action inert. Structural edits preserve unknown data and start-animation moves
retain entry identity. The revision follows history, while binding freshness uses a separate
monotonic generation.

The dirty marker compares the current history state to the loaded or last exported state. Undoing
an exported edit is therefore dirty, and redoing it is clean. Selecting another mission, replacing
the document or unloading the plugin invalidates its old actions. They can remain in Godot's shared
global history, but they no longer mutate a document, and unrelated editor history is not cleared.

### Import

1. Let the user choose their own install or disc image and an external modding folder. Do not copy
   retail data into the repository.
2. Load shared `cm_sequence.zrd` and select a `CM` ordinal or storage address.
3. Decode the complete retail mission reader scope with `ZrdrSet.Load`.
4. Load the chapter world, textures, chapter readers, animation archives, messages, and setup
   script as read-only dependencies.
5. Build typed adapters over deep copies of the raw mission readers. Keep source names and entry
   order beside them.
6. Build the preview only after the `.gw` setup has selected the mission's world state. Startup
   animation playback is optional, but its names and references can still be validated.

The dock performs steps 1 through 6. Its lightweight retail loader reads shared readers, messages
and interpreter scripts without opening planes, HUD art, sounds or UI resources. A fresh
per-selection bundle materializes the chapter world, highest available texture tier, chapter
readers, both compiled animation archives and parsed setup script. Each input retains its exact
path and an availability state; a failed replacement leaves no stale document or bundle. The typed
`PLAYER_INIT`, roster-start, target, zeppelin, generator, cloud-band, wind, precipitation, fog, and lighting adapters
bind to the deep-copied mission scope or read-only chapter fallback. Continuous diagnostics rebuild from document revisions and keep world,
message, vehicle, AI-net, target, animation, sound, generator, objective and reader-path joins in
their runtime scopes. Confirmed inherited misses remain visible without blocking an unchanged
scope; newly introduced misses block export, while runtime-staged or unavailable checks remain
explicitly unverified.

The **Existing complete mod scope** mode accepts a selected mission's `zrdr.zip` or unpacked `zrdr`
directory. Preflight rejects nested paths, unsupported names and JSON shapes, case-insensitive
duplicates, partial retail inventories, and a ZIP whose unpacked sibling is the effective runtime
owner. Retail names establish completeness but retail values never fill a missing mod reader.
Mod-only readers remain in observed order. A scope path is bound to the selected mission and clears
when the mission changes.

### Read-only mission preview

The **Preview** tab opens a transient, collision-free copy of the selected chapter world. Mission
setup applies when available; the viewport does not play compiled or startup animations, stage
actors, simulate AI-net eligibility or moving entities, or apply weather-zone culling. World and
site poses can therefore differ from a live mission.

The preview displays the `PLAYER_INIT` start, enabled non-player roster starts, target sites,
zeppelin poses, generator origins and chapter danger ribbons. The colors and inactive markers are
identified in the tab. Player and roster XYZ coordinates are metres and yaw is in degrees. A
matching built world node takes precedence for a roster start. Zeppelin orientation uses authored
pitch and yaw; generators resolve origins under their host and keep offsets in the host frame.
Surface launch points include the runtime's world-up lift. Target markers prefer an objective's
literal `TRAVELERS` point, then the built site's mesh-bounds center, with an explicitly labeled
unstaged actor fallback. Danger ribbons show sampled chapter geometry before setup, together with
effective mission overrides. No named-location point is inferred without a CSVM consumer.

Choose a row or click a handle to inspect its exact mission, chapter or world source and any related
mission paths. Right-drag orbits, the wheel zooms, and **Frame selection** centers the camera.
**Show mission source** selects the first related mission path in Raw. Handles are inspection
controls, not draggable authoring fields. Edits replace only affected overlay layers. Closing the
preview, switching missions or unloading the dock frees the world and derived nodes. Neither those
nodes nor retail geometry are exported or saved as `.tscn` or `.tres` mission data.

Imported or generated retail-derived data remains in memory or under the selected external
modding folder. It does not belong under `CSVM/`, `docs/`, or a committed Godot resource.

A shareable authoring project should contain only the author's changes, not unchanged readers
copied from the retail archive. The editor can apply those changes to a recipient's own install
and materialize the complete runtime scope locally. The full generated `zrdr.zip` is a local build
product and must not be committed or published.

### Editor surfaces

The current dock gives every reader the same raw tree. Existing string and float cells are
editable; list structure, nulls, file membership, and unsupported JSON value types are read-only
there, apart from the target and danger-zone panels' explicit structural actions.
It also binds the last `PLAYER_INIT` occurrence, matching `ZrdrDict`'s consumer view, and keeps the
exact raw path of each typed field. A missing, bare, truncated, or non-float record disables the
panel with a diagnostic instead of constructing defaults.

The **Roster** tab selects the first runtime-selected `aiv` reader and lists every raw block in
order. It edits position, yaw, team and group, plus independent groups for existing net IDs,
health and armor, activation and nitro, pilot ratings and ace, signature mask, primary target and
title, objective marker, scripted path and accent. Each group validates all its leaves before one
document revision. Missing, malformed and unsupported leaves remain raw; nothing adds a net-list
choice or changes its order. Names, enabled flags and positional template headers remain raw.
`player` is read-only here, including its roster pose, because `PLAYER_INIT` places the human.
A matching live world node can override a non-player block's initial roster pose.

The numeric groups accept these canonical values:

- Net choices and accent IDs accept `-1` or an integer from `0` through `16777216`. Existing
  fractional or otherwise unsupported values remain raw.
- Health and armor accept `-1` or a nonnegative finite value. Health uses its default at both
  `-1` and `0`; armor uses its default at `-1` and treats `0` as an override.
- Ratings accept `-1` or an integer from `0` through `9`. Activation, nitro, ace and marker
  flags accept `-1`, `0` or `1`. The maneuver mask accepts `-1` or a 17-bit nonnegative integer.
- Target, title, enabled-marker labels and scripted paths expose existing strings only. Edited
  strings cannot contain control characters or surrounding whitespace; numeric no-path values
  remain raw.

The per-block reference view uses continuous diagnostics. Net IDs resolve only against the chapter
graphs, not a mission `net.zrd`; empty graphs are unresolved. Template labels resolve only to
disabled, plannable blocks in the selected mission reader, with the last plannable duplicate
winning. New missing references block export, inherited misses remain visible, and unavailable
dependencies are unverified. A generator chooses its own patrol net and launch position, but the
template plan's first nonnegative numeric net choice can still supply range defaults. Marker labels
are editable and treated as message keys only when the marker flag is enabled; an unmarked help
slot can be designer text.

The **Targets** tab lists raw entry indexes and selected node keys, including shadowed duplicates.
It shows each key's world-reference verdict and each label key's message verdict and resolved
text. Node lists remain raw. The last label and node pairs win within an entry; later matching
node keys replace earlier ones case-insensitively. A malformed selected label remains raw, and an
entry without a valid selected node cannot be edited.

Mission-local targets are writable. An inherited chapter table is read-only until the user
confirms **Copy chapter targets to this mission**. That action deep-copies the complete table into
the mission scope, leaves the chapter unchanged, and creates a whole-file override that does not
follow later chapter changes. A present empty mission or chapter reader is invalid, not a fallback.
The two starting flags are independent. Adding a flag adds a bare pair; removing it removes all
matching bare or null-valued pairs, but refuses a pair with extra payload. Existing label values
can be cleared to an empty string, while a previously absent label is added only when supplied.
Unknown pairs, duplicate and unselected values, node paths, tails, nulls, and float bits are
preserved. Validation completes before any target entry changes.

The **Zeppelins** tab selects a mission-local instance, a field group and, for part lists, one
authored entry. It edits hull and net names, position X/Y/Z in metres, yaw and pitch in degrees,
speed and acceleration limits, team and activation, survivor threshold, broadside settings,
targets, healthy zones, engines, gasbags and cannon records. A selected row is validated and
written as one document revision. Invalid required `node`, `net`, or three-component `position`
disables that instance and produces a blocking diagnostic; repair malformed structure in the
source reader. An absent reader is not an error, but an empty reader is rejected and blocks export.
Absent or malformed optional values, unknown fields, skipped entries, unselected duplicates and
all tails stay raw. The name or numeric representation of `team` is preserved. Existing nonzero
`deactivated` values remain unchanged unless edited, when the field
accepts 0 or 1. New survivor counts are integral and, for nonempty healthy lists, cannot exceed the
valid entry count.

The last alternating key wins even when it is a bare key. Each healthy entry counts toward the
survivor threshold, while duplicate node names share the first state-child binding. The last
gasbag entry of a name supplies the matching healthy zone's hit points and animation; unmatched
gasbags do not currently supply a pool. The dock marks these cases. `min_pitch`, `max_pitch`, and
the cannon-health `gunback` and `frame` fields stay raw because the motion and damage consumers do
not use them. Pitch is not clamped to those bounds. A broadside remains disengaged until mission
logic or the relevant game mode engages it.

Hull nodes and chapter nets show their static verdicts, targets use the runtime-actor resolver, and
destruction and cannon animation names use the effective-animation index. Staged part bindings
remain unverified, as do animations on unmatched gasbags. Suppressed duplicate references are
visible but do not block export. An unavailable animation archive cannot establish a missing name.

The **Generators** tab selects a runtime record and its existing timing, origin, vehicle-template,
or AI-net fields. It keeps the chapter-world host read-only. For a zeppelin, the origin is a named
node under that host; the displayed offset is derived from source-world transforms before `.gw`
setup and live animation. A missing origin falls back to the host. Surface take-off points are
derived from the host name and resolved under it, not positioned by the generator record.

Capacity is parsed but does not seed CSVM's launch credit. Wave and individual periods are seconds,
and their sum is the inter-wave gap. New counts must be integral, max active and wave size must be
positive, wave size cannot exceed max active, and new periods must be nonnegative with a finite
sum. The altitude gate is in metres; `-1` disables it. An authored rotation is exposed only when
all three components are finite; its X component is the drop pitch in degrees and the other
components remain raw. `choose_nets` is read-only because CSVM cycles both authored choices.
The entire selected row validates before one revision. Absent or malformed optional values,
unknown directives, unselected duplicates, list tails and untouched float bits remain raw.

Diagnostics resolve hosts in the chapter world, origins and surface path points under that host,
template labels through the mission's disabled roster blocks and vehicle plans, and net names in
the chapter net set. A generator with no direct net names has a nonblocking drop warning.
Objective `WAKEUP_GENERATOR` and `DEDG` uses follow the selected objective
reader and directive slots. Derived animation names and authored doors are checked against known
definitions, but a found door's host-scoped live binding remains unverified. A missing default
door is unverified and nonblocking because the runtime continues without it. Inherited misses
stay visible and a newly introduced confirmed miss blocks export.

The **Objectives** tab lists the contiguous runtime-selected blocks and each block's directives in
raw order. Its graph summary shows the selected block's objective links. A first exact-case
directive wins, including a bare one; the long-form wake directive takes precedence over
`WAKE_OBJECTIVE`. Duplicates, unsupported directives, malformed values and tails remain visible in
the raw reader. The raw tree is always available, and the tab can select `objectives.zrd` there.

Existing selected `IDENTITY` class, priority and message leaves, `BEGIN_DORMANT` timing leaves,
objective-number links, `INACTIVEn` path segments, target arguments and `SET_HELP_LABEL` fields
are editable when their shapes are supported. An `INACTIVEn` list is one world path. Each direct
target string is a separate target and each nested list is one path, so the panel never joins
separate targets into a single path. Help-label editing uses the first target and last message
argument. An absent value is not created. A complete row is checked before any of its leaves
change; strings and floats keep their types and untouched values keep their float bits.

Objective numbers are graph identities, not `IDENTITY` priorities. The runtime display keeps the
first objective per priority, and the briefing independently includes every keyed identity,
including duplicates. Changing priority can merge display rows and change reward-mask bits. A
confirmed number swap rewrites the two block keys and known objective links, including shadowed
duplicates, as one validated revision. The swap refuses gaps, unknown directives, noncanonical
blocks, malformed identities, objective-bearing nested data, ambiguous link shapes and unresolved
objective numbers. It retains raw order and does not renumber danger-zone completion slots.
Numbered block keys in the selected objective reader are read-only in Raw so a single-leaf edit
cannot bypass this check. Changing graph numbers can change the runtime's rotating completion order
or which identity wins; compiled scripts and other external consumers are not proved equivalent by
the swap.

Diagnostics resolve objective numbers, target and world paths, and message keys against the
selected bundle. A target path is resolved from the first matching node and then within each
selected subtree. Missing dependencies and staged bindings remain unverified. The briefing's
additional identities are checked even when an objective block or directive is shadowed at
runtime. A newly confirmed missing reference blocks export; an inherited miss remains visible.

The **Danger Zones** tab shows chapter path names, the selected objective blocks that name each
path, and the player's gate verdict. The chapter ribbon is a non-interactive top-down X/Z plot of
the runtime-selected source geometry before `.gw` setup; it neither moves gates nor writes gamez.
The ribbon selector's numeric-index duplicate rule can select a differently spelled node from the
player gate lookup, which uses the first case-insensitive world-node match.

Only the selected mission's first `dzones.zrd` is writable. No mission reader means no overrides,
not inheritance from a chapter reader. An explicit action creates an empty mission reader without
copying chapter data. The enabled and snapshot controls edit direct string membership in the
runtime-selected `disable` and `nosnapshot` lists. Clearing membership removes every matching
direct string; nested values and shadowed lists stay raw. The slot control edits the last valid
selected `objective_numbers` pair's float or appends a pair for an absent slot. Clearing an existing
slot uses `-1`, leaving its pair and tail intact. Changed slots must be integral and either `-1`
or nonnegative; all three controls validate before one revision. Unknown fields, malformed entries,
unselected duplicates, pair tails and untouched float bits remain unchanged.

The slot names a completion/scrapbook slot, not an `OBJECTIVEn` dependency. A snapshot also needs
an armed player zone, a nonnegative slot, a profile directory, and a camera. The pending commit sweep covers slots
10 to 31; debrief mask rows cover 18 to 30. Diagnostics use the selected chapter gamez for gates,
retain suppressed duplicates and distinguish inherited missing gates from new misses. Disabled
objective gates are suppressed until enabled. A disable can resolve to its selected objective or
runtime-selected AI ribbon when the same node has no valid player gates. A disabled zone cannot
complete for an objective; whether the objective remains achievable depends on its required count.

The cloud-band tab follows `WeatherState.Load` by binding the last list-valued `CLOUD_COVER`
block, then the first valid immediate `TOP`, `BOTTOM`, and `THICKNESS` values inside it. It requires
`TOP` above `BOTTOM` and an opaque-core thickness from zero through the full band depth. Applying
it writes those exact indexes. The optional `TOP_COLOR` and `BOTTOM_COLOR` fields each bind the
first immediate list the runtime would parse. An existing complete triple is editable in its
authored normalized or 0–255 encoding; an absent triple stays disabled and is not created. A
present malformed first list disables the panel rather than skipping to a later duplicate that
the runtime would not use. Later duplicates, vector tails, unknown fields, and every other weather
block remain raw. The tab warns that changing the midpoint also moves the hard cloud-deck regime
transition; it does not claim to preview that geometry.

The wind tab follows `WeatherState.Load` at both parsing layers. It binds the last list-valued
`WIND` block selected by the outer alternating dictionary, then the first valid immediate value
for each bare-scalar field inside that block. Applying it writes those exact indexes and leaves
duplicate fields, unknown fields, and every other weather block untouched.

The optional precipitation tab follows `WeatherState.ParsePrecip` over the weather root rather
than treating the fields as one nested block. It binds the first runtime-selected `TYPE`, `COLOR`,
`WIND_DIR`, `WIND_VEL`, `GRAVITY`, optional `PARTICLES`, and `ALPHA_GRADIENT` values. An absent
definition stays absent, and a malformed required selected value does not fall through to a later
duplicate. Applying it preserves missing `PARTICLES`, raw color encoding, list tails, later
duplicates, unknown values, and every unrelated weather field.

The fog tab discovers the same `ZONE<digit>` keys as `WeatherState.Load`, in file order, and
excludes every `SW_*` software-renderer twin. Each selector entry binds the outer dictionary's last
zone list and the inner dictionary's last `FOG_COLOR`, `FOG_RANGES`, and `FOG_ALTITUDE` lists.
Colors stay in their authored encoding: a normalized block remains 0 to 1, while a block selected
by the runtime's strict component-above-1 rule remains 0 to 255. The tab labels these values as raw
instead of silently converting the encoding.

The Light tab uses the same dynamic zone order and `SW_*` exclusion, with its own exact-index
binding to the last selected `CLIP_RANGES`, sunlight scalar, color, bicolored, and orientation
lists. The shipped campaign's 48 runtime zones carry every list at its canonical width. Modified
readers may omit each one independently because `WeatherState` supplies independent fallbacks.
Absent fields remain disabled and are not created. Applying a zone writes only clip far,
`SUNLIGHT_DIFFUSE`, `SUNLIGHT_AMBIENT`, the two authored color triples,
`SUNLIGHT_BICOLORED`, and the existing orientation components. A two-component orientation does
not gain a roll. Duplicates, vector tails, `CLIP_RANGES[0]`, `SUNLIGHT_ACTIVE`,
`SUNLIGHT_STATIC`, `SW_*` twins, and unknown zone data remain raw. Orientation is shown in authored
degrees as a shading direction, not a sun-billboard position. Clip far remains informational in
CSVM and does not change the remake's camera far plane.

The read-only Summary tab rebuilds `WeatherState` from the current editable reader after typed or
raw changes. One manual runtime-zone selector keeps fog and lighting on the same zone. An altitude
probe shows the base whiteout amount, interpolated band color, band-only camera state, cloud-band
and opaque-core thresholds, and a numeric fog-distance plot. It also summarizes wind,
precipitation, world light, sunlight colors and shading orientation. It labels clip far as
informational and states that chapter deck, horizon and fog-volume geometry is not loaded. State 3
therefore remains a dependency-backed result, not something the altitude probe invents.

The Animation and Readers tab selects existing `NEW_GAME_START` entries from the first matching
`startanims.zrd` reader, in the order CSVM dispatches them. Renaming changes only an entry's first
string. Moving an entry carries its whole list and tail between recognized slots without changing
malformed entries, unknown values, other groups, or duplicate names. No absent reader or entry is
created. The raw tree remains available for other existing leaves.

Each startup reference uses the compiled-first animation program, including shared, chapter and
mission reader gates and both compiled archives. An unavailable required input gives an unverified
verdict; a confirmed missing name newly introduced by an edit prevents export. Inherited misses
follow their original entries when reordered, so a new duplicate miss is still reported. A resolved
name proves definition lookup, not successful live node binding. See [Animation definitions](../anim-definitions.md).

The same tab shows `map.zrd` presence and raw structure, not map gameplay values. It lists the valid
multiplayer `net.zrd` spawn positions and headings accepted by `SpawnPoints.LoadNetTable`, and
separately identifies ignored malformed rows. An empty reader root is reported as rejected by that
reader rather than as an accepted empty table. Those spawns do not place a campaign player or define
chapter AI nets. The `mis_anim.zrd` reader index, winning reader-sourced definitions, and effective
program counts are distinct from the read-only compiled archives. See [Briefing](../briefing.md)
and [Network spawns](../net-spawns.md).

| Surface | Godot view | Typed behavior |
|---|---|---|
| `PLAYER_INIT` | Numeric panel and read-only player-start handle | Edit position in metres, the full authored pitch/yaw/roll triple in degrees, throttle as a 0 to 1 fraction, and speed in metres per second. The raw speed field is the displayed speed divided by 0.1. The pending flag is visible and read-only. CSVM currently applies only yaw, but pitch and roll remain preserved and editable. |
| `aiv.zrd` | Roster selector, start and AI-field panels, reference verdicts and read-only start handles | Edit supported existing leaves when every block carries the complete six-slot prefix. Names, enabled/template flags, header data, absent or malformed leaves and other tails remain raw. The `player` block is read-only because `PLAYER_INIT` places the human aircraft. Chapter-net paths are not drawn. |
| `zeppelins.zrd` | Instance, field-group and part selectors, reference verdicts and read-only hull handles | Edit existing runtime-backed placement, motion, team, targets, and damage fields without rebuilding records. Missing optional fields and unsupported tails remain raw. |
| `egen.zrd` | Record, field-group and net selectors, reference verdicts and read-only generator handles | Edit existing timing, origin, template, net and door fields. The chapter-world host is read-only and the origin offset is derived from its source subtree. |
| `location.zrd` | Raw tree only | Preserve the complete reader and allow generic existing-leaf edits. No typed point view or point-specific gameplay effect is established. |
| `dzones.zrd` | Path selector, gate verdicts, top-down plot and 3D chapter ribbons | Edit mission-local enablement, completion/scrapbook slot and snapshot policy, not gate geometry. |
| `targets.zrd` | Target selector, flags, message keys, reference verdicts and read-only target handles | Edit mission-local starting flags and labels; explicitly copy an inherited chapter table before editing it. Node lists and unsupported payloads remain raw. |
| `objectives.zrd` | Ordered objective list or graph plus directive inspector | Preserve contiguous numbering, raw directive order, unknown directives, and target paths. |
| `weather.zrd` | Implemented cloud-band, wind, precipitation, per-zone fog and Light tabs, plus a runtime-derived numeric Summary tab | Edit `CLOUD_COVER`, `WIND`, top-level precipitation, and each runtime `ZONE<digit>` in file order. Zone controls cover fog fields, clip far, sunlight scalars and colors, the bicolored flag, and authored shading orientation. The Summary tab plots numeric band and fog thresholds without chapter geometry. Missing optional fields are not created. Color encodings, list tails, duplicates, `SW_*` twins, and unknown values remain raw. |
| `startanims.zrd` | Implemented ordered selector and reference verdicts | Rename or move existing runtime-consumed entries, preserving duplicates, unknowns and full entry tails. |
| `map.zrd`, `net.zrd`, animation readers | Implemented read-only structural and runtime summaries | Show map structure, multiplayer spawn values, indexed paths and effective animation definitions without claiming unconsumed campaign effects. |
| `.gw`, gamez, chapter readers, compiled anims | Read-only dependency browser | Show what controls a value and where a wider-scope edit would belong. |

The ordinary named records in `location.zrd` have no current CSVM point consumer. Player spawn,
objective travelers and warps read `objectives.zrd`; node-form travelers resolve against the built
world or roster, and named warps resolve scripted world paths. Briefing art and script come from
shared `Briefing.zrd`, objective text from `objectives.zrd`, and the map projects positions supplied
by its callers. These observations do not establish how the original executable used the records.

The absence of a named-point consumer does not make the entire reader inert. `AnimDefs.LoadArchive`
scans every nonempty reader for `ANIMATION_DEFINITIONS`, and `MissionCutscenes` can load a reader
named in the mission's animation index. `AnimProgram` applies its compiled-manifest rules to
mission animation definitions. The editor also scans all eligible readers for
`ANIMATION_DEFINITION_FILE` references and indexes `ANIM_NAME` and `ANIMATION_NAME` strings.
These are generic animation behaviors, not location-point semantics. The reference diagnostics
report the location family as informational and unverified.

Applying an unchanged displayed speed does not recalculate its raw field. This preserves authored
float bits even when multiplying by 0.1 underflows or cannot round-trip through the displayed
metres-per-second value.

### Export

`CampaignMissionDocument` performs steps 1 through 3 below and enforces the local-output boundary
in step 6. Steps 4 and 5 remain validation and launch work.

1. Serialize the complete mission reader scope as nested JSON arrays. Retain every unedited file
   and every unedited raw subtree. Write an empty top-level reader as JSON `null`.
2. Write a temporary `<mod>\<chapter>\<mission>\zrdr.zip` with entries in source order, then
   replace the destination atomically. Refuse to overwrite a preferred unpacked `zrdr\` sibling
   because that directory is the effective runtime owner.
3. Reopen the generated archive with `ZrdrSet.FromExtracted` and compare names, tree shapes,
   values, nulls, order, and float bits against the in-memory document.
4. Refresh cross-reader diagnostics and report all confirmed failures before offering export or a
   test launch. Keep inherited and runtime-unverified findings visible without calling them new
   authoring failures.
5. Launch CSVM with `--mod-folder=<folder>` and the selected campaign position. Never write into
   the retail install or repack its `zrdr.zbd`.
6. Keep the generated complete archive local. Save or distribute only an author-owned delta that
   another user can apply to their own retail data.

## Validation

A mission editor is ready to save only when all of these checks pass:

- The exported scope reopens and contains every retained reader under a `*.zrd.json` name.
- A no-edit import/export preserves every normalized reader tree, effective file name, file order,
  nested null, and float bit. Empty top-level roots use the normalization described above.
- `ObjectiveScript`, `AiSkills`, `WeatherState`, `MissionTargets`, `Zeppelins`,
  `EnemyGenerators`, `CampaignDangerZones`, and `AnimProgram` can read the relevant output.
- Each edited node, target, generator host, entity part, AI net, animation, sound group, and
  message key is checked in the same bundle. Live-staged or unavailable bindings remain explicitly
  unverified rather than being reported as resolved.
- Objective numbering remains contiguous and every displayed objective has a valid identity.
- The external mod scope survives a real campaign load, not only an isolated parser test.

The no-edit round trip is the primary safety test. A typed panel that looks correct but rewrites
unknown data is not a lossless editor. Static diagnostics do not prove every displayed identity,
external objective-number consumer, or runtime completion order is valid.

### Complete-scope authoring matrix

`CampaignMissionAuthoringEndToEndTests` defines one invented complete mission for each of the
`ZONE1`/`ZONE2` and C5-style `ZONE1`/`ZONE3` layouts. The matrix edits the selected player start,
roster start and health, target description and flags, zeppelin placement, generator timing,
danger override, objective identity, start-animation order, cloud band, wind, precipitation, fog,
lighting, and one raw leaf. The location reader remains unedited and informational because no
current runtime consumer establishes point semantics. Map, net, animation and weather summaries
are read-only checks.

The independently specified expected trees include duplicate keys and reader names, unknown
fields, tails, nested nulls, negative zero and a subnormal. The matrix checks inherited versus
newly missing references, repair, unavailable animation bindings, preview refresh and selection,
then undo and redo of every edit. Runtime assertions cover player and roster starts, targets,
zeppelins, generators, objectives, danger zones, weather and start animations.

The in-memory cases run with the normal Quick unit tier. The ZIP import/export cases are
deliberately skipped unless `CSVM_AUTHORING_OUTPUT_ROOT` names an existing absolute directory
outside the checkout and the system temporary directory, with no linked or junction ancestors.
Choose a non-temporary local directory dedicated to validation, then run:

```powershell
$env:CSVM_AUTHORING_OUTPUT_ROOT = 'D:\AuthoringValidation'
.\scripts\RunTests.ps1 -UnitFilter 'FullyQualifiedName~CampaignMissionAuthoringEndToEndTests' -SkipEngine -SkipGoldens
Remove-Item Env:\CSVM_AUTHORING_OUTPUT_ROOT
```

Each external case writes an invented input scope and exported complete scope in a unique owned
subdirectory, checks ZIP order and exact trees after both import and `ZrdrSet` reopen, and loads the
effective mission through `GameData`'s mod-folder path. Diagnostics, preview projections and
summaries are checked again after import. It removes the archives and its own empty directories.
Inspect the test result for skipped cases: an unset variable does not exercise
external output. These checks do not substitute for the editor viewport or a real campaign flight.

For the flight check, export the user's own mission to an external mod folder, seed an isolated
profile store under `.scratch/`, and set `$profile`, `$sequence`, `$profiles`, `$modRoot` and
`$shot` to the seeded profile name, its matching mission sequence, the absolute profile-store path,
the external mod-folder path and a unique external screenshot path. Then run:

```powershell
.\scripts\RunProbe.ps1 -TimeoutSec 600 "--campaign=${profile}:$sequence" "--profiles=$profiles" "--mod-folder=$modRoot" "--screenshot=$shot" --frames=7200 --mute
```

Inspect the mod-selection and mission-load logs and the edited behavior, and remove the screenshot
afterwards. Never create or retain a retail-derived archive, extracted reader or screenshot in the
repository. See `docs/verification.md` LOG-20 and SHELL-12 for profile isolation and the exit
condition.

The engine-free unit surface uses invented reader trees to cover deep-copy isolation,
type-preserving scalar edits, complete entry order, duplicate-name renames, empty-reader `null`,
negative zero, a subnormal float, unpacked-scope refusal, external-output guards, temporary-file
cleanup, and reopen verification. `CampaignPlayerInit` tests cover last-duplicate selection,
malformed records, exact raw-leaf edits, validation before mutation, and agreement with
`SpawnPoints.LoadPlayerInit`. `CampaignRosterStarts` and roster-field tests cover variable widths,
malformed prefixes and leaves, exact paths, validation before mutation, duplicate names and
templates, full-scope preservation, `AiSkills` and spawn-plan parity, player placement, and
scope-sensitive reference refresh. Optional story censuses report only shapes and duplicate
counts. `CampaignWeatherWind` tests cover outer duplicate selection, first-valid
bare-field selection, malformed records, exact raw-leaf edits, validation before mutation, and
agreement with `WeatherState.Load`. `CampaignWeatherFogZones` tests cover dynamic `ZONE1`/`ZONE3`
discovery, `SW_*` and unknown-field preservation, duplicate selection, both color encodings,
validation before mutation, exact raw-leaf edits, and `WeatherState` parity.
`CampaignWeatherLightingZones` tests cover `ZONE1`/`ZONE2` and `ZONE1`/`ZONE3` families,
last-list selection, independently absent fields, two- and three-component orientations,
`SW_*`, duplicate, unknown-field, and vector-tail preservation, both color encodings, validation
before mutation, exact raw-leaf edits, and `WeatherState.Zone` parity.
`CampaignWeatherDiagnostics` tests cover cloud and core thresholds, base whiteout and color
samples, plot positions, both runtime zone families, explicit absent values, zone-consistent fog
and lighting labels, and refresh after a raw edit.
`CampaignReferenceDiagnostics` tests cover exact source paths, simultaneous failures, first-match
duplicate suppression, wrong-scope names, unavailable dependencies, coalesced multi-leaf refresh,
new failure detection and repair clearing. `CampaignMissionTargets` and target-reference tests
cover runtime pair and node selection, inherited refusal and explicit copy, exact raw paths,
validation-before-mutation, unknown and float-bit preservation, publication, repair, and
`MissionTargets` parity. A separate install-backed census checks mission and chapter origins.
`CampaignMissionZeppelins` and zeppelin-reference tests cover required and optional fields,
variable-width parts, exact paths, duplicate and bare-key selection, unknown-tail and float-bit
preservation, atomic validation, inherited and new missing references, and `Zeppelins` and motion
parity. An install-backed story census measures optional presence and part widths.
`CampaignMissionGenerators` and generator-reference tests cover selected reader and raw paths,
variable and malformed records, duplicate and bare overrides, preservation, atomic validation,
host-relative offsets, scope-sensitive joins, objective uses, and `EnemyGenerators` and
`GeneratorCycle` parity. A separate install-backed census measures story-reader widths and
optional directives.
`CampaignMissionLocationTests` exercises the actual spawn, objective, briefing and animation
readers with invented location data, the generic diagnostic scans, and no-edit export including an
empty root. The install- and extraction-backed location censuses report only reader presence and
structural type and width counts; each skips when its corresponding game source is unavailable.
`CampaignMissionObjectivesTests` and `CampaignObjectiveReferenceTests` define invented-data checks
for contiguous numbering, both identity consumers, duplicate and bare-key selection, unknown data,
target and world paths, malformed records, atomic validation and swapping, no-edit export, and
`ObjectiveScript`/`ObjectiveGraph` behavior. `CampaignObjectiveCensusTests` inventories only
directive names, shapes and counts from a user-owned install or extraction; it skips without one.
`CampaignMissionImport` tests cover complete ZIP and
directory scopes, mod-only readers, entry order, deep-copy editing, null and empty trees, float
bits, missing inventories, nested and unsupported entries, case-insensitive duplicates, malformed
JSON, and unpacked-sibling precedence.
`CampaignWeatherCloudBand` tests cover outer duplicate selection, first-valid bare scalars,
first-list optional-color selection, both color encodings, absent and malformed colors, duplicate
and tail preservation, validation before mutation, exact raw-leaf edits, and `WeatherState` band,
whiteout, and color parity. `CampaignWeatherPrecipitation` tests cover exact top-level paths,
runtime first-match behavior, absent and malformed definitions, optional `PARTICLES`, both color
encodings, duplicate and vector-tail preservation, validation before mutation, and
`WeatherState.Precip` parity. A real campaign launch remains required.

## Evidence & limits

The archive census comes from `CampaignSequence.Load` over the shared reader followed by
`ZrdrSet.Load` over all 24 resolved mission addresses. The same walk checks the companion
`gamez.zbd`, `cam_anim.zbd`, `mis_anim.zbd`, and `.gw` setup script. It records entry names and
presence only, not retail payloads.

`ZrdrZbd.cs` establishes the binary value model and table order. `Zrdr.cs` establishes the
accepted `*.zrd.json` mod names. `GameData.cs` and `ModdingFolderTests.cs` establish whole-archive
replacement; the test's one-file mission override has its edited `pickups.zrd` and no
`objectives.zrd`. `CampaignMissionDocument.cs` owns raw edits, complete-scope JSON ZIP output, and
reopen comparison. `CampaignPlayerInit.cs` owns the exact-index typed player-start view and its
speed conversion. `CampaignRosterStarts.cs` owns exact-index roster-start and supported AI-field views.
`CampaignMissionTargets.cs` owns the target-entry view and explicit copy boundary.
`CampaignMissionZeppelins.cs` owns the exact-index zeppelin views and their row validation.
`CampaignMissionGenerators.cs` owns exact-index generator views, row validation and source-pose
host-relative offsets; `EnemyGenerators.cs` and `AiGeneratorRuntime.cs` define the consumed fields.
`CampaignMissionObjectives.cs` owns exact-index objective views and validated number swaps;
`CampaignObjectiveReferences.cs` supplies objective, path and message reference occurrences.
`CampaignWeatherWind.cs` owns the exact-index typed wind view and follows `WeatherState.Load`'s
alternating-list lookup. `CampaignWeatherFog.cs` owns dynamic exact-index fog-zone views and keeps
the source color encoding. `CampaignWeatherColor.cs` owns the shared raw color-encoding
validation. `CampaignWeatherCloudBand.cs` owns the exact-index typed cloud-band geometry and
existing-color view. `CampaignWeatherPrecipitation.cs` owns the exact-index typed precipitation
view over the weather root. `CampaignWeatherLighting.cs` owns dynamic exact-index clip and
sunlight-zone views. `CampaignWeatherDiagnostics.cs` owns the runtime-derived read-only summary
model, and `CampaignWeatherPreview.cs` draws its numeric plots. `CampaignMissionEditorPlugin.cs`
owns the Godot dock. `CampaignMissionDependencyBundle.cs` owns the selected address's materialized
read-only inputs and their exact-path availability; `GameData.LoadCampaignEditor` supplies only
the shared authoring subset and lazy chapter factory. Invented bundle tests cover exact paths,
texture-tier selection, missing and unavailable inputs, valid empty scopes, selection generations
and retail-only ownership. `CampaignReferenceExtractor.cs`, `CampaignReferenceIndex.cs` and
`CampaignReferenceDiagnostics.cs` own source projection, field-specific scope rules and
revision-bound aggregation respectively. `CampaignMissionImport.cs` owns strict existing-scope
preflight, completeness and source-kind retention. `AnimProgram.cs` establishes compiled-first
animation precedence.

The editor targets CSVM's external modding path only. It does not define a binary writer for the
original game, compiled animation authoring, chapter-world editing, a shared-scope campaign
rewrite, or typed mission fields beyond the supported adapters. Runtime-staged actor binding,
compiled event payloads, briefing media, sound playback assets and the explicitly labelled
unverified diagnostic families still require their live consumers. The census covers one English
retail data set.
