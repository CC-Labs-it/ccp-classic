# CCP Classic handoff

## Scope

M0: fresh .NET 10 / Avalonia 12.1.2 shell, ten languages, local settings and panic.
All effects are inert. M1 is the first playable milestone.

Source design: owner decisions and mockup dated 2026-11-03 in the CCP handoff.
CCP v7.2.2 was published as Latest on 2026-11-03, satisfying the build start gate.

## Implemented

- Current CCP image mosaic with twelve perimeter tiles and the native animated logo
  in its central 2x2 area. The owner replaced the flat card mockup on 2026-11-03.
- Compact 724 x 760 default window, two-row header. The owner requested a nearly
  square window fitted around the grid, avoiding empty sides.
- Studio, Presets and Progress. Tile editors and Library, Companion, Toys and
  Settings open in owned native windows. Panic reaches both owner and child.
- Configurable in-app panic, held-key suppression, shared manual Stop policy.
  Disabling panic requires the visible red warning and resets on the next launch.
- Gentle visuals, reduced motion and performance settings quiet the animated logo.
  Inactive, hidden and minimized surfaces do not render animation frames.
- Local atomic settings saves, export, recovery notice, normalized panic bindings.
- All ten embedded language files, exact key parity, UTF-8 without BOM, neutral
  chrome checks. No account, browser, updater, telemetry or networking API use.
- Original public CCP artwork and layered logo renderer with source provenance.
  The gendered bouncing-text image is omitted; that tile uses its native glyph.
- Windows and Linux build/test CI. Release tests cover real pointer/key routing,
  owned windows, neutral language resources, motion gates and network boundaries.

## Deliberately pending

- Global panic hook: M0 panic works inside Classic and its owned settings windows.
  Before M1 opens effect windows, wire their input into the shared controller and
  implement the platform hook. X11 and Wayland support still require platform work.
- Starter media and all effect engines. A fresh install doing something within
  30 seconds of Start is the M1 acceptance gate, not a claim about this inert shell.
- Strict Lock, presets loading/saving, achievements, companion clips/phrases,
  folder/file selection, autostart/hidden startup, haptics, Loom and wallpaper.
  Their unimplemented controls are disabled with a localized explanation.
- LibVLCSharp and NAudio enter with playback implementation, not as unused M0
  dependencies. SkiaSharp is already supplied by the pinned Avalonia renderer.

## Validation

`dotnet test -c Release`: 43 tests passed locally, including the final layout guard.
The source CCP, server and port branches were not modified. Each change is kept
under roughly 600 changed text lines in a linear review stack.

## Next

M1: Flash Images, Subliminals plus recorded whispers, Spiral, Pink Filter, Audio,
and a verified CCP Default starter pack. Preserve the dashboard and owned editors.

## Owner questions

1. D12: repository name `ccp-classic` is provisional until 2026-11-10.
2. Before M5, review the original 27 achievement conditions and approve neutral names.
3. Before M4, approve the companion's neutral bark and trigger phrase list.
4. Approve neutral Bouncing Text artwork if that tile should use a full image.
   The existing native glyph keeps the current build neutral.
