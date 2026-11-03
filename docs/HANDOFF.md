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
  The owner selected the original Bouncing Text artwork unchanged on 2026-11-03;
  it replaces the initial native glyph placeholder.
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
Windows and Linux CI also passed for the complete M0 branch.
The source CCP, server and port branches were not modified. Each change is kept
under roughly 600 changed text lines in a linear review stack.

## Review stack

All PRs are drafts. Each targets its predecessor; review from 1 through 9.
Retarget dependent PRs before removing parent branches during merging.

| PR | Change |
|---|---|
| [1](https://github.com/CC-Labs-it/ccp-classic/pull/1) | Foundation, local settings, session safety, CI |
| [2](https://github.com/CC-Labs-it/ccp-classic/pull/2) | Localization loader, English, German, Spanish |
| [3](https://github.com/CC-Labs-it/ccp-classic/pull/3) | French, Italian, Japanese |
| [4](https://github.com/CC-Labs-it/ccp-classic/pull/4) | Korean, Brazilian Portuguese, Russian |
| [5](https://github.com/CC-Labs-it/ccp-classic/pull/5) | Simplified Chinese and localization guards |
| [6](https://github.com/CC-Labs-it/ccp-classic/pull/6) | Avalonia bootstrap, native editors, preset/progress scaffolds |
| [7](https://github.com/CC-Labs-it/ccp-classic/pull/7) | Original public artwork and native animated logo |
| [8](https://github.com/CC-Labs-it/ccp-classic/pull/8) | Compact mosaic and owned settings windows |
| [9](https://github.com/CC-Labs-it/ccp-classic/pull/9) | Safety, layout, animation and language rendering tests |

## Next

M1: Flash Images, Subliminals plus recorded whispers, Spiral, Pink Filter, Audio,
and a verified CCP Default starter pack. Preserve the dashboard and owned editors.

## Owner questions

1. D12: repository name `ccp-classic` is provisional until 2026-11-10.
2. Before M5, review the original 27 achievement conditions and approve neutral names.
3. Before M4, approve the companion's neutral bark and trigger phrase list.
