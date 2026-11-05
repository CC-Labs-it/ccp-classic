# Mandatory Video parity

Source pin: public CCP `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
Read-only lift sources: `Services/Video/VideoService.cs` (SetupAttention at5667,
SpawnTarget at5850, verdict/replay/mercy at6105, actual-playback cut at6995),
`Services/Video/AttentionTargets.cs`, `MandatoryVideoPresentation.cs`,
`FloatingAudioFocus.cs`, `VideoLengthCap.cs`, and
`Floating/FloatingVideoMotion.cs`; controls from `Features/VideoFeatureControl.xaml`.
These are the exact pinned blobs, retained for research under ignored artifacts,
rather than the later WPF working-tree versions. No WPF reference or browser was imported.

Classic retains a1x LibVLC decoder, bounded CPU frames and actual-playback caps.
The optional cut in seconds narrows the existing session cap in minutes. The
minimum only filters known short clips. Unknown duration remains eligible on
its first cycle, matching the source's asynchronously warmed metadata behavior.
Random start is selected only with a cut and enough known duration to leave a
whole slice. Its initial seek is excluded from playback allowance; audio
metering retains the raw media position. Native output remains DirectSound on
Windows with the selected local device. Missing/invalid media still fail closed.

Floating videos retain width15-60%, throw speed10-100%, at most four windows,
open-together, current-sound retention and monitor selection. The source pure
motion helper was reused with namespace changes and comments removed:80ms
throw sampling,6px/250ms tap test,120ms stale hand,3600px/sec speed ceiling,
25% bounce loss, friction and cross-monitor drag. Native pointer capture
replaces the source HWND hook because the CPU-frame video has no child HWND.
Windows begin at rest; this speed controls a throw rather than invented drift.
Fullscreen mirrors share one decoder and immutable frames. One sound holder
follows spawn/touch recency; a closing holder yields to the newest survivor.

Original attention checks replace Classic's counting prompt. Videos keep
playing while moving text targets are clicked. Target total1-10, random1-max,
3sec minimum spawn spacing, lifetime1-30sec, size30-150DIP, enabled custom pool,
font, floating outlined text, gradient panel and border are editable. Source
target speed187.5DIP/sec,7.5DIP outline,20px corners and two-word stacked
layout are preserved. Copies on several monitors share one hit and one result.
Only fullscreen video has targets. A failed verdict replays when selected,
strict always replays, and the optional2-10 attempt mercy bound ends repeats.
The original10% surprise replay after a pass follows the same setting and bound.
No microphone, gaze, OCR, accounts or external video playback were added.

Gentle slows movement, softens entrances and exits; Reduced Motion freezes
target travel and suppresses throws. Hidden/minimized windows pause their
decoder views, frame copies and target clock. Visible unfocused windows keep
playing. The shared panic gate and immediate teardown remain available across
strict videos and retries. No user text or local media path is logged.

The source24DIP corner mute and nonstrict close controls are retained, with
the60% resting opacity and full hover opacity. Mirror views share mute state;
muting changes no progression or playback timing. Closing consults the shared
panic gate. Classic's authoritative panic key is deliberately preserved rather
than repurposed as CCP's strict Escape grace-pause door, per owner direction.
The editor's Test action performs a cancellable local scan and opens one owned
video. Outside a running session this is floating/nonstrict with no targets;
inside a session it follows the configured presentation and cap. Tests never
launch that native preview or emit sound.
