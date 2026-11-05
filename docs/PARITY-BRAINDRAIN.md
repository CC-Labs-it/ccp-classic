# Brain Drain native port

The renderer follows the public CCP files at
`fe40ea7587ac0b09e47d0724e59d7178db3336da`:

- `Services/Compositor/BrainDrainLayer.cs`, blob `0c41aeff57cbff39a81ffc254a5c5dfa37ec4145`.
- `Services/Compositor/BrainDrainCapturePump.cs`, blob `a6cf9fc5c111337e12812e9733b908909f2f3592`.
- `Services/LockCard/BrainDrainService.cs` for clip scheduling.
- `Services/Notifications/BrainDrainVisualPolicy.cs` for the zero-strength policy.
- `Services/Chaos/BrainDrainBubble.cs` for the ten-second no-overlap melt payload.
- `Views/Controls/Studio/BrainDrainFeatureControl.xaml` for independent controls.

The layer/pump blob identities and formulas were verified with GitHub at the pinned
public revision. No server source, account behavior, private recordings or screen
captures enter this repository.

The Windows path uses a separate capture-excluded, input-transparent window per
selected monitor. It captures plain SRCCOPY on a single background worker, never
the dispatcher. GDI's unused fourth byte is ignored using the original Rgb888x
and red/blue swap fix. Capture buffers and a replaceable latest frame are bounded
to eight screens, each at most 960 by 540 pixels. Performance mode downsamples
further and caps capture at 15 fps. A stalled native capture cannot delay stop or
start a second worker while the old one is retiring.

The source blur sigma, alpha curve and cached Perlin constants are retained. Blur
strength zero removes the picture completely while leaving the audio schedule
independent. Gentle and Reduced motion use plain blur. Entrances and setting
disable exits fade; a session stop removes hosts and pending frames immediately.
Default capture exclusion can be changed explicitly through Show in screenshots.
Frames stay in memory and are never saved or inspected for text.

The audio lane reads only explicitly selected local folders, rescans on session
start or Refresh, and has no fallback speech. Each original five-second tick
(half a second under High refresh) rolls `audioRate/100 * tickSeconds/60`.
A winning roll while a clip is busy is skipped without queuing or restarting.
LibVLC playback, live master/duck gain, mute and stop policies remain shared with
the rest of Classic.

The original bubble payload uses ten seconds of melt, or plain blur when motion
is reduced. It cannot overlap a timed drain or an already visible baseline drain,
and zero blur strength remains silent. No account or purchase gate is carried over.

Safe native desktop capture on X11 and Wayland is not implemented by this slice.
Those backends report Brain Drain visual unavailability while local audio can
continue. There is no substitute ring or tint effect claiming desktop blur parity.
Tests inject synthetic BGRX pixels and fake capture sources and never capture the
actual desktop.
