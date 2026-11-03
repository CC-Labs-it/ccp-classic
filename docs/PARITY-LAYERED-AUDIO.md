# Layered audio

Reference: public CCP commit `fe40ea7587ac0b09e47d0724e59d7178db3336da`,
`Services/Audio/LayeredAudioService.cs`, `Models/AudioLayerTrack.cs`,
`Windows/LayeredAudioWindow.xaml(.cs)`, `Models/AppSettings.cs:3806-3840`,
and `MainWindow/MainWindow.StartStop.cs:338-454,545-546`.

Source defaults are preserved: layers disabled, master 70 percent, individual
tracks enabled at 70 percent, audio-only session off. Each selected local track
loops independently; live track/master gains do not rebuild the output graph.
Add/remove/enable changes rebuild. Missing or unreadable tracks are skipped
while other playable tracks continue. One real output device serves the mix.
The effective gain is track * layer master * app master * cooperative duck.
The layer switch is independent of the recorded-audio tile switch, matching
the source's independent layer enable and global MasterVolume. Numeric app
master zero still mutes the layers.
The source Preset model has explicit effect fields and does not include
AudioLayers or AudioOnlySession. Classic therefore keeps this local file list
outside effect presets. It is persisted in the ordinary local settings.

Standalone playback starts only on explicit user changes to the layer controls
or authorized session startup, never merely by constructing the saved editor.
Gain-only refresh cannot restart a graph stopped by panic. AudioOnlySession is
captured for the next session and starts the layered bed even if its standalone
switch is off. The source suppresses Flash, Video, Subliminal, spiral/pink
overlay, Bubbles, LockCard, BouncingText, MindWipe, BrainDrain and excluded visual
games. Wallpaper, companion and user corner GIFs remain independent (global
typed input left with keyword triggers, removed 2026-11-03). Classic's authoritative panic/app-close gate stops all local audio.

## Native architecture and resource limits

Codec decoding and actual output use the existing LibVLC dependency. One silent
context hosts at most eight virtual decoders. Each has one codec thread, an
adummy fallback and decoded-audio callbacks, so it cannot open a sound device.
One separate player owns the real output, forced DirectSound on Windows, at 1x.
The mixer supplies an in-memory float WAV stream, with no browser or external
playback decoder. Media inputs use verified local handles, pinned demuxers and
playlist rejection. No private source media is copied into the repository.

Silent rate4 and callback rate176400Hz preserve the normalized44100Hz PCM sample
sequence. A quiet decoder is paused ahead of the output and resumed after
consumption, so its native clock freezes instead of dropping late samples.
The real output's clock and rate remain unchanged. Each first pass is written
into GUID-named private transient PCM storage, and plays after a250ms prefill;
long tracks do not wait for full decode. Finished PCM loops at sample boundaries.

Budgets: eight tracks,4GiB per track,8GiB aggregate transient PCM, at least512MiB
free disk reserve, the shared75percent system RAM ceiling, pooled callback
chunks under8MiB, and64KiB read cache per track. The output reader stays at most
120ms ahead, freezes its pacing clock during buffering, and wakes on cancellation.
Live status distinguishes buffering, partial failure, no tracks and unavailable
output. Track-limit/resource failures remain visible rather than silently
truncating a loop.

One control thread owns native lifetime and a single latest-wins pending slot.
Stop clears published samples and gain immediately, cancels reads before native
teardown, then disposes output, decoders, contexts and private spools off the UI
thread. The returned stop task covers that cleanup. Crash cleanup only removes
verified GUID directories with an unlocked ownership marker and known filenames.

## Evidence and adapter differences

VLC3's [copy packetizer](https://github.com/videolan/vlc/blob/3.0.24/modules/packetizer/copy.c)
discards its held final raw packet on drain. Transcode/smem, raw passthrough and
WAV file sink all reproduced a20ms missing tail. Direct decoded-audio callbacks
preserve the entire input and avoid that path. Native
[amem](https://github.com/videolan/vlc/blob/3.0.24/modules/audio_output/amem.c)
supports S16N only in this runtime; PCM therefore has16-bit precision before the
float mix, compared with the source NAudio float pipeline. No missing samples
or guessed padding are accepted as a substitute.

Ignored neutral-fixture probes compared1x and4x sample-for-sample on44.1kHz
stereo and48kHz mono/stereo, short and long. The10.37s fixture retained all914634
samples at4x and through repeated pause/resume. Unequal loop math differed by
less than3.6e-9. A paced silent native output crossed live gain boundaries within
one16-bit sample step, with119.993ms maximum lead,35.66ms observed gain response
and1.73ms cancellation. Results are in `artifacts/audio-layers-probe`.

Committed checks cover normalization/reset, bounded storage and cleanup,
sample-exact unequal/tiny loops, forced buffer stalls, lifecycle/error/reentry
fakes and editor interactions. `NativeLayeredAudioTests` exercises the complete
service with two generated neutral WAVs, silent output, full tail counts,
decoder backpressure, looping, live gains, output-clock samples and cleanup.
Root coordinates actual Release/native runs and updates final release evidence.

Final Windows Release validation passed all 828 tests with zero skipped cases.
The real silent service smoke passed. Its latest recorded native outcome: one
output, one decoder backpressure pause, 16,317 and 148,617 complete normalized
frames for the 370 ms 44.1 kHz stereo and 3.37 s 48 kHz mono fixtures, live
effective gains 0.072/0.252, and output source position 1.643 seconds. Reported
loop cursors were 0.162993/1.642993 seconds. Stop and private cache removal
completed in 62.88 ms. The proof is
`artifacts/audio-envelope-probe/native-layered-audio.json`.

The constructor-failure cleanup also cancels the stream before releasing a
partially started native player. A focused regression verifies that aborting
an empty blocked read releases it and all following reads return EOF. This
regression is included in the final passing run.

Final Linux native/package run 41683113049 also passed. Eight alternating local
WAV/WMA tracks used one output graph, crossed a real loop boundary, applied live
master/track/duck gains without restarting, and retained 2,765,520 bytes of PCM.
Stop completed in 20.91 ms and left the private cache empty. The captured proof
is under `artifacts/ci-preview8-linux/artifacts/native-linux-probe/native.jsonl`.

The pinned source does not feed standalone layered audio into AudioSync.
`AudioSyncService.cs:355,369` contains the only producers of
`HapticService.SetSyncIntensityAsync`; those sample the web-video ChunkManager
from video playback updates. `LayeredAudioService.cs` has no haptic/DSP hook or
global loopback meter. Classic's retained local audio-sync path includes layer
samples during normal and audio-only sessions. Standalone layer playback keeps
the existing session gate for toys; no additional out-of-session event routing
was introduced.
