# Wallpaper and companion placement checkpoint

Public CCP reference: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
All source reads use that commit with `git show`. Its local working tree is not
the reference and was not changed. These are source-code ports under the public
MIT license. No new artwork or private asset is included.

- `ConditioningControlPanel/Services/Video/CollageComposer.cs` and
  `CollageComposer.Plan.cs`: original three layouts, hero/filler/spill planning,
  edge feathering, image-focus estimate, tilts, blurred atmosphere, accent grade
  and vignette. The Classic files preserve these drawing/layout algorithms.
- `ConditioningControlPanel/Services/Video/WallpaperService.cs` and
  `Views/Controls/Studio/WallpaperFeatureControl.xaml`: dedicated local folder,
  own image, shuffle, rotation interval and selectable collage layouts.
- `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Windowing.cs` and
  `TubePlacement.cs`: detached position, scale bounds 0.5-1.5 with 0.25 steps,
  placement persistence and topmost preference. Existing approved tube clips
  and bark content are unchanged.

Classic exposes these wallpaper settings and passes them into the existing
transactional cover/restore orchestration. Source changes invalidate the current
generation. Shuffle leaves the previous wallpaper in place until the new cover
is opaque; stop cancels scans/composition and restores through the existing
verified journal. Dedicated folders do not replace the user's Library folders.
Generated files are individually tracked and retained if restoration fails.
Own GIF/WebP images are converted locally to a Windows-compatible PNG.

Composition runs on a worker with at most 40 decoded source images, capped tile
resolution, bounded source dimensions/file size and a maximum 3840x2160 output.
It does not launch a decoder process or change desktop state while rendering.
The original dynamic collage composition is still a separate parity gap: the
current dynamic mode plays a selected local video through the existing cover.

The companion now supports explicit detached placement, drag, Ctrl+wheel scale,
topmost preference and reset. Physical saved positions are clamped to a current
monitor working area. Attached placement follows CCP's `TubeDockPlacement`: the
tube art docks 3 DIP left of the main window, mirrors onto its right side when the
left has no room, and otherwise floats fully on screen; main is never moved.
Machine placement is kept outside effect presets.

Tests use fake static writers/covers and temporary generated test images. They
cover rotation without catch-up, folder snapshot isolation, replacement-cover
ordering, all three deterministic offscreen collage layouts, format conversion,
owned-file cleanup and companion scale application. Actual Windows wallpaper
changes and physical screen drag behavior require an owner manual check.
