# CCP Classic

CC labs Srls. A free, offline conditioning panel for Windows and Linux.

One main window, three tabs, twelve image tiles around the animated CCP logo.
Click a tile to open its settings window. No accounts, downloads, telemetry,
mods or games. Licensed under MIT.

## Development

Requires .NET 10. The desktop uses Avalonia 12.1.2, matching the CCP port.

```powershell
dotnet test -c Release
dotnet run --project src/CCP.Classic.App
```

Run `git lfs install` before cloning, or `git lfs pull` after cloning, to obtain
the bundled neutral recordings. Linux audio needs the system LibVLC runtime and
its audio/codec plugins. Windows includes its native runtime.

Press Start to use the bundled neutral starter media. Local image and video
libraries, recorded whispers, bubbles, bouncing text, lock cards, Mind Wipe,
Brain Drain and the original companion are included. Native Loom creates saved
custom spirals. Presets, timed changes, progress and achievements stay offline.

Toys connects only after an explicit request to a local Intiface or Lovense
provider. Optional audio sync uses an offline FFmpeg analysis helper; playback
uses LibVLC. Windows also supports recoverable static and dynamic wallpaper.

Panic listens globally on Windows and local X11. Wayland keeps in-app panic and
uses a normal owned effect window when desktop overlays are unavailable.
See [installation](docs/INSTALL.md) for dependencies and unsigned preview builds,
and [the handoff](docs/HANDOFF.md) for validation and remaining release checks.
