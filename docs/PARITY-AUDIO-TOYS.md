# Audio and local device parity checkpoint

Pinned public reference: `CC-Labs-it/Conditioning-Control-Panel` at
`fe40ea7587ac0b09e47d0724e59d7178db3336da`. Source was read with `git show` at that
commit, never from the local working tree. No source repository writes occurred.

- `Views/Controls/AppSettings/AudioSettingsSection.xaml`: output refresh/selection,
  test recording, independent duck enable, source DSP controls.
- `Models/AudioSyncSettings.cs`: sensitivity 0.1-3, smoothing 0-0.95, three feature
  weights 0-1, minimum 0-0.5, maximum 0.5-1, offset -600 to 600 ms.
- `Services/Audio/AudioAnalyzer.cs`: 2048-point Hann FFT, 512-frame hop, bass energy
  around 20-250 Hz, positive spectral flux and asymmetric adaptive baselines
  (rise 0.015, fall 0.003, subtract 0.85).
- `Models/HapticSettings.cs`, `Services/Haptics/Core/HapticContracts.cs`,
  `HapticDeviceManager.cs`, `HapticMixer.cs`: provider-qualified device identity,
  device enabled/trim/nickname/role, event target roles, global cap and trim.

The LibVLC output picker enumerates real local devices on a worker and routes
existing native players using SetOutputDevice. Windows retains the forced
DirectSound output module. System Default remains available; unavailable saved
identities are shown rather than silently rewritten. Testing uses the approved
local relax recording and obeys the existing master gain and stop gate. Output
identity is a machine preference outside effect presets.

Local routing controls affect provider commands individually. A disabled or
unmatched device receives zero while another device can remain active. The master
power cap applies before per-device trim. Stop/disconnect still flush every
device. No provider connects merely because a routing editor opens.

Audio sync keeps source positions and raw RMS waveform behavior. Its six raw FFT
features come from the same PCM stream, with bounded five-minute chunks and no
second decoder or system mixer capture. The original four passes are preserved:
chunk peak normalization, adaptive baselines, median-times-0.3 quiet threshold,
sensitivity/minimum/maximum/smoothing and high/bass transient pulse overrides.
The low and high envelopes use the original fourth pass, gated by the combined
envelope. FFT band indices follow the actual 48 kHz PCM rate rather than assuming
44.1 kHz. Curve calculation runs on a worker; a pending curve stays silent.
Silence, paused/expired samples and failed analysis clear output immediately.

Opt-in band split routes low energy to the first vibration motor and highs to the
second on toys with two or more such motors. Single-motor toys retain combined
output. The live intensity control scales all three without reanalysis.

`Services/Haptics/FunScript.cs` and `FunScriptService.cs` supply local discovery,
parsing, interpolation, 300 ms position lookahead, 900 ms stale-report cutoff and
the 10-500 position-units/second vibration mapping. Classic searches only beside
the selected local video and its `funscripts` subfolder, reads at most 10 MB and
never starts output before an explicit provider connection. A canceled or old
load cannot become current. Position output holds on pause instead of issuing a
home movement; all positions also obey Classic's power cap and device trim.

`Services/Flash/FlashService.cs:2210-2324` supplies flash luminance sync. Classic computes
the same alpha-weighted Rec.601 8x8 average from the first frame while its image
worker is already decoding it. Cached scalar metadata survives bitmap eviction.
The optional independent layer uses its configured scale/role, expires without
fresh visible images and clears on stop. No desktop capture or second decode is
involved, and GIF animation does not change a flash's sampled first-frame value.

`Services/Haptics/Core/HapticTemperament.cs` supplies the original five profile
tables. Continuous/transient strength, attack/decay shape and concurrency bias
apply before the power cap. `HapticMixer.cs:899-1000` supplies live-pulse priority
admission, expired-pulse replacement and the shared 100 ms peak-of-sum sampling
grid. Pending source pattern steps are bounded and cleared with active pulses.
Each connected device card now has a real six-pattern graph, strength/duration
and a bounded device-targeted test. Its Stop button delegates to the shared
session gate; the editor does not bypass a rejected stop.

The source default hard cap is 0.70 and continuous rise time is 800 ms. Classic
now uses both defaults, limits rise according to monotonic elapsed time and
drops immediately when a source falls or stops. The 0.06 perceptible floor is
applied before the cap and per-device trim; zero master intensity still stops
output. These rules apply once per device/tick, including split-motor ratios.

`Models/HapticSettings.cs` VideoEnabled/VideoIntensity and
`Services/Haptics/Core/HapticContracts.cs:144-156` distinguish the continuous
video background from transient accents. Classic's video floor is now an
independent fresh continuous source, using the video intensity and target role,
without allocating transient patterns. Muted video uses timeline samples;
old serialized video-pattern values remain unused, matching the source's dead
compatibility field.

Remaining functional gaps and deliberate distinctions:

- `ButtplugProviderV2.cs:164,506,778-818` has a real Intiface button path. It hooks
  InputReadingReceived, subscribes only advertised Button features with Subscribe
  support, then emits down/up events. `ToyInputService.cs:24-145` applies a
  350 ms per-device debounce and supplies attention-check waiters. Classic has
  no corresponding advertised-button input subscription yet. This is a confirmed
  functional gap and needs compatibility checks against Classic's native wire
  transport before a port.
- The source Pattern layer row, labeled Deeper, also scales/gates FunScript
  (`MainWindow.Haptics.cs:131`, `HapticUiModels.cs:202-318`). Classic already has
  explicit script enable/conversion/role plus global cap/trim. An independent
  FunScript scale is the one optional remaining local strength control; a new
  generic layer framework is not needed.
- `LovenseToyEventsClient.cs:12-32` explicitly calls its /v1 handshake unpublished
  and uses two speculative access-request shapes. Its strength-change backoff
  does not establish functioning parity without protocol/device evidence. The
  Intiface implementation above emits buttons/battery, not StrengthChanged,
  so override cooldown is not a separate confirmed Intiface gap.
- Qualified provider identities are deliberate. Source
  `HapticDeviceManager.cs:217-300` suppresses duplicate normalized names and prefers
  Lovense over Intiface. That heuristic also merges distinct toys of the same
  model/name. Classic keeps them separate and exposes explicit enabled/role/trim
  controls rather than silently merging physical devices.
- Source Manual is an auto-zero test shim; Session is marked future. The current
  real tests and continuous controls cover this local scope. Deeper/Race/network
  feature families are excluded. They do not justify extra dormant controls.
- OutputHz is persisted but unused by the source runtime. Chunk duration/buffer
  sizes are internal analysis tuning with no Views/MainWindow controls. Classic
  uses the same 10 Hz/five-minute defaults with its local buffer/cache budgets;
  absent expert sliders are not functioning source UI gaps.

Tests use fake toy transports and actual FFT response to low/high tones. They
check identities, disposal/cancellation, emitted per-device commands, routing,
caps and trim. Five native Windows audio checks also pass, including real
DirectSound enumeration, exact selected-device identity, default reset and
approved recorded playback at zero gain. No physical toy was connected.

Native verification caught a managed API trap: LibVLCSharp SetOutputDevice takes
deviceId before module, unlike the C API. Named arguments preserve that boundary.
Windows enumerates actual DirectSound GUIDs because VLC's legacy instance API can
return an empty list. The directx-audio-device setting primes future outputs;
the active-output call handles a live change, and empty ID restores default.
