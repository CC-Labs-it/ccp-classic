# CCP Classic

Fresh public MIT application, By CodeBambi. Follow the owner's global brief.

- Work only in this repository. CCP and its port are read-only lift sources.
  Never copy server code or private assets here.
- Target net10.0 and Avalonia 12.1.2. Never reference WPF or the full CCP projects.
- No networking APIs, embedded browser, accounts, telemetry or update checks.
  Future localhost-only haptics providers live under src/CCP.Classic.Core/LocalDevices.
- Ten languages: de, en, es, fr, it, ja, ko, pt-BR, ru, zh-CN.
  Flat JSON, UTF-8 without BOM. Edit existing lines, never reserialize translations.
- Chrome is neutral, no persona names, no exclamation marks, no en/em dashes.
  Italian stays gender neutral. Never synthetic speech. Only approved neutral clips.
- Stop gestures share the panic gate. A held key is one press. Effects consult
  Gentle visuals and have entrances and exits. Videos always play at 1x.
- Owner correction 2026-11-03: keep the CURRENT CCP dashboard image mosaic,
  with the animated CCP logo in its central 2x2 cells. Image tiles open owned
  settings windows, replacing the flat mockup grid and slide-in sheets.
  Fit the main window around the grid, nearly square, with a compact two-row header.
- Owner correction 2026-11-03: use CCP's original Bouncing Text tile artwork
  unchanged, including its baked-in wording. It replaces the vector placeholder.
- Studio / Presets / Progress; Library / Companion / Toys / Settings windows.
  At most three primary controls per editor. Video concurrency goes under More.
  Wallpaper is absent on Linux. No achievement or companion copy without approval.
- Keep changes around 600 lines per PR; use a buildable stack. Test Release.
  CI must build and test on Windows and Linux. Keep docs/HANDOFF.md current.
- All UiDispatcher-backed tests share the Avalonia UI collection. RunJobs can
  reenter another queued test; one dispatcher thread alone does not isolate input.
