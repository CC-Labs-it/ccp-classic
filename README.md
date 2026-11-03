# CCP Classic

By CodeBambi. A free, offline conditioning panel for Windows and Linux.

One window, three tabs, twelve effect cards. No accounts, downloads, telemetry,
mods or games. Licensed under MIT.

## Development

Requires .NET 10. The desktop uses Avalonia 12.1.2, matching the CCP port.

```powershell
dotnet test -c Release
dotnet run --project src/CCP.Classic.App
```

M0 builds the panel and its safety controls. Effects remain inert until their
implementation milestones. See [the handoff](docs/HANDOFF.md) for current status.
