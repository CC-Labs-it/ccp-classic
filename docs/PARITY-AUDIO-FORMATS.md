# Local audio format parity

Public CCP pin: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
`ConditioningControlPanel/Windows/LayeredAudioWindow.xaml.cs`,
`BtnAddTrack_Click`, includes WMA with MP3, WAV, OGG, FLAC, M4A and AAC.
Classic also already supported local Ogg Opus playback. A shared eight-format
list now drives library discovery, every recording picker and saved clip paths,
including subliminal phrases, Mind Wipe and layered tracks (keyword actions
left with keyword triggers, removed 2026-11-03).

WMA remains a local file input. Playback reads all 16 bytes of the ASF Header
Object GUID `75B22630-668E-11CF-A6D9-00AA0062CE6C`, with stored bytes
`3026B2758E66CF11A6D900AA0062CE6C`, before selecting the forced `asf` demuxer.
A matching extension or partial GUID does not select ASF. ASX, M3U and PLS text
disguised as WMA fails the container check. Native media retains its open local
handle, playlist rejection and no-network metadata policy. URLs, UNC paths,
network drives and reparse paths retain their existing native input rejection.
The [Microsoft ASF structure reference](https://learn.microsoft.com/en-us/windows/win32/medfound/asf-file-structure)
describes the mandatory leading Header Object and its 128-bit object identity.

Offline analysis uses the same full signature and the fixed `asf` demuxer.
Its file/pipe protocol whitelist, preserved timestamps, process/resource limits
and streamed PCM path are unchanged. The approved helper remains analysis-only
in production. Fixture encoding is confined to opt-in tests and isolated probes.

`AudioFormatsTests` covers scanner/settings parity, all 16 truncated signature
lengths, mutation of every GUID byte, renamed playlists and nonlocal paths.
`NativeWmaPlaybackTests` generates a neutral local 440 Hz fixture under ignored
artifacts using the verified helper. Actual LibVLC playback checks volume zero
throughout, source progression and successful natural completion. PCM analysis
checks the leading silence and both known energy levels. The first Windows run
completed 1.622 seconds, with silence 0, high RMS 0.50005 and low RMS 0.24998.

The existing Linux probe now generates a two-second neutral WMA using its local
FFmpeg and verifies muted native completion/cancellation plus ASF analysis of
the same leading silence and two energy levels. It uses the existing private
Xvfb display and PulseAudio null sink. No public desktop, hardware output or
runner setup change is involved. Linux execution remains a CI verification gate.
