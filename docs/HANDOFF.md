# CCP Classic handoff

## Scope

M0 is merged into main. M1 is complete in a draft review stack. The owner authorized
continued M2-M8 production work with agents on 2026-11-03; that work is active.

Source design: owner decisions and mockup dated 2026-11-03 in the CCP handoff.
CCP v7.2.2 was published as Latest on 2026-11-03, satisfying the build start gate.

## Visual polish continuation, 2026-11-03

The owner requested reuse of CCP's finished artwork and interactions after reviewing
the functional preview. Three agents and the coordinator implemented this pass:

- Original public application icon in the executable, owned windows and installer.
  The desktop-shortcut task is selected by default. Shared raised surfaces, hover
  glow/lift, press depth and page entrances preserve motion preferences.
- The existing animated logo keeps rendering while visible but inactive. Hidden,
  minimized, Gentle, Reduced motion and Performance gates remain in place.
- Three original CCP preset hero masks, animated card art and selected-state glow.
  Progress has an illustrated XP header, statistic cards and all 27 original badge
  images with the previously approved localized names and requirements.
- Native contextual question-mark help for all editors and panels. Thirteen public
  CCP HelpLoops were ported with their drawing kit, rather than recreating their
  illustrations or copying untracked local tutorial movies. All ten languages
  include the guide and help text. Illustrations are silent and never start effects.
- Library uses CCP's folder rail, thumbnail wall, selection overlays and preview
  interaction. Search and media filters work on bounded pages. Image decoding is
  bounded; selected videos preview muted at 1x; audio uses explicit Play and a real
  waveform from the approved offline helper. Closing or panic releases previews.

Provenance is recorded in `POLISH-ASSETS-CHROME.md`, `POLISH-ASSETS-PROGRESS.md`,
`POLISH-ASSETS-LIBRARY.md` and `POLISH-ASSETS-HELP.md`. Only pinned public client
resources were copied. There are no new packages or private assets in this pass.

Release verification: 575 tests passed, zero failures or skips, including the real
offline FFmpeg fixtures. `tests/CCP.Classic.Tests/TestResults/polish-release.trx`
records this run. Rendered reviews under `artifacts/proof` include
`studio-inactive-01.png`, `studio-inactive-02.png`, `presets-polished.png`,
`progress-polished.png`, `library-polished.png`, `help-guide.png` and the 13 help
scene stills. Review corrected badge framing, disabled-button contrast, source-stage
clipping and text opacity. The native Linux probe follow-up is separate from these
Windows results; desktop feel and actual device/listening checks still need owner use.

Windows `1.0.0-preview.4` was built from clean source
`1e6b651fa0e47a27293bd0c1fd215723befb9516`, final polish draft
[PR 75](https://github.com/CC-Labs-it/ccp-classic/pull/75). The final badge-framing
and preset-motion corrections passed their focused checks, 2 and 6 tests.
Packages are under `artifacts/packages`:

| File | Bytes | SHA-256 |
|---|---:|---|
| CCP.Classic-1.0.0-preview.4-win-x64-setup.exe | 187683322 | 15a7f1eb99ee3caea1dc1cd31c14017b64075d9f8ddbb1d467b20ed6ebad8e4c |
| CCP.Classic-1.0.0-preview.4-win-x64.zip | 219367606 | f47a7b0ea7ece67290f4c72138d6b97cf0509ca907dbf2c424652cc6e1f53db7 |
| CCP.Classic-1.0.0-preview.4-analysis-sources.zip | 2258080548 | cc6f48c1c9e03e7aa491b9da9cc7086594640ac903e448e71d115af56d6c390f |

All adjacent checksum files match. The installer and packaged application each
contain the five original ICO image payloads byte for byte. Package metadata:
dirty=false, signed=false, selfContained=true, ownerLocalReview=false,
analysisSourceBundleComplete=true, externalDistributionBlocked=false. Nothing was
installed, launched or published. Do not call this unsigned preview a signed release.

Final polish [standard CI](https://github.com/CC-Labs-it/ccp-classic/actions/runs/41651300704)
passed on Windows and Linux. Native Linux follow-up at the same revision passed
screen discovery, refusal without a compositor, real transparency/input regions,
and global panic/keyword input. Its muted-video completion check still needs
diagnosis; PR 76 adds probe-only measurements without changing the application.

Post-preview hardening: a LibVLC callback can fail after a healthy worker Refresh
but before the completion check. Audio and video workers now reuse a single
completion snapshot and revalidate terminal errors before release. Deterministic
barrier regressions preserve the original failure message and verify disposal.
The audio follow-up passed 576 complete Windows Release checks including native
FFmpeg; the matching video fix then passed 40 focused playback/window/preview
checks. Library test cleanup now waits for canceled thumbnail readers to release
their handle. The device pulse test uses its existing fake clock to prove expiry.
These fixes do not change the already-built preview.4 artifacts listed above.

## Implemented

- Production continuation: local Library folder pickers and file checkboxes, bounded
  recursive indexing and a 12-image lazy cache. Files remain in their selected folders.
- Local video decoding at 1x, fullscreen/floating owned windows, Strict Lock, count
  attention checks, actual-playback caps and continuous Gentle entrances and exits.
  Loading stays hidden, and panic cancels both pending players and visible windows.
- Bubble Pop, Bouncing Text, Lock Card, Mind Wipe and Brain Drain state models and
  native drawing/windows. Mind Wipe uses tracked local audio, approved fallback
  clips and bounded looping. Bubble sound is a soft procedural non-speech pop.
- Gentle, Daily, Deep and atomic local user presets. Native save/load/delete controls
  and the header picker preserve language, panic, startup, folders and audio output.
- Offline stats, the original v1 XP curve, live Progress and all 27 approved neutral
  achievement rules. Classic awards 1 XP per 5 seconds, 2 per popped bubble,
  50 per finished video and 10 per correct attention answer. There are no unlock gates.
- The verified original companion tube and five approved recorded barks are wired.
  Timed phrases and opted-in keyword capture feed bounded native effect triggers;
  the Wayland fallback accepts text only inside Classic. Panic clears every queue.
- M6: explicit literal-loopback Intiface spec4 and Lovense connections, six bounded
  patterns, event controls, global gain and shared Stop. Audio sync samples real
  LibVLC source positions and effective gain every 100 ms. An expired sample goes
  to zero after 300 ms; decoding failures never substitute a synthetic signal.
- M7: native Loom editor, twelve saved spirals, live custom playback and preset
  selection. One shared worker supplies bounded frames to all selected monitors;
  reduced motion freezes the pose and Gentle visuals reduce animation and glow.
- M7: Windows wallpaper recovery journal, opaque transition covers, static restore,
  muted 1x dynamic playback and covered/battery/motion gates. Recovery and cancellation
  are tested with fake native backends. No real wallpaper changes were made.
- M8: explicit startup registration, recoverable hidden launch and one-shot AutoRun.
  Closing the owner stops the session and awaits bounded toy/wallpaper cleanup.
  Unsupported overlays fall back to a normal owned window. Isolated Linux probes
  cover X11 input, transparency, silent playback, analysis and compositor refusal.

- Current CCP image mosaic with twelve perimeter tiles and the native animated logo
  in its central 2x2 area. The owner replaced the flat card mockup on 2026-11-03.
- Compact 724 x 760 default window, two-row header. The owner requested a nearly
  square window fitted around the grid, avoiding empty sides.
- Studio, Presets and Progress. Tile editors and Library, Companion, Toys and
  Settings open in owned native windows. Panic reaches both owner and child.
- Configurable in-app panic, held-key suppression, shared manual Stop policy.
  Disabling panic requires the visible red warning and resets on the next launch.
- Gentle visuals, reduced motion and performance settings quiet the animated logo.
  Hidden and minimized surfaces do not render animation frames. Per the owner's
  2026-11-03 correction, visible inactive windows keep animating.
- Local atomic settings saves, export, recovery notice, normalized panic bindings.
- All ten embedded language files, exact key parity, UTF-8 without BOM, neutral
  chrome checks. No account, browser, updater, telemetry or external networking.
  Literal-loopback toy providers are the only runtime networking boundary.
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
- Production features are integrated. Final Release validation, native Linux CI and
  installer verification are in progress. Actual device feel, wallpaper behavior on
  the owner's desktop and listening remain manual release checks. No real toy
  connections, audible playback or operating-system startup changes were made.
- LibVLCSharp 3.10.1 supplies the cross-platform recorded-audio backend; NAudio is
  not required by this implementation. Linux needs LibVLC plus audio/codec plugins.
  Windows ships 3.0.24 natives. Missing audio reports status while visuals continue.

## Validation

Latest integrated `dotnet test -c Release -m:4 --no-restore --disable-build-servers`:
Latest fully green integrated checkpoint: 554 tests passed, zero failures/skips,
including the approved pinned FFmpeg helper and known delayed-media fixtures.
The suite covers the existing
48 M0 tests plus M1 scheduling, real starter
hashes, native drawing pixels, media cancellation, input mappings/rebinding,
native/local event ordering and a composed first-playable frame with no stop ghosts.
The 26 review regressions cover live Gentle transitions, active-recording gain/mute,
stop/restart ordering, monitor host reconciliation and local-display parser parity.
Windows and Linux CI run for each PR and retrieve the starter recordings through LFS.
The Windows native offscreen probe confirmed real transparency, input pass-through,
global Esc listener availability and clean disposal without audio or synthetic keys.
Muted native probes decoded exact 320 x 180 video frames at rate 1 and verified
WAV/MP3 completion durations, panic cancellation and cleanup without sound/windows.
Production PRs #19-#35 passed Windows and Linux CI. New slices continue the linear stack.
Windows preview.3 ZIP and Inno installer were built from clean e59313b after all
554 tests passed. The 2,258,080,548-byte corresponding-source ZIP is complete;
all checksums were verified. Package metadata records self-contained=true,
dirty=false, ownerLocalReview=false, analysisSourceBundleComplete=true and
externalDistributionBlocked=false. The installer is unsigned. No installer was
run, no release was published and no real desktop playback was started.
Earlier preview.1/preview.2 packages are superseded by preview.3.
Standard CI for final source passed on Windows and Linux. The extra isolated
Linux desktop probe failed during setup; diagnostics are being reviewed.
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

The owner can test `artifacts/packages/CCP.Classic-1.0.0-preview.3-win-x64-setup.exe`
or the portable ZIP in the same directory. The installer is 174,661,141 bytes,
SHA256 `c63170e883c1064237d0fa4909e2669798a3b91309a6c4ad584dd5ff5e51b419`.
The portable ZIP is 206,430,004 bytes,
SHA256 `96100801eaa94bd135c365d4732f0d1b277180f861a1e192232c81b670a8d125`.
The separate analysis-sources ZIP has SHA256
`45c27fb9f8dc2b9d8bd8238b70a67d8326fbe5f7d02a90c01123c0f9d098553c`.
All files have adjacent checksum files. These local build outputs are ignored by git.

### Conversation checkpoint, 2026-11-03

- Current integration branch: `codex/production-45-preview-packages`. The original
  tube, native keywords and companion runtime are committed, followed by native
  Loom model/renderer/translations, timed preset storage and owner-exit fixes.
- Review fixed app exit under NoPanic/strict children using OwnerWindowOnly, exact
  bubble geometry and 16 input regions, separate cancellable bark/pop audio lanes,
  video/companion frame cadence, and correct normal completion of Until stopped.
  Explicit Stop completes an untimed run; panic, child dismissal and app close
  remain interruptions, preserving the original three-hour completion condition.
- Startup registration and tray/reset UI are ready; hidden startup applies only
  to an idle system launch, and a session restores the panel for owned windows.
  Local device providers/mixer and wallpaper services use injected native tests.
  Audio-sync PCM metering is still under native timestamp alignment investigation.
  The owner approved an offline FFmpeg analysis helper after the prototype preserved
  the intentional 400 ms delay. Playback stays LibVLC; helper bundling is in progress.
  No actual hardware connections, startup registration or wallpaper writes were sent.
- All six M1 review findings are fixed in PRs #17 and #18. Three agents implemented
  and cross-reviewed the fixes. Both new PRs passed Windows and Linux Release CI.
  The complete suite passed 148 tests, with zero failures or skips, including 26 new cases.
- M0 PRs #1-#11 are merged. M1 PRs #12-#18 remain drafts in a linear stack.
  The owner authorized continued production work and agent delegation on 2026-11-03.
  M2 and the remaining milestones are now in scope. All PR titles and descriptions
  reflect the seven-part stack. Each slice remains below 600 changed text lines.
- Latest request: keep building until production ready, using agents. Continue
  independent work; approvals for the companion words and 27 badges are recorded below.
  Native desktop feel and actual listening remain manual checks. The app was closed
  gracefully for builds; no unexpected desktop playback or hardware commands were sent.
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

Continue companion/global keyword integration, local haptics, native Loom, Windows
wallpaper, startup behavior and installers. Keep cross-platform CI and the small
review stack current. The owner-approved tube GIFs are being verified from public
source; never substitute private assets or new companion identity/copy.

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
