# Bouncing Text source parity

Public CCP pin: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
`ConditioningControlPanel/Services/Subliminal/BouncingTextService.cs` was read
from that exact commit, blob `5d395034efd42bc24e70771b059db445506a2b3e`.
Its working-tree copy had later edits, so that copy was not used for the port.
The matching feature XAML blob is `5c21ef210239e2767e83023ff9214f064fe1c73c`.

The native controls retain the source's three primary ranges: speed 1-10,
size 50-300% of 72 DIP, opacity 10-100%. More includes random/fixed/rainbow
color, fixed color reset, installed font and bundled Fredoka, independent
second text, display over videos, breathing, wobble, spin, velocity tilt,
squash/stretch, corner burst, outline and enabled editable phrase pool.
Classic's approved five neutral defaults replace the source's phrase defaults;
the owner can enter local custom phrases. An empty enabled pool falls back to Focus.

The source's motion formulas are retained: 180-300 DIP/sec times speed/10,
3.2 second .08 breathing, 2.3 second 6 degree wobble, 40 degree/sec spin,
6/sec smoothing toward a bounded 12 degree velocity tilt, damped .35 squash,
.4 corner pop, 550 ms expanding ring with quadratic ease out, 15 DIP corner
tolerance, ten-color random palette, twelve-step rainbow and 10% phrase
change on a wall bounce. Text measurement uses the selected native font;
transformed overhang keeps glyphs within the outer desktop bounds. The second line has its
own position, phase, velocity, color and timers. No text is logged.

Classic uses its existing bounded shared effects host rather than source WPF
windows. Native glyph rendering and typed input use no WPF reference. Reduced
motion freezes the current pose. Gentle halves travel and breathing/wobble,
freezes spin, omits corner flashes and keeps colors steady on wall hits.
Panic clears both lines and all transient transforms. Video pause uses the
existing playback state and the explicit display-over-videos option.

Multi-monitor travel retains `CalculateScreenBounds` at source lines 344-357 and
`UpdatePosition` at 1031-1064: exactly one or two global text instances move inside
the rectangular union of the selected displays. Each host subtracts its desktop
origin and clips the same glyphs; it never clamps a copy into its own monitor.
Monitor seams, vertical offsets and gaps do not create walls or rewards. Text can
briefly be invisible while crossing a gap, as in the source. The same rules apply
to the global corner burst center.

Classic keeps one reference DPI scale per session and projects physical desktop
coordinates into each host's local DIPs. This preserves glyph size and position
at a mixed-DPI seam, including negative screen origins. Display changes preserve
the current global position, quietly clamp and redirect into the new outer bounds,
and cancel an obsolete corner ring without emitting a bounce. The opaque fallback
uses its own client rectangle. Extra spiral or filter hosts outside the selected
displays receive no bouncing text. Panic clears global poses along with the hosts.

`BouncingDesktopTests` covers geometry, gap traversal, topology changes and motion
policies. `BouncingDesktopRendererTests` renders a real outlined Fredoka glyph
across a 100%/150% seam and compares its combined raster with one full desktop
render. `bouncing-text-desktop-seam.png` is the native offscreen visual proof.
Its 96-DPI bitmap uses explicit host scaling, matching compositor transforms;
Avalonia 12.1.2's bitmap-only hidden DPI path can reapply DPI after `PopClip`.

Ordinary wall hits and corner hits are separate events. Every wall bounce
routes one source haptic, while corner achievements remain independent. The
source 15 XP reward shares a 2 second cooldown and 150 XP per minute cap across both
lines. Its monotonic limiter survives session restarts and does not award twice
for a corner. No haptic is emitted merely because text becomes visible.

`Assets/Fonts/Fredoka.ttf` is copied unchanged from public `Fonts/Fredoka.ttf`,
verified source blob `349b6ecef8a358ede29b7bf74a08196be5d71b25`.
`Fredoka.OFL.txt` is the unchanged SIL Open Font License from public
`Resources/web/remix/assets/fonts/fredoka-latin.OFL.txt`, verified blob
`791a98de7893b9bb62d06729745d00655e791a40`. Its license stays embedded beside
the font. Installed fonts remain local machine choices and are not copied.
