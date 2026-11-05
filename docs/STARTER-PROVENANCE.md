# Starter pack provenance

Source repository: public `CC-Labs-it/Conditioning-Control-Panel`.
Source ref: `origin/main`.
Source commit: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
License: MIT, historical upstream attribution "By CodeBambi"; retained in
THIRD-PARTY-NOTICES.md. Classic's current copyright is in the root LICENSE.

All six assets are copied byte-for-byte. No generation, transcoding or voice synthesis
was performed. The source repository was read only. LFS content was read from its
existing object store and accepted only after committed size and SHA-256 checks.

| Pack file | Original source path | SHA-256 | Bytes | Retrieval |
|---|---|---|---:|---|
| `Images/spiral.png` | `ConditioningControlPanel/Resources/web/dtrh/assets/bubbles/effects/spiral.png` | `59b776a8c7ebb814091e5e310858af3186e14418452f86cd747323a78e9743f1` | 84991 | Git blob |
| `Words/focus.mp3` | `ConditioningControlPanel/Resources/Audio/backroom/words/focus.mp3` | `65c5a795c34b9e245d48089e8b541f89b84145b41cf6fc7106c70d710eab279c` | 27629 | Verified local LFS object |
| `Words/breathe.mp3` | `ConditioningControlPanel/Resources/Audio/backroom/words/breathe.mp3` | `3e82d0ebdf1accb6d1d817177d3007d2e00499b575b33bf5ed6428f334f8b636` | 33585 | Verified local LFS object |
| `Words/relax.mp3` | `ConditioningControlPanel/Resources/Audio/backroom/words/relax.mp3` | `bdc1fe71f15951890ff9ce12fbfd0a505958fdff2e119a0cf6f147841b2bc158` | 31704 | Verified local LFS object |
| `Words/listen.mp3` | `ConditioningControlPanel/Resources/Audio/backroom/words/listen.mp3` | `5ebb42e79e5dac1612b0141e29c5743f911871b0fee7b5a3351c827c69ecdf4a` | 18225 | Verified local LFS object |
| `Words/deeper.mp3` | `ConditioningControlPanel/Resources/Audio/backroom/words/deeper.mp3` | `7d25687bb44a4fc58dcf43c11b74ff2236146a1b2dee8f18e6406f8dad9614c2` | 27629 | Verified local LFS object |

## Content approval

The five spoken words are FOCUS, BREATHE, RELAX, LISTEN and DEEPER, already enabled
in `Models/BuiltInMods.cs`, `CreateCCPDefault`. Their recordings are the neutral
Circe clips explicitly allowed by `Services/ModAudioPolicy.cs` and resolved by
`Services/BackRoom/BackRoomVoice.cs`, `FindNeutralClip`, through the original
`Resources/Audio/backroom/words/words.json` manifest. Missing recordings stay text.

The image is the original word-free teal spiral bubble, visually inspected before
copying. Only the bitmap is reused; no game code or game behavior is included.

No private folder, persona clip, mod override or shared persona audio entered this pack.
The pack totals 223763 bytes before filesystem overhead.

## Playback

Audio uses LibVLCSharp 3.10.1 with a separate player for each sound. Windows uses
DirectSound output and the port's cubic volume mapping to preserve per-sound gain.
Linux needs the system libvlc runtime; soname resolution supports libvlc.so.5 and
libvlccore.so.9. Missing native audio reports an error without stopping visuals.
Only existing local MP3, WAV, OGG and FLAC paths are accepted. URL, UNC and playlist
inputs are refused before playback. Native media loading and teardown run off the UI.
