# CCP Classic

Fresh public MIT application, CC labs Srls. Follow the owner's global brief.

- Work only in this repository. CCP and its port are read-only lift sources.
  Never copy server code or private assets here.
- Target net10.0 and Avalonia 12.1.2. Never reference WPF or the full CCP projects.
- No networking APIs, embedded browser, accounts, telemetry or update checks.
  Future localhost-only haptics providers live under src/CCP.Classic.Core/LocalDevices.
  Owner-approved exception: the support banner opens the fixed public Patreon URL
  in the system browser only when clicked. The app makes no request itself.
- Ten languages: de, en, es, fr, it, ja, ko, pt-BR, ru, zh-CN.
  Flat JSON, UTF-8 without BOM. Edit existing lines, never reserialize translations.
- Chrome is neutral, no persona names, no exclamation marks, no en/em dashes.
  Italian stays gender neutral. Never synthetic speech. Only approved neutral clips.
- Stop gestures share the panic gate. A held key is one press. Effects consult
  Gentle visuals and have entrances and exits. Videos always play at 1x.
  Preserve held keys across owned-window focus changes; reset only after the
  whole Classic window group loses focus.
  When the global listener is available it owns both key edges. Mixing local
  keyup with queued global keydown can count a physical press twice.
- Owner correction 2026-11-03: keep the CURRENT CCP dashboard image mosaic,
  with the animated CCP logo in its central 2x2 cells. Image tiles open owned
  settings windows, replacing the flat mockup grid and slide-in sheets.
  Fit the main window around the grid, nearly square, with a compact two-row header.
- Owner correction 2026-11-03: use CCP's original Bouncing Text tile artwork
  unchanged, including its baked-in wording. It replaces the vector placeholder.
- Owner polish direction 2026-11-03: reuse public CCP artwork, icons, preset and
  achievement presentation, help animations and asset-manager patterns. Basic
  scope must retain depth, hover/press feedback, transitions and visual flair.
  The visible dashboard logo keeps animating when another app has focus;
  hidden/minimized and motion-preference gates still apply.
- Owner correction 2026-11-03: current publisher, copyright and app credits are
  CC labs Srls. Remove the old personal byline from current chrome. Preserve
  historical upstream asset attribution. Editors fit their content with readable,
  colored typography; reuse CCP's support banner with its direct Patreon link.
- Owner effect parity request 2026-11-03: reuse CCP's free image-click outros
  and ambient effect bubbles, including the same flash image inside the bubble
  and after its pop. Keep timed image expiry soft, motion preferences effective,
  and effect/video capacity bounds. Defaults are Mix exits and 10% effect bubbles.
- Owner correction 2026-11-03: existing Classic features need full public CCP
  behavior and customization parity, including original bubble sound/animations,
  Bouncing Text, Brain Drain, editable triggers and lock cards. Simplified chrome
  is not permission to replace the underlying behavior with approximations.
  Reuse verified public source and record concrete gaps before porting them.
  Owner scope decision: typed input only. Do not add microphone completion or
  screen-reading/OCR. Brain Drain desktop capture is a visual effect, not OCR.
- Owner approval 2026-11-03: Target Practice replaces Counting Practice, earned
  by five consecutive video attention targets hit, with a miss resetting the
  streak. Persist separate video-target streaks. Preserve the existing badge ID
  and unlocked history, but never award the new condition from legacy count stats.
- Studio / Presets / Progress; Library / Companion / Toys / Settings windows.
  At most three primary controls per editor. Video concurrency goes under More.
  Wallpaper is absent on Linux. No achievement or companion copy without approval.
- Keep changes around 600 lines per PR; use a buildable stack. Test Release.
  CI must build and test on Windows and Linux. Keep docs/HANDOFF.md current.
- All UiDispatcher-backed tests share the Avalonia UI collection. RunJobs can
  reenter another queued test; one dispatcher thread alone does not isolate input.
- Worker-built Avalonia scenes must publish immutable brushes. Mutable gradients
  retain the worker dispatcher and fail when the UI renders their opacity masks.
- Native playback callbacks can finish between worker polls. Sample completion
  once per pass and revalidate terminal errors before release; a healthy Refresh
  does not prove that a subsequent completion succeeded.
- LibVLC 3 can leave player.Time stale while video Display callbacks keep playing.
  Video caps combine source time with bounded presented-frame progress. Keep raw
  source positions for audio metering; do not replace this with metadata duration.
- LibVLCSharp SetOutputDevice takes deviceId before module, unlike the native C
  function. Use named arguments. Windows lists real DirectSound GUIDs through
  DirectSoundEnumerateW; VLC's legacy instance enumeration can return empty.
  Empty device ID restores default; null is ignored. Native identity tests guard this.
- LibVLC 3 decoded-audio callbacks use S16N unless a setup callback changes the
  negotiated format. SetAudioFormat("FL32") alone does not yield float samples.
  The transcode copy packetizer drops its last packet; direct decoded callbacks
  preserve the tail but run at playback speed. Do not treat fast smem as lossless.
- Starter recordings use LFS; checkout in CI must fetch LFS. Audio playback uses local
  LibVLC only, forced DirectSound on Windows, never a mixer-wide fallback output.
- Owner approval 2026-11-03: bundle offline FFmpeg for PCM/RMS analysis only.
  Playback remains LibVLC. Restrict helper inputs to local files/pipes, cap CPU
  use and stop analysis if total system RAM exceeds the owner's 75% ceiling.
  Retain FFmpeg -copyts without -start_at_zero. LibVLC source time preserves
  common leading gaps; do not normalize envelopes using ffprobe duration.
  Retain the verified dependency source cache in artifacts/analysis-helper-cache.
  Its original upstream Actions download expires on 2026-11-14; source bundles
  accompany redistributed Windows packages and are separate from the runtime.
- Owner approval 2026-11-03: Companion barks are Breathe, Relax, Focus, Listen,
  Deeper, using the approved starter recordings. Relax triggers a flash, Focus
  a whisper, Deeper a spiral pulse. The 27 neutral badge names and four legacy
  preset condition mappings are approved and recorded in docs/HANDOFF.md.
- Native Loom correction: odd-arm tunnel layers need one full radial color cycle
  as their loop span. The source's generic arm symmetry swaps colors at the seam.
- Windows packaging: keep Inno staging paths below MAX_PATH. Short indexed notice
  folders preserve package identities in components.json without long NuGet paths.
- Main-window closing uses OwnerWindowOnly. Avalonia's default asks owned children
  first, allowing strict video/card/companion windows to veto the app-close escape.
