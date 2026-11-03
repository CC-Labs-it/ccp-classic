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

M0 builds the compact dashboard and its safety controls. Effects remain inert until their
implementation milestones. See [the handoff](docs/HANDOFF.md) for current status.
