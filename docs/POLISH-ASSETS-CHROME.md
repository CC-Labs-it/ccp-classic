# Classic visual polish provenance

The original CCP mosaic, tile artwork and layered logo remain the approved brand
assets documented in ASSET-PROVENANCE.md. This pass changes native chrome and
interaction behavior around those assets. No private assets or server code are used.

## Original application icon

- Public repository: https://github.com/CC-Labs-it/Conditioning-Control-Panel
- Pinned commit: `fe40ea7587ac0b09e47d0724e59d7178db3336da`
- Source: `ConditioningControlPanel/Resources/app.ico`
- Classic: `src/CCP.Classic.App/Assets/app.ico`
- Bytes: `99678`
- SHA-256: `8004d82ec8dcbadfab2fe56f7264a82366697836bb90967f7f6c56ff38a79514`
- Git blob SHA-1: `e778a679243c917a9a4e955ec158866edd409a26`

The pinned public raw file and inspected local source have identical bytes. The
original icon retains its baked legacy branding, consistent with the approved
original artwork. It uses the owner's Classic MIT reuse permission. The `.ico`
extension is marked binary before its first commit. The same file supplies the
application executable, native window icon and Windows setup icon.

## Chrome and animation

The coral/pink diagonal card edge and restrained press/release behavior follow
CCP's existing `Resources/Theme/Controls.xaml` Velvet Kit treatment. The Avalonia
implementation uses its own native controls, shared styles and bounded timers.
The source's WPF project is not referenced.

The existing `Controls/DashboardLogoRenderer.cs` supplies the logo's real layered
animation. This pass increases its existing sparkle count and size without
changing the artwork. Its host presents at most one worker-rendered frame at a
time. Visible inactive windows continue animating, as requested by the owner;
hidden/minimized windows and motion preferences suspend animation.

Chrome entrances and hover/press movement consult Gentle visuals, Reduced motion
and Performance mode. Idle controls have no running hover clock. Owned settings
windows receive the same icon, palette and preference updates as the main panel.
