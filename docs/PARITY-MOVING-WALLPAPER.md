# Moving wallpaper collage

Public CCP pin `fe40ea7587ac0b09e47d0724e59d7178db3336da` was read only.
Lift sources are `Services/Video/DynamicWallpaperService.Build.cs`,
`DynamicFrameBudget.cs` and `CollageComposer.Plan.cs`, alongside the already
ported static compositor. No private data, pack content or network loader was copied.

Dynamic plus Collage now consumes the selected local image/GIF/WebP pool,
rather than requiring a video. Existing local video wallpaper remains a separate
path when Collage is off. Static image/collage behavior and the crash-recovery
wallpaper journal are unchanged. Feature presets capture the wallpaper options
by value, including selected layouts, colors, source and rotation interval.

The native scene reuses the original three layout plans, per-edge focus-aware
feathering, hero sharpener, tilted filler/spill tiles, blurred atmosphere,
accent grade and vignette. Each monitor gets its own plan, with aspect-preserving
cover fit. The largest readable sources supply heroes. Up to19 tiles per monitor
move, with the source hero+25 percentage-point weighting and farthest-of-three
spread. Moving heroes decode at512px, moving filler at320px; still heroes retain
larger bounded resolution. Native animated-image disposal is honored, and the
original delays of skipped frames are accumulated. At most120 frames are kept
from at most900 source frames. Longer sources remain still.

One serialized worker builds scenes off the UI thread. The512MB budget includes
all retained clip frames and backdrop/finish layers across every monitor.
Before large allocations and every decoded frame, the existing75% system RAM
ceiling is checked, including projected allocation headroom. Source files are
validated local files, limited to64MB and16million source pixels. Cancellation
is checked every frame; the whole build has a30second timeout. Resource refusal
stops the build and releases its partial frames. Two scenes may coexist only
through the900ms rotation crossfade. Dynamic collage rotation has the source
30second minimum, avoiding repeated full GIF bakes.

The existing native desktop adapter owns attachment and visibility. Animation
freezes on covered/minimized monitors, battery or Reduced Motion, and resumes
without catching up. Gentle halves the animation clock. Performance mode keeps
the existing15fps presentation bound. Panic cancels pending workers and queued
publication, clears hosts and releases frames. Linux wallpaper remains absent.

Validation uses synthetic4px GIFs, approved starter images, native offscreen
rendering and an injected desktop adapter. The tests do not change the actual
desktop wallpaper. A Windows native desktop acceptance check is still separate
from those offscreen checks. `wallpaper-moving-collage.png` is the deterministic
proof artifact when `CCP_CLASSIC_PROOF_DIR` is set.

`wallpaper-moving-collage-public.png` additionally uses the exact previously
verified public starter spiral GIFs, the approved starter image and unchanged
neutral dashboard artwork already in Classic. The synthetic timing fixture is
kept separate, so saturated test-color frames do not stand in for visual proof.
