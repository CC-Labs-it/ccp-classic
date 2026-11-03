# CCP Classic handoff

## Scope

M0 is merged into main. M1 adds the first playable effects in a new review stack:
Flash Images, Subliminals with recorded whispers, Spiral, Pink Filter and Audio.

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
- M1: one frame clock, transparent native overlay per selected monitor, configurable
  flash frequency/size/opacity/fade/count/click dismissal/Hydra and subliminal rate,
  opacity/colour/frame duration. Live card changes are read while running.
- Native spiral choices and Pink Filter use eased entrances and exits. Gentle
  fades last at least 750 ms, slow spirals and dim persistent overlays; reduced
  motion preserves the current spiral phase. Outlined text composites opacity once.
- Start shows persistent effects immediately and schedules the first approved word
  at one second and starter image at two seconds. Fresh installs have usable content.
- Starter: one word-free image and five hash-verified neutral Circe recordings,
  223,763 bytes total. Missing clips stay silent text. Recorded whispers are opt-in.
- Native local audio uses per-player DirectSound on Windows and system LibVLC on
  Linux. Queue cancellation and native loading/teardown stay off the UI thread.
- Review fixes capture each cue's timing and keep Gentle entrances, exits and live
  opacity changes continuous. Expired words stay expired when Gentle is enabled.
- Master gain updates active recordings. Disabling Audio, Subliminals, Whispers
  or master volume stops recorded playback; a pending old stop cannot cancel a new run.
- Fill all monitors changes reconcile desktop hosts live, retaining unchanged
  layouts and replacing removed, resized or rescaled displays without restarting cues.
  Overlay input and global panic share one local-X11 DISPLAY parser, including unix/:0.
- Passive Windows global panic and local X11 polling feed the same panic policy.
  Native input owns both key edges while available, preventing duplicate stops
  from reordered global and local window events. In-app input remains the fallback.

## Deliberately pending

- Wayland global shortcuts and complete Linux desktop parity remain M8 work.
  Unsupported transparency/input backends refuse desktop-sized overlays and show
  a localized status. X11 needs a local composited desktop; remote DISPLAYs are refused.
- X11 key polling is 10 ms and may miss shorter external taps. Native Windows
  bridge/hook setup passed an offscreen probe; an owner-driven desktop session and
  actual audio listening remain manual review gates. No unexpected desktop effects
  or sounds were played during implementation.
- Strict Lock, presets loading/saving, achievements, companion clips/phrases,
  folder/file selection, autostart/hidden startup, haptics, Loom and wallpaper.
  Their unimplemented controls are disabled with a localized explanation.
- LibVLCSharp 3.10.1 supplies the cross-platform recorded-audio backend; NAudio is
  not required by this implementation. Linux needs LibVLC plus audio/codec plugins.
  Windows ships 3.0.24 natives. Missing audio reports status while visuals continue.

## Validation

`dotnet test -c Release`: 148 tests passed locally. The suite covers the existing
48 M0 tests plus M1 scheduling, real starter
hashes, native drawing pixels, media cancellation, input mappings/rebinding,
native/local event ordering and a composed first-playable frame with no stop ghosts.
The 26 review regressions cover live Gentle transitions, active-recording gain/mute,
stop/restart ordering, monitor host reconciliation and local-display parser parity.
Windows and Linux CI run for each PR and retrieve the starter recordings through LFS.
The Windows native offscreen probe confirmed real transparency, input pass-through,
global Esc listener availability and clean disposal without audio or synthetic keys.
The source CCP, server and port branches were not modified. Each change is kept
under roughly 600 changed text lines in a linear review stack.

## Review stack

M0 PRs 1 through 11 were merged in order on 2026-11-03, with dependent PRs retargeted
before parent merges. Source branches were preserved. M1 follows as small draft PRs.

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
| [10](https://github.com/CC-Labs-it/ccp-classic/pull/10) | Original CCP Bouncing Text artwork |
| [11](https://github.com/CC-Labs-it/ccp-classic/pull/11) | Review fixes: held panic keys across window focus and translated XP header |

## Next

### Conversation checkpoint, 2026-11-03

- Current branch: `codex/m1-07-live-session-review-fixes`. The implementation tip
  is `246f6b08ce87e141dcb4ad6daab197287542393d`; subsequent checkpoint changes are docs only.
- All six M1 review findings are fixed in PRs #17 and #18. Three agents implemented
  and cross-reviewed the fixes. Both new PRs passed Windows and Linux Release CI.
  The complete suite passed 148 tests, with zero failures or skips, including 26 new cases.
- M0 PRs #1-#11 are merged. M1 PRs #12-#18 remain drafts in a linear stack.
  The owner authorized continued production work and agent delegation on 2026-11-03.
  M2 and the remaining milestones are now in scope. All PR titles and descriptions
  reflect the seven-part stack. Each slice remains below 600 changed text lines.
- Latest request before this checkpoint: open the app. A native CCP Classic window
  was created successfully; the launch process subsequently exited with code 0.
  Confirm whether the owner still needs it open when resuming. Native desktop feel
  and actual audio listening are still manual checks, not covered by headless CI.
- Use `C:\Users\Admin\.dotnet10\dotnet.exe`, not the global .NET 9 SDK.
  Launch from `src/CCP.Classic.App/bin/Release/net10.0` with that executable and
  `CCP.Classic.App.dll`. The latest Release build already exists. For tests, run
  `dotnet test -c Release -m:4` through the same SDK and set `CCP_CLASSIC_PROOF_DIR`
  to this repository's `artifacts/proof` directory for rendered previews.

### M1 review stack

All seven PRs are drafts, each based on its predecessor. Review in order; each
contains fewer than 600 changed text lines. Retarget children before merging parents.

| PR | Change |
|---|---|
| [12](https://github.com/CC-Labs-it/ccp-classic/pull/12) | elapsed-time effects and native visual rendering |
| [13](https://github.com/CC-Labs-it/ccp-classic/pull/13) | passive Windows and local X11 panic listeners |
| [14](https://github.com/CC-Labs-it/ccp-classic/pull/14) | verified starter media and cancellable local audio |
| [15](https://github.com/CC-Labs-it/ccp-classic/pull/15) | transparent desktop hosts and native input regions |
| [16](https://github.com/CC-Labs-it/ccp-classic/pull/16) | connect the first playable session and shared panic routing |
| [17](https://github.com/CC-Labs-it/ccp-classic/pull/17) | review fixes for captured cue timing and continuous Gentle opacity |
| [18](https://github.com/CC-Labs-it/ccp-classic/pull/18) | review fixes for live audio, monitor selection and X11 parser parity |

Review the M1 first-playable stack and try a local session. M2 adds Mandatory
Videos: fullscreen/floating, Strict Lock, attention checks, length caps and curtain.

## Owner questions

1. D12: repository name `ccp-classic` is provisional until 2026-11-10.

## Approved production scope, 2026-11-03

The owner authorized continued development through production readiness with agents.
M2 videos; M3 remaining effects; M4 companion and triggers; M5 Library, presets and
progress; M6 local haptics; M7 native Loom and Windows wallpaper; M8 Linux parity,
installers and a test build. Work remains in this repository in small buildable slices.

Approved companion barks: Breathe, Relax, Focus, Listen, Deeper, using only the five
verified starter recordings. Trigger mappings: Relax to flash, Focus to whisper,
Deeper to spiral pulse. No additional companion wording is approved.

Approved achievement names, in the original v5.0.0 catalog order:
1. First Steps
2. Finding Focus
3. Steady Practice
4. Long Practice
5. Lasting Practice
6. Milestone
7. Rose Tint
8. Long Session
9. Daily Practice
10. Image Collector
11. Morning Session
12. Undivided Attention
13. Quiet Session
14. Staying With It
15. Spiral Focus
16. Counting Practice
17. Bubble Collector
18. Careful Typing
19. Quick Response
20. Try Again
21. Clear Minute
22. Corner Hit
23. Hello Again
24. Committed Session
25. Fresh Start
26. Full Focus
27. All Together

Approved replacements for four removed named-preset conditions: finish Gentle
between 06:00 and 09:00 local time; finish Daily without switching apps; finish
Gentle for at least 30 minutes; finish Deep with Strict Lock. All other original
conditions remain, including five consecutive correct bubble-count answers.
