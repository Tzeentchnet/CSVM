# FSR 3 and DLSS investigation

Status: investigated against CSVM `0.7.1` and Godot `4.7-stable` on 2026-11-18.

## Decision

Do not add `fsr3` or `dlss` choices to CSVM while it runs the stock Godot 4.7
editor and export templates. Godot 4.7 has no project or extension API that can
execute either choice, so adding the words now would create a dead setting or a
silent fallback.

The practical route is an experimental Windows-only custom Godot build:

1. Port DLSS Super Resolution and DLAA from NVIDIA's Godot fork to a maintained
   Godot 4.7 branch, without its path tracer.
2. Rebase the open FSR 3.1 upscaler work onto that same engine branch.
3. Build matching editor and export templates, then prove each backend before
   adding a capability-aware choice to CSVM.
4. Keep frame generation out of the first milestone. Treat NVIDIA DLSS Frame
   Generation and AMD FSR Frame Generation as separate renderer/presentation
   projects after both temporal upscalers are stable.

Keep stock Godot 4.7 and its existing FSR 2.2 path as the release baseline until
the custom build passes CSVM's complete verification battery and packaging
checks.

## What CSVM already has

CSVM already owns the application-side seam an integrated upscaler needs:

- [`AntiAliasingSetting.cs`](../CSVM/src/Utils/AntiAliasingSetting.cs) exposes
  Off, FXAA, SMAA, TAA, and Godot's FSR 2.2.
- [`RenderScaleSetting.cs`](../CSVM/src/Utils/RenderScaleSetting.cs) owns
  50-200 percent render-scale choices and limits FSR 2.2 to native or below.
- [`ViewportQuality.cs`](../CSVM/src/Utils/ViewportQuality.cs) applies the
  resolved pair to the root viewport, split-screen panes, and spyglass.
- The transparent cockpit-interior viewport deliberately keeps MSAA. Godot's
  temporal resolves replace its alpha and would otherwise turn the overlay
  opaque.
- [`DisplaySettingsSuites.cs`](../CSVM/src/Testing/DisplaySettingsSuites.cs)
  reads the resulting Godot viewport properties back, so a new backend can be
  tested at the actual engine boundary rather than only at the options model.

The project is Forward+ on Godot .NET 4.7. Windows exports use Vulkan by default
and can fall back to Direct3D 12. That is the right renderer floor for the AMD
and NVIDIA integrations, but it does not make them available to a project by
itself.

## Godot 4.7 constraints

### Stock engine

Godot 4.7's `Viewport.Scaling3DMode` contains Bilinear, FSR 1, FSR 2,
MetalFX Spatial, MetalFX Temporal, and Nearest. It contains no FSR 3, DLSS, or
Custom mode.

Godot proposal
[`#13718`](https://github.com/godotengine/godot-proposals/issues/13718) states
that a custom upscaler cannot currently be implemented as an add-on. The
corresponding engine pull request,
[`godot#114670`](https://github.com/godotengine/godot/pull/114670), adds a
Forward+ callback with color, depth, motion-vector, and output textures. As of
the investigation date it is open, conflicts with its target branch, and is not
part of Godot 4.7. It is an upscaling hook, not a frame-generation/present hook.

A `CompositorEffect`, compute shader, C# module, or ordinary GDExtension in the
CSVM repository therefore cannot replace Godot's integrated temporal upscaler
at the correct point in the 4.7 frame.

### AMD FSR 3

[`godot#111631`](https://github.com/godotengine/godot/pull/111631) proposes the
FidelityFX SDK and FSR 3.1.4 upscaler. It also adds an opaque-only copy used to
generate a reactive mask, which is important for transparent effects.

The pull request explicitly contains only the FSR 3 upscaler. It does not
contain FSR Frame Generation. As of the investigation date it:

- targets Godot `master`, not 4.7;
- is open, conflicting, and has changes requested;
- replaces/refactors much of Godot's existing FSR 1/2 integration; and
- adds a large third-party FidelityFX SDK surface to the engine.

This is useful reference code, but not a patch that can be enabled from
CSVM's C# project.

### NVIDIA DLSS

NVIDIA maintains
[`NVIDIA-RTX/godot`](https://github.com/NVIDIA-RTX/godot). Its
`backport/nvidia-pt-dlss-4.6.3` branch demonstrates DLSS on a stable Godot base,
but there is no 4.7 backport. The integration adds:

- a Windows-only Streamline driver for Vulkan and Direct3D 12;
- `Viewport.SCALING_3D_MODE_DLSS`;
- DLSS Super Resolution scales for Performance (`0.5`), Balanced (`0.58`),
  Quality (`0.66`), and DLAA (`1.0`);
- capability checks, NVIDIA Reflex, and a viewport frame-generation switch; and
- a requirement for Streamline runtime binaries such as `sl.interposer.dll`
  beside the executable.

The available branch also carries NVIDIA's path tracer and broad renderer
changes. CSVM should not adopt that whole engine fork. A 4.7 experiment should
port and retain only the Streamline, DLSS, capability, and required presentation
changes. Matching editor/export templates and the redistributable Streamline
runtime must be produced and licensed as one package.

### Frame generation is a separate feature

FSR 3 and DLSS are product families, not single interchangeable switches:

- Super Resolution/DLAA replaces Godot's temporal upscale/AA pass and consumes
  color, depth, motion vectors, jitter, and exposure.
- Frame Generation runs around presentation, needs real-frame pacing and latency
  integration, and must keep UI/HUD composition correct.

The custom-upscaler proposal cannot implement frame generation. NVIDIA's fork
has engine-level presentation support for DLSS Frame Generation. The open Godot
FSR 3 pull request has no AMD frame-generation implementation.

CSVM's simulation FPS, rendered FPS, and displayed/generated FPS must remain
separate measurements. A generated-frame count must never be reported as a
simulation or renderer performance gain.

## Route comparison

| Route | FSR 3 upscale | DLSS SR/DLAA | Frame generation | Maintenance |
|---|---:|---:|---:|---|
| Stock Godot 4.7 project/add-on | No | No | No | Low, but cannot meet the goal |
| Backport `godot#111631` | Yes | No | No | High; open renderer refactor |
| Port NVIDIA's 4.6.3 backport to 4.7 | No | Yes | DLSS only, with more engine work | High; custom engine and runtime |
| Backport `godot#114670`, then write native upscalers | Potentially | Potentially | No | High now; best common seam if stabilized |
| Upgrade after a custom-upscaler API reaches stable Godot | Potentially | Potentially | No | Lowest long-term SR maintenance |

## Recommended implementation sequence

### 1. Establish the custom-engine boundary

- Create a separate Godot fork pinned to the same 4.7 maintenance commit for
  the editor and export templates.
- Give custom builds an unambiguous version suffix and log the engine commit at
  CSVM startup.
- Preserve both Vulkan and Direct3D 12. Do not make Compatibility a supported
  path for these modes.
- Keep the engine fork out of the CSVM repository. CSVM should contain only the
  application adapter, build provenance, packaging rules, and notices.

### 2. Prove DLSS SR and DLAA first

- Port the minimum Streamline and DLSS changes from NVIDIA's stable backport.
- Package all required signed runtime binaries and add their licenses/notices.
- Query capability at runtime. Never infer support only from the GPU vendor.
- Prove Performance, Balanced, Quality, and DLAA under Vulkan and Direct3D 12.
- On an unsupported device or missing runtime, refuse DLSS explicitly and keep
  the previous setting. Do not report a fallback as DLSS.

This is the first spike because maintained, working Godot reference code exists
on a stable engine base. It also proves the custom template and runtime
packaging problem that both vendor paths introduce.

### 3. Add the FSR 3.1 upscaler

- Rebase the FSR 3 upscaler work rather than copying only its public enum.
- Retain FSR 2 until image quality, stability, and performance are measured.
- Enable and test reactive-mask generation. CSVM has clouds, smoke, propellers,
  heat shimmer, water, cockpit glass, and other transparent content that can
  expose unstable temporal reconstruction.
- Verify both AMD and NVIDIA devices under Vulkan and Direct3D 12.

### 4. Wire CSVM only after engine capability exists

Extend the existing setting pipeline rather than adding a parallel graphics
menu:

- add separate stable words for `fsr3` and `dlss`;
- offer only modes the running engine reports as supported;
- keep a saved but currently unavailable choice visible as unavailable rather
  than silently rewriting it;
- log requested mode, resolved mode, capability result, scale, renderer, and
  engine build;
- apply temporal upscalers to the root, split-screen, and spyglass viewports;
- keep the transparent cockpit viewport on MSAA;
- leave the VR quality profile unchanged until each backend is evaluated in
  stereo; and
- preserve Off/native as the faithful deterministic default, so existing
  golden images do not change merely because the custom engine is installed.

### 5. Evaluate frame generation separately

Start only after both super-resolution paths are green:

- DLSS Frame Generation needs NVIDIA Reflex and a real/generated-frame pacing
  model.
- AMD FSR Frame Generation needs a new engine integration; the FSR 3 upscaler
  pull request does not provide it.
- Compose the menu, HUD, pointer, and text after interpolation where the SDK
  permits it, or provide the SDK's UI resources explicitly.
- Disable frame generation for headless runs, deterministic captures, tests,
  VR, and any renderer/backend that cannot prove support.
- Report simulation, real render, generated presentation, input latency, and
  hitch metrics independently.

## Acceptance gates

A backend is not ready for a player-facing option until all of these are true:

1. The startup log proves the requested backend, actual backend, renderer,
   scale, capability result, engine commit, and runtime version.
2. Vulkan and Direct3D 12 both run the targeted engine suites.
3. Root, two- and four-player split-screen, spyglass, graphics live-switch, and
   window/display-mode changes survive repeated mode changes.
4. Transparent effects are checked in motion, including reactive-mask A/B
   evidence for FSR 3.
5. Camera cuts, respawns, mission loads, pause/resume, and viewport resize reset
   temporal history without flashes or old-frame leakage.
6. The transparent cockpit pass remains transparent and aligned with the world.
7. Unsupported hardware and missing SDK binaries produce a visible refusal and
   an actionable log entry.
8. Exported builds contain the exact engine/runtime pair tested by the editor,
   with third-party notices and redistribution terms reviewed.
9. Targeted display-setting tests, `RunTests.ps1 -Quick`, the complete test
   battery, golden images, and a representative performance capture pass.
10. Stock Godot's Off/native and FSR 2.2 behavior remains available for A/B and
    rollback.

## Sources pinned for this investigation

- [Godot 4.7 release](https://github.com/godotengine/godot/releases/tag/4.7-stable)
  and its [4.7 viewport scaling enum](https://github.com/godotengine/godot/blob/4.7-stable/scene/main/viewport.h)
- [Custom upscaler proposal #13718](https://github.com/godotengine/godot-proposals/issues/13718)
  and [implementation PR #114670](https://github.com/godotengine/godot/pull/114670)
- [FSR 3 upscaler PR #111631](https://github.com/godotengine/godot/pull/111631)
- [NVIDIA Godot fork](https://github.com/NVIDIA-RTX/godot) and its
  [4.6.3 DLSS backport](https://github.com/NVIDIA-RTX/godot/tree/backport/nvidia-pt-dlss-4.6.3)
- [NVIDIA Streamline SDK](https://github.com/NVIDIA-RTX/Streamline)

This investigation was prepared with the assistance of an AI coding agent.
