# CCP Classic handoff

## Companion bark library, 2026-11-03 (fix/tube-barks)

Owner asked for every gender-neutral Circe line from CCP Default, tied to real actions.
285 lines (108 rules, 18.3 MB of LFS mp3s in Starter/Barks) curated from CCP's 702; event map,
selection rules and drop reasons in docs/BARKS.md. Core `BarkEngine` (priority, conditions,
per-rule cooldown, once per session, chance, global gap min(45 s, Bark interval), no line twice
in a row, 30 s panic quiet) + `BarkSwitchWatch` (player switches turned on). The idle timer and
clicks draw from the library; session timing words stay the approved five. Panic routes call
`SilenceBarks()`; a stop never speaks; only a preset's timed end gets a closing line.
`DesktopEffectsService.LineSpoken(RuleId, LineId, Mood, Text, Seconds)` is the seam for the
emote controller lane (fix/tube-emotes); barks never pick an animation clip.
CompanionWindow change is one line (approval via `CompanionTimeline.IsApproved`, smaller font
for long lines). No new loc keys. 852 Release tests pass. Not desk-run: hear the mix, the
chatter rate and the bubble fit in the app before shipping.

## Tube emotes at CCP parity, 2026-11-03 (fix/tube-emotes)

Owner: animations 20% slower, no talking on every trigger, idle animations more often, full
parity with CCP. Before: `CompanionWindow.Update` played `talkA` for every visible bark (idle
interval words, click pokes and session timed phrases, which are also the effect triggers), the
greeting for 7.2 s after a click, and otherwise looped `idle` forever at the GIFs' 60 ms frames.
Now `Core/CompanionEmotes.cs` ports CCP's `AvatarTubeWindow.CirceEmotes.cs` as a pure planner
(`CompanionEmoteDirector`) over the trimmed `Assets/Companion/emotes.json` + `clip_timing.json`:
each clip plays once, weighted idle rotation never repeats the clip on screen, a spoken line plays
1 to 3 talk clips by length then one reaction (stem, mood, random expressive), non-verbal sounds and
single trigger words play one expressive clip from idle (never talk), a click plays a click emote
(3 s cooldown, 2 s hold). The window crossfades on two layers (fadeMs 1 s, Gentle 1.5 s) once the
new clip has a frame. Rate: 16.7 fps (7.2 s clips) before, 13.3 fps (9.0 s) now
(`CompanionEmoteDirector.PlaybackRate` 0.8). Gentle and Reduced motion still only still the bob,
as before. Bark hook for fix/tube-barks: `CompanionHost.SpeakLine(lineId, mood, text, seconds)`,
plus `NonVerbal()` and `TriggerWord()` (keyword triggers are not wired to it yet: that lives in
DesktopEffectsService.Companion.cs). 24 clips added (50.3 MB, LFS); provenance in
docs/ASSET-PROVENANCE.md. Not packaged or launched; the owner judges the feel.

## Tube right-click menu, 2026-11-03 (fix/tube-menu)

Owner: "nothing happens clicking right click on the avatar". Right-click on the tube's hit box
now opens CCP's AvatarContextMenu look (`Companion/CompanionMenu.cs`: #121220 body, 2 px pink
rim, radius 8, #3D3D60 hover, pink separators, green Start, red Stop/barks off). Items:
Companion settings (pink, bold), Detach / Attach to main window, Grow, Shrink, Reset size,
Always on top (detached only), Hide avatar, Start/Stop session, Barks check, Show main window.
Placement, size and barks apply at once and save like Ctrl+wheel (whole settings file, as
PlacementChanged did). Hide sets Avatar off and saves; the Companion window's Avatar switch
brings her back (status line says so). Start/Stop go to MainWindow (same Start as the button;
Stop = tray Stop through the panic gate, shown disabled while panic is off). Right-click no
longer stops a session by itself (it used to call Stop silently); closing the tube still does.
Dropped from CCP: Talk to, Trigger mode, Takeover, Personality, remote emotes, Rain wardrobe,
chat history, mute whispers, pause browser (AI, mods, remote, browser: none exist in Classic).
Wiring avoids DesktopEffectsService.Companion.cs: host events surface through the new
DesktopEffectsService.CompanionMenu.cs partial. Not yet seen on a real desktop or X11.

## Tube fixes after the owner's beta.4 look, 2026-11-03 (feat/companion-tube)

Owner saw a still avatar, a tube too big with a gap to the panel, clicks that did
nothing and no barks. Causes and fixes (lanes fix/tube-geometry + fix/tube-motion):
- Size follows the panel's visible height (art = 38% of it, `TubeLayout.Scale`), not the
  monitor; Companion size and Ctrl+wheel multiply it, docked too.
- The gap was Windows' invisible resize borders: docking uses the DWM visible frame
  (`PanelFrame`), art overlaps it by 1 DIP.
- Gentle froze the clip: Gentle now slows the idle loop, only reduced motion stills it.
- Barks were session only (timeline, audio gate and default all off outside one):
  Barks default ON, idle barks between sessions, a click barks at once (1.5 s cooldown).
840 Release tests pass. NOT packaged: owner tests an unpackaged build first.

## 1.0.0-beta.4 draft release, 2026-11-03

GitHub pre-release v1.0.0-beta.4 (tag at 3f8fc45 on feat/companion-tube) is a DRAFT,
waiting for the owner's go; the briefly public v1.0.0-beta.3 release and tag were deleted
(repo had 0 watchers). Assets: setup.exe, win-x64.zip, linux-x64.tar.gz, SHA256SUMS + .asc
(GPG key 54D3 23FB 3798 AFE1 81F4 003E 910D 8162 E51F 4553), analysis-sources split in
.001/.002 (GitHub's 2 GiB asset cap). Every asset digest matches the local file. Notes in
artifacts/release-v1.0.0-beta.4/NOTES.md, in the owner's voice; the Discord post text the
owner posts by hand is on their Desktop (ccp-classic-discord-post.txt).
834 Release tests passed on Windows and Ubuntu 24.04; app exe and installer Valid
Authenticode, timestamped. Headless WSL proof on the Linux tarball: tube docked left of the
panel at first launch with no session, follows a moved panel, hides on minimise and returns,
docks mirrored on the right at the screen's left edge, stays through a session and panic.
3f8fc45 also keeps a docked tube off topmost (CCP parity); Always on top is for detached.
SHA256: linux bd9c9261..., setup 0e670de4..., zip b85c09c8... (full list in SHA256SUMS).

## Companion tube, 2026-11-03 (branch feat/companion-tube)

Owner expected CCP's look: the avatar inside CCP's glass tube, docked beside the main
window and present whenever the app is open. Avatar is now on by default (barks stay
off). `Companion/CompanionHost` keeps the tube while Avatar is on: idle frames at about
20 fps while motion is allowed (still frame otherwise), session frames and barks from
the effects tick. A stop or panic ends barks and talk at once and returns the tube to
idle; turning Avatar off or closing the app releases it. Window size comes from the
panel: the painted art (canvas y 380 to 923) is `TubeLayout.ArtToPanelHeight` (0.45) of
main's visible height, clamped 0.35 to 1.0, times Companion size (0.5 to 1.5, Ctrl+wheel,
docked or detached), never taller than the work area; an 800 DIP panel gives 0.663
(window 676 DIP, art 360, avatar 203). Attached docking is the pure `TubeLayout.Dock`
against main's VISIBLE frame (`PanelFrame`: DWM extended frame bounds on Windows, whose
Position/FrameSize include about 7 px invisible borders; Position + FrameSize elsewhere);
the pipes overlap that edge by 1 DIP, no daylight. It re-runs on main's PositionChanged and
size changes. The window
shows only the art's band of the 780 x 1020 canvas: Avalonia windows take clicks on
transparent pixels, so a full-width canvas would cover the left of the main window.
Existing settings files keep their saved Avatar value; only fresh profiles and
Reset get the new default. Headless visual check and X11 docking still owed.

## 1.0.0-beta.3: Linux audio output picker, 2026-11-03

Owner asked for output selection on Linux (Windows already listed DirectSound devices).
VLC3's instance-level AudioOutputDevices("pulse") is always empty; a MediaPlayer's
AudioOutputDeviceEnum reports the server's sinks at once (WSLg: "RDP Sink"). Commit
a77649b lists Linux outputs from the player, sanitized like Configure, with a test.
Headless proof on the beta.3 tarball, two PulseAudio null sinks: picking Headphones
sent Test audio only there (-15.5 dB, other sink -91 dB) and picking Null Output
reversed it; after the main Save and a restart the choice held (settings.json
OutputDeviceId "headphones", sound on that sink). Effect editor changes reach disk
only on the main Save, on every platform; unchanged here.
Known gaps: returning to System default on Linux applies from the next sound; .NET
returns "" for LocalApplicationData when ~/.local/share does not exist, so settings
then land in a "CCP Classic" folder under the working directory (seen in a bare test
profile; normal desktops have the folder). PipeWire not tested.

Built from clean a77649b, 826 Release tests passed on Windows and Ubuntu 24.04.
App exe and installer: Valid Authenticode, timestamped, CN=CC Labs Srls.
- CCP.Classic-1.0.0-beta.3-linux-x64.tar.gz (83,285,161 bytes)
  e17b42c099e8f3bafeb4109836f78be4543a658f9f51d6ff58a19c9120dc3a12
- CCP.Classic-1.0.0-beta.3-win-x64-setup.exe (200,993,016 bytes)
  f2ebb4e5e74596c59ccb7ca9f6e89a4792434dd7479f873e6c890fa939094b29
- CCP.Classic-1.0.0-beta.3-win-x64.zip (232,743,307 bytes)
  2f54dcba5c4da3d6ac586b84ca9c13be08884535babf41458a54877a4e44f7f9
- plus the matching analysis-sources ZIP. Nothing published or pushed.

## 1.0.0-beta.2: Linux audio fix, 2026-11-03

A headless test of the Linux 1.0.0-beta tarball (WSL Ubuntu 24.04, private Xvfb,
picom, openbox, PulseAudio null sink, xdotool) found every sound silent: preview,
Test audio and sessions. Cause: System default sent "" to SetOutputDevice; VLC3's
PulseAudio output uses it as a sink name, the server refuses the stream and VLC
falls through to a silent ALSA output. Reproduced with the system libvlc directly:
no device = sound, "" = silence, explicit sink = sound. Fix e477527: Linux leaves
the device unset for System default; Windows keeps the DirectSound reset. Not
fixed: VLC's pulse enumeration still lists no Linux outputs, so the picker shows
only System default. PipeWire was not tested.

Built from clean de3a096 (dirty=false), 825 Release tests passed on both Windows
and Ubuntu 24.04 (six opt-in skipped). Linux was built natively in WSL; Windows was
built locally with -SignWindows (Git Bash's tar breaks analysis-helper.ps1: put
System32 first on PATH). App exe and installer: Valid Authenticode, timestamped,
CN=CC Labs Srls. Files in artifacts/packages, all checksums verified:
- CCP.Classic-1.0.0-beta.2-linux-x64.tar.gz (83,286,318 bytes)
  f1959e8cd8b5aeb1af016eb71f8fda3c12cc485e982391e3fe50e54e1e1e55e8
- CCP.Classic-1.0.0-beta.2-win-x64-setup.exe (200,993,504 bytes)
  572cd46ce5b304b132c60f090f28d4fc95e0babed6f56f0eb4d3f892630e6379
- CCP.Classic-1.0.0-beta.2-win-x64.zip (232,743,083 bytes)
  aa5e5656a30673d155d4c2933480cb7d2ddd820870da76cfab38dd919914d159
- plus the matching analysis-sources ZIP.
After the fix the headless rerun measured sound on all three paths: Test audio
-15.4 dB peak, focus.mp3 preview -16.2 dB, a 20 s Daily session -11.4 dB (all
-91 dB before). Nothing was published or pushed; the 1.0.0-beta files are kept.

## Linux 1.0.0 beta ready, 2026-11-03

Linux x64 package: artifacts/packages/CCP.Classic-1.0.0-beta-linux-x64.tar.gz
(83,317,166 bytes), SHA256
01691bd19ea2ec2974cfef7121c9431c0b3e8d5c65fda664f9b154b8ba9b4b35.
Built on Ubuntu 24.04 from clean 159d5765c61250cd610e66abe4c9572ec57909c8,
branch codex/release-100-beta-package. This commits the same app changes already
in the signed Windows beta; no application source changed for the Linux build.
Workflow https://github.com/CC-Labs-it/ccp-classic/actions/runs/41696399389 passed:
825 Release tests, zero failures (six opt-in tests skipped), plus the isolated
native Linux desktop/media probe. Evidence: artifacts/ci-beta-linux.
Verified archive SHA256, version/runtime/source metadata, 318 files and executable
flags for launcher, app and native graphics libraries. Both beta packages are now
local under artifacts/packages; neither has been published as a release.
Linux users extract and run CCP.Classic, with dependencies from docs/INSTALL.md.
Full desktop effects need a local composited X11 session; Wallpaper is Windows-only.

## Signed Windows 1.0.0 beta, 2026-11-03

Owner accepted the new menus and requested the beta package. Built
artifacts/packages/CCP.Classic-1.0.0-beta-win-x64-setup.exe (200,992,888 bytes).
SHA256: caed355909dcd252528b1edaecfb33688007b5d592a6a02c33e645753f8c1f25.
Both CCP.Classic.App.exe and the installer have Valid Authenticode signatures
with timestamps, publisher CC Labs Srls. All 759 portable files match staging,
and all three artifact checksums pass. Matching portable and complete analysis
source ZIPs are in artifacts/packages. No app was installed/launched or release
published. The current source remains uncommitted; metadata records dirty=true.
Linux was built separately afterward; see the Linux beta checkpoint above.

Packaging now supports -SignWindows. It signs the app before archiving/Inno,
then signs the installer and calculates final checksums. The helper reads the
user-scoped ACTALIS_PW without logging it and rejects invalid or untimestamped
signatures. App behavior is unchanged since the 825-pass menu validation.

## Menu clarity pass, 2026-11-03

Implemented in the working tree after preview.8. Every editable menu option now
has hover help and a keyboard-accessible ? flyout, including dynamic phrase/audio
rows, Library, Presets, Companion, Toys and Loom. Added 150 translated help families
plus 21 Loom help keys in all ten languages. Context-specific descriptions separate
bubble motion, wallpaper mode and toy event routing.

Editors use soft gradient section bands and paired controls when width permits;
narrow windows fall back to one column. Flash uses General, Motion and Utility,
with Stay until popped beside linked audio and GIF remix beside Hydra timing.
More now keeps advanced details while useful settings remain visible, including
Audio output, Brain Drain, Companion placement and common toy sync controls.
The old three-primary-control rule is superseded in AGENTS.md.

Validation: 825 passed, zero failures, six opt-in native-media tests skipped in the
full Windows Release run (831 total). Result: artifacts/test-results/menu-polish-release.trx.
New checks cover responsive layout, help not changing settings, and menu help
coverage in all ten languages. Reviewed native offscreen proofs for compact,
expanded and narrow Flash, Settings, Video, Companion and Toys under
artifacts/proof/menu-polish/menu-*.png. No desktop app or effects were launched.

Updated installer built for owner testing: artifacts/packages/CCP.Classic-1.0.0-preview.9-win-x64-setup.exe
(200,977,511 bytes), SHA256 bbd494c93015f031b98628d3744367b59ac21c470836331fc35e6e30be656be5.
The matching portable ZIP and complete analysis-source ZIP are in the same folder.
All 759 portable payload files match staging; all three checksums were independently
verified. This is an unsigned local review build from the working tree (dirty=true,
ownerLocalReview=true). It was not installed, launched or published. Preview.8 is retained.


## Preview.8 parity checkpoint, 2026-11-03

Final preview.8 validation checkpoint: all 828 Windows Release tests pass with
zero skips in `artifacts/test-results/parity-preview8-release.trx`. This includes
the real silent layered-audio service, WMA, FFmpeg analysis, native video timing,
desktop capture, source renders and deterministic input-reconnect regressions.
The extended Linux probe builds with zero warnings/errors and now includes eight
simultaneous looping audio tracks, live gains, cancellation and cache cleanup.
Preview.8 is built from clean `37064c8bcb29b06f9e8977a9b3b9902c4130578d`.
Independent package verification passed: all 759 archive files match the staged
payload; installer/app icons, CC labs Srls branding/license, original media,
native dependencies and complete analysis-source evidence are correct. Standard
Windows and Ubuntu CI passed. The extended Linux native/package run also passed,
including eight looping WAV/WMA layers through one output, live gains, 2,765,520
bytes of bounded PCM, 20.91 ms stop and an empty cache. Downloaded proof is under
`artifacts/ci-preview8-linux`; exact CI links are in Scope below.

Installer: `artifacts/packages/CCP.Classic-1.0.0-preview.8-win-x64-setup.exe`
(200,925,660 bytes), SHA256
`5c17e702442a1efcb64ac564d7c425446aa42a49a9f043c9512606e5b6875168`.
Portable ZIP: 232,684,870 bytes, SHA256
`2080a8819cdecf50bbb270d14436c5e95ca60e8c5ad23b53d986e6d8b52bc9e8`.
Analysis-source ZIP: 2,258,080,548 bytes, SHA256
`a1c72d9ce60807702fcba30a93efeb7707a99b524eb1d3626aca890e7474d84f`.
All three independently computed hashes match their adjacent `.sha256` files.

Owner requested full behavior/customization parity for the existing features, using
public CCP source and assets. Typed input only: microphone and OCR are excluded.
Source is pinned at public `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
Do not use the original repo's current working tree as implementation input: it is
different from that pin. Read pinned files with `git show`; cached reference files
are in ignored `artifacts/parity-source`. See `docs/PARITY-REVIEW.md` for gaps.

Current branch: `codex/parity-68-recorded-bubble-sounds`. Existing-feature parity
checkpoint `902f32b9fdccbf036f09805d48c23c8dc31605bf` is pushed and packaged as preview.7.
Preview.6 does not contain these changes. All 774 Windows Release tests pass,
with zero skips (`artifacts/test-results/parity-release-verified.trx`), including
native playback, selected/default output identity, actual desktop capture and
offline FFmpeg analysis. Windows CI and the Linux full/native/package pipeline
passed. The first standard Ubuntu job exposed a fake-clock test scheduling race;
test-only fix `7c8b2bf` is pushed and passed standard Windows and Ubuntu CI:
https://github.com/CC-Labs-it/ccp-classic/actions/runs/41677176398

Implemented: original recorded bubble pops and effect faces, flash-in-bubble,
click outros, flash movement/drag/fling/pendulum/shatter, audio-held batches and
linked Hydra, original native Remix compositions, native GIF timing, original
Bouncing Text motion/font/colors/two lines/FX and wall-hit rewards, real Windows
Brain Drain capture/blur/melt, configurable typed lock cards, editable typed trigger
rules, phrase/audio mapping, GIF spiral libraries/corners, source video targets and
floating windows, output selection, source audio DSP, per-device toy routing,
FunScript/luminance/band split/temperaments, avatar placement, hierarchical Library
selection presets and native animated wallpaper collages. All ten languages carry
the controls. Feature presets capture the options, including wallpaper.

Stop, owned-window closure, motion preferences, local-only media and bounded
resource use remain authoritative. Video tests share the normal capacity pool.
Finished hidden video views retire their reservations. Library edits now apply to
future video launches immediately; currently playing videos retain their captured
path. Image selection changes remove old flashes and image-bearing bubble payloads,
cancel prepared Remix content and rebuild the selected cache. Folder changes and
Refresh trigger cancellable rescans. Applying a preset closes stale settings editors.

The native render proofs include `remix-native.png`,
`wallpaper-moving-collage-public.png`, `video-attention-parity.png`, original bubble
faces/pop physics and all five image outros under `artifacts/proof`.
Real desktop-capture tests keep their tiny buffers in memory, never saving the screen.
No toy hardware was connected. Desktop wallpaper and the installed application
were not changed. The native audio check plays the approved recording at zero gain.

Post-preview.7 checkpoints are not in that installer:

- `eaa5253`: multi-monitor Bouncing Text crosses the selected desktop union with
  mixed-DPI projection and outer-wall rewards. Geometry, motion, native seam render
  and standard Windows/Ubuntu CI pass.
- `2df6735` through `cedc665`: consistent local audio formats including verified
  ASF/WMA, a helper-exit monitoring race fix, bounded Intiface v4 receive/subscription
  handling and optional button attention. All 37 focused Release checks pass,
  including actual muted WMA playback and analysis. Button timestamps preserve the
  physical edge; hidden-period and old-generation presses cannot hit later targets.
  Linux native/package run 41680210118 passed, including actual WMA playback and
  analysis. Standard CI exposed an input subscription race under device reuse;
  its generation guards and stop-resume correction pass the final full suite.
- `2010037`: bounded PCM storage, sample-domain looping/mixing and pacing. Six
  focused checks pass, including tiny-loop caching, starvation recovery, gain
  changes and owned-cache cleanup.
- `c06cd8a` and `c6dce23`: native layered playback, one output, latest-wins control
  worker, editor, ten-language help, captured audio-only session mode and shared
  stop/reset/close integration. The focused integrated run passed 46/46. The
  first complete native run exposed the Intiface stop-resume race. Checkpoint
  `0ed3e63` fixes it and the final run passes all 828 cases. Native playback has full
  16,317/148,617-frame tails, live gains, decoder pause/resume and cache cleanup.

The native layered prototype under ignored `artifacts/audio-layers-probe` proved
complete direct callback decoding at 4x with a 176400 Hz callback format and a
separate 44100 Hz/1x output. Repeated decoder pause/resume preserves the tail.
The output read-ahead remains bounded at 120 ms. Fast transcode/smem loses its last
packet and must not replace this path. Microphone, OCR and camera/gaze remain
outside scope. Brain Drain is explicitly Windows only.

Original neutral public spiral GIFs were copied, without overwriting other files,
to the user's existing image folder `C:/Users/Admin/Documents/ShareX/Backup` as
`CCP-Classic-Test-Spiral.gif` and `CCP-Classic-Test-Corner-Spiral.gif`. Actual profile
settings were not changed. Use literal profile paths when inspecting them because
the sandbox LOCALAPPDATA points to a different profile.

Agents completed native audio evidence, integration review, editor, lifecycle
tests, translations, Intiface fixes, extended Linux probe and independent package
verification. Cross-platform CI and packaging are complete. The installer remains
an unsigned preview; physical toy hardware and owner interaction are manual checks.

## Scope

M0 is merged into main. M1 is complete in a draft review stack. The owner authorized
continued M2-M8 production work with agents on 2026-11-03; that work is active.

Source design: owner decisions and mockup dated 2026-11-03 in the CCP handoff.
CCP v7.2.2 was published as Latest on 2026-11-03, satisfying the build start gate.

Latest local installer: `artifacts/packages/CCP.Classic-1.0.0-preview.8-win-x64-setup.exe`.
It includes the integrated parity ports and all post-preview.7 additions above,
built from clean `37064c8bcb29b06f9e8977a9b3b9902c4130578d`.
All 828 Windows Release tests passed, including native layered audio, silent
video pacing/caps and offline FFmpeg analysis, with no skips.
Installer and portable ZIP checksums were verified. Publisher and current license
both read CC labs Srls. The exact analysis source bundle accompanies the binaries.
Build/test CI: https://github.com/CC-Labs-it/ccp-classic/actions/runs/41683094087
Linux native/package CI: https://github.com/CC-Labs-it/ccp-classic/actions/runs/41683113049
The installer remains unsigned and has not been installed or published by agents.
Prior playback source PR 79 passed standard CI on both Windows and Ubuntu. Its
isolated Linux native acceptance passed every stage, including the live video cap.
Earlier preview.6 work used the draft stack PRs 81-84. The active parity branch
and exact preview.8 source are recorded above; those earlier PRs are historical.

Preview.6 artifacts, all adjacent SHA-256 files verified:

| File | Bytes | SHA-256 |
|---|---:|---|
| CCP.Classic-1.0.0-preview.6-win-x64-setup.exe | 187861602 | 1c5c58bed0c483a18da09e04a4afa5e665a20b3e365e1bbf60683a35c1802192 |
| CCP.Classic-1.0.0-preview.6-win-x64.zip | 219545158 | 2f7b70cbda48e99d3209388450e6026686e0d4bcf49cbbe737d77fdc4f61e5df |
| CCP.Classic-1.0.0-preview.6-analysis-sources.zip | 2258080548 | 1df33426c2f94415acf9cc4f14fc3c88a1c92d8fe1397949b4ad461edba7991a |

Package company, installer and application version resources read `CC labs Srls`.
The packaged MIT license matches the repository byte for byte. All five original
icon payloads match in the application and installer. Metadata: dirty=false,
signed=false, selfContained=true, ownerLocalReview=false,
analysisSourceBundleComplete=true, externalDistributionBlocked=false.

## Visual polish continuation, 2026-11-03

Owner follow-up: current copyright, publisher and all ten app credits now read
`CC labs Srls`. Editors fit their contents and expand for More, bounded by the
monitor work area. Bundled Inter, larger pink headings, lavender labels and cyan
values replace the small white hierarchy. The main CCP support wording opens
`https://www.patreon.com/CodeBambi` through the system browser only on a click.
The branding/layout checkpoint passed 17 focused Release checks plus the new
compact/expanded editor regression. Stable visual evidence is in
`artifacts/proof/flash-settings-compact.png` and `studio-support.png`.
The subsequent effect parity request is also implemented. Image clicks now use
CCP's original Pop, TV off, Spiral, Melt and Glitch exits, with nonrepeating Mix
as the default. Timed expiry keeps its fade. Gentle and Reduced motion simplify
the exits, and completed exits release pointer input.

Effect bubbles reuse CCP's public glass artwork, payload colors and symbols.
Flash bubbles preview the exact image delivered on pop. Mixed bubbles select
eligible enabled effects; More controls their 10% default chance and type.
The pool stays bounded to eight bubbles, at most four carrying effects. Video
payloads require available local nonstrict floating playback and stop at 30 seconds.
Normal scheduling, panic cleanup and saved effect settings remain intact.

The combined source passes all 599 Windows Release tests with no skips, including
native video and offline analysis. `preview6-release.trx` records this run.
Rendered `bubble-faces.png` and all five `flash-outro-*.png` were reviewed.
Source provenance is in `POLISH-ASSETS-BUBBLES.md` and `POLISH-ASSETS-EFFECTS.md`.
These changes are included in the verified `1.0.0-preview.6` installer above.

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
and global panic/keyword input. Its muted-video completion check failed; PR 76
added probe-only measurements. The diagnosed timing issue is fixed and the final
native acceptance below passes.

Post-preview hardening: a LibVLC callback can fail after a healthy worker Refresh
but before the completion check. Audio and video workers now reuse a single
completion snapshot and revalidate terminal errors before release. Deterministic
barrier regressions preserve the original failure message and verify disposal.
The audio follow-up passed 576 complete Windows Release checks including native
FFmpeg; the matching video fix then passed 40 focused playback/window/preview
checks. Library test cleanup now waits for canceled thumbnail readers to release
their handle. The device pulse test uses its existing fake clock to prove expiry.
These fixes do not change the already-built preview.4 artifacts listed above.

The native Linux timing trail proved that all 29 colored frames presented over
2.112 seconds at 1x while LibVLC's source position stayed at 0.25 seconds. Video
allowance now combines the existing source clock with an independent, bounded
Display-frame clock. Loading, pauses, seek discontinuities and stalled gaps do
not earn frame credit; audio metering retains the raw source position. Published
playback time is capped, without trusting duration metadata. Very low frame rates
below 1 fps or long observer delays may conservatively undercount fallback time.
The timing fix passed 46 focused Windows checks, including real silent native
two-second playback and a one-second live cap. The same native cap is now checked
in the isolated Linux probe. Dispatch can select Linux only for diagnostics.

Final local `1.0.0-preview.5` artifacts, all adjacent checksums verified:

| File | Bytes | SHA-256 |
|---|---:|---|
| CCP.Classic-1.0.0-preview.5-win-x64-setup.exe | 187684251 | e2bfe9e1fab5e92141a287ca70ad1aecc6174f31e8908fc46121e8e749f637c3 |
| CCP.Classic-1.0.0-preview.5-win-x64.zip | 219368440 | d891e5c311691432a6c82ae1fc0e605d8d0da647344d3d988517fb4ba32c460e |
| CCP.Classic-1.0.0-preview.5-analysis-sources.zip | 2258080548 | 2e2207ef7e0ed7383d55a2a702b97ea0129a548098caaea77e515b681c7ddd44 |

This package is from clean `19d97b4`, [PR 79](https://github.com/CC-Labs-it/ccp-classic/pull/79).
Its executable and installer contain the five original icon payloads. Metadata
confirms complete helper sources, no distribution blocker and no local-review
exception. `polish-final-release.trx` records the final 583-test Windows pass.

Final cross-platform acceptance on the same `19d97b4` source:

- [PR 79 standard CI](https://github.com/CC-Labs-it/ccp-classic/actions/runs/41655654169):
  Windows and Ubuntu passed.
- [Isolated Linux native validation](https://github.com/CC-Labs-it/ccp-classic/actions/runs/41655630819):
  every native stage passed. Refusal without a compositor, transparency/input
  regions and global panic/keyword input all passed.
- The silent natural video presented 29 colored 320 x 180 frames at 1x and credited
  1.883 seconds. The one-second cap stopped 1037 ms after the first frame and
  reported exactly 1 second. Video cancellation passed.
- Muted audio completed at 2 seconds; audio cancellation passed. Offline analysis
  produced a 2-second silent envelope with RMS 0; analysis cancellation passed.

The native evidence is in job `125985039061` of that Linux run. Local logs and
proofs remain under ignored `artifacts/`. Signed distribution and owner listening,
desktop feel and real-device review are separate from these automated results.

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
- The preview.7 parity checkpoint passed local Release validation, native Linux CI
  and installer verification. Three remaining ports are listed at the top of this
  handoff and are not part of preview.7. Actual device feel, wallpaper behavior on
  the owner's desktop and listening remain manual release checks. No real toy
  connections, audible playback or operating-system startup changes were made.
- LibVLCSharp 3.10.1 supplies the cross-platform recorded-audio backend; NAudio is
  not required by this implementation. Linux needs LibVLC plus audio/codec plugins.
  Windows ships 3.0.24 natives. Missing audio reports status while visuals continue.

## Validation

Latest integrated `dotnet test -c Release -m:4 --no-restore --disable-build-servers`:
Latest fully green integrated Windows checkpoint: 774 tests passed, zero failures/skips,
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
16. Target Practice
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
Gentle for at least 30 minutes; finish Deep with Strict Lock. Owner follow-up
approval on 2026-11-03 replaces Counting Practice with Target Practice: hit five
consecutive video attention targets, with a miss resetting the streak. Its new
persisted counters are separate from legacy counting stats; existing unlock history
is preserved. Other approved conditions remain unchanged.
