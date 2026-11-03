# CCP Classic

By CodeBambi. A free, offline conditioning panel for Windows and Linux.

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

M1 adds Flash Images, Subliminals with optional recorded whispers, Spiral, Pink
Filter and Audio. Press Start to use the bundled CCP Default starter media.
Panic listens globally on Windows and local X11. Wayland keeps in-app panic;
the panel reports when global panic or desktop transparency is unavailable.
See [the handoff](docs/HANDOFF.md) for the review stack and current limitations.
