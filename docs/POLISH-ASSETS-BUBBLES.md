# Effect bubble provenance

Public source: `CC-Labs-it/Conditioning-Control-Panel`, pinned commit
`fe40ea7587ac0b09e47d0724e59d7178db3336da`. The source repository was read only.
Owner approved reuse of the ambient effect bubbles and flash images inside bubbles.

`Assets/Bubbles/glass.png` is an unchanged copy of
`ConditioningControlPanel/Resources/bubble.png`, verified against the pinned public
raw file before copying: 166363 bytes, SHA-256
`56b043658f5b5f645a4d786b6fb3e301cdbd70af8067d619d5aa6b17ce6fd41f`.
The existing PNG binary rule was in place before this addition. The original
MIT attribution is retained in THIRD-PARTY-NOTICES.md.

The public GitHub service verified these code blobs at that commit:

- `Services/BubbleService.cs`, `6fd546ca98491717ba57fab1def8cd6f79ca1e37`:
  `RollTriggerSpec`, `BuildTriggerSpec`, `CreateAmbientBubble`, `PrepareFlashFace`,
  `PrepareFlashDelivery`, and `BuildBubbleFace`.
- `Services/BubbleFace.cs`, `03079d9184b6f7c63ae075cb4124799e926821d8`:
  the 60% face diameter and the radial edge fade beginning at 80%.
- `Services/Chaos/ChaosBubbleVariants.cs`: the existing neutral tint and symbol
  mapping for Flash, Subliminal, Pink Filter, Spiral, Brain Drain and Video.

Classic uses its existing bounded shared overlay, local image cache and effect
schedulers. A captured image index follows a flash bubble through its pop to the
delivered flash. Only enabled effects enter the pool, and video bubbles require
available local playback capacity, nonstrict floating playback and a 30 second cap.
There are at most four effect bubbles inside the existing eight bubble pool.
Gentle entrances and exits remain at least 750 ms. Reduced motion disables pop
expansion and particles. Panic clears the existing host and pending effects.

## Original recorded pop sounds

The same pinned `Services/BubbleService.cs` blob selects one of `Pop.mp3`,
`Pop2.mp3` and `Pop3.mp3` in `PlayPopSound`. `ModResourceResolver.ResolveAudioPath`
resolves that pool under `Resources/sounds/bubbles`. All three files were verified
against their pinned public raw bytes before copying unchanged to
`Starter/Audio/Bubbles`. No recording was generated or transcoded.

| File | Bytes | SHA-256 |
|---|---:|---|
| Pop.mp3 | 5065 | f6ed355d41899fe7af6b3330bd2e5156d2c13244253523b6ecdc6a61db8a596d |
| Pop2.mp3 | 3811 | f102b4354a0ac9f68dcad314ff0b6cd4b309cd12aceedde75cbe03fc2425d0be |
| Pop3.mp3 | 3811 | 54e87d407679cd7ce7f29b967f502860e7e39ba5bca4ef789b5e6702204b6493 |

The preexisting MP3 LFS rule applies before the first Classic commit. The existing
Starter content glob includes the recordings in build and published packages.
Playback keeps Classic's LibVLC cue lane, per-sound gain, mute and immediate stop
gate. The temporary synthetic WAV and its generator have been removed. Packaging
requires all three recordings and refuses an LFS pointer in their place.

## Original ambient animation

The same verified `BubbleService.cs` keeps the source's four sway profiles
(lines 4059-4064), random 1-2 DIP per frame rising speed (3344-3363), breathing
amplitude .06 (4298-4310), and regular pop at +.04 scale, +2 degrees rotation
and -.066 alpha per 30 Hz frame (3827-3831). Particle travel retains the
source's 2-6 DIP velocity, .15 DIP gravity and -.06 alpha per frame (3834-3843,
4444-4515). The native port advances those formulas by elapsed time rather than
depending on a WPF timer. The source shared compositor
`Services/Compositor/BubbleLayer.cs` (205-220) rotates/scales the glass while
keeping its image face and label upright; Classic uses the same ordering.

Ordinary bubbles enter from below the viewport. Motion is measured in DIP on
each visible host and shares its rendered circle with hit testing. Gentle
halves the travel and breathing, uses a 750 ms soft pop and omits particles.
Reduced motion freezes travel and uses a 120 ms opacity exit. Performance mode
halves the bounded particle count. Changing a motion policy during a pop keeps
its current scale and opacity continuous. The eight-bubble/four-payload limits
and immediate panic cleanup remain in place. No new image assets were copied.

## Original variant sprites and local image payloads

The nine variant sprites below were subsequently verified against their exact
public pinned git blobs and copied unchanged from
`ConditioningControlPanel/assets/Chaos/bubbles/` into `Assets/Bubbles/`.
All existing PNG binary rules were already in place. No private assets were read.

| File | Public git blob |
|---|---|
| flash.png | c6fd6f7459efac31176f772419bd8d50ad413622 |
| subliminal.png | 57fa793489bb7b7c2f7e06bd4aec92bd6345b344 |
| pink.png | cd75fe50ce5e82332bff14411f121a969a054db7 |
| spiral.png | 4e054dfd3f2a18261b93be32ab272ffbcd1bee6c |
| braindrain.png | c2ba5c4074460b528968aa4c6028a3680a15e7d1 |
| glitch.png | c2ba5c4074460b528968aa4c6028a3680a15e7d1 |
| htlink.png | 68c0423e4122417bb4189f67e95b809920451903 |
| video.png | 572e37da23341195851848e78e9c8765f6d451ad |
| braindrain_melt.png | 6e9df0dfabf44e55573653a4808a15201b649704 |

Source `Services/Chaos/ChaosBubbleVariants.cs:715-786` supplies the original
size bands,75% visual scale, extra70% video/cascade scale and7second ambient
treat lifetime. `BubbleService.BuildTriggerSpec` supplies fixed200DIP Glitch
and220DIP melt. `Services/Chaos/BrainDrainBubble.cs` supplies the slow violet
melt pulse and10second existing desktop melt. The old indigo Brain Drain
variant is a faint image wash and is kept as a separate optional choice.
New enum values append to the original Classic values so saved Video6 is stable.

`Services/Chaos/EffectPayload.cs:128-161,396-419`,
`Chaos/ChaosFlashOverlay.cs:185-199,296-301`, and
`Chaos/ChaosGifCascadeOverlay.cs:319-333,455-473,489-548` supply the local visual
payloads. Glitch is the original30% image/GIF wash for3.96seconds, with500ms
entrance and700ms exit. The older wash is10% for10seconds. Cascade spawns at
1.67persecond for6seconds,400DIP width,45% initial scale,3.6DIP/16ms falling
speed with70-130% variation,90% opacity, and growth to full at75% screen depth.
At most14 tiles survive at once,7 in Performance mode. A120second hard bound
also drains abandoned/stalled instances. They reuse Classic's already bounded
local image/GIF cache and selected pool. No URL, download or new media decoder
path was imported. Reduced Motion freezes current tiles and fades them out;
new static tiles start within the screen rather than above it.

Classic always renders bubbles and these payloads in one bounded shared host,
which already supplies the source Solid Mode stability behavior. It does not
offer a nonfunctional switch back to per-bubble native windows. Rain and
Spiral In apply to plain ambient bubbles; effect bubbles keep the source's
benign Float Up behavior. Source game-only live/fuse/skill/reward mechanics
and the retired magnet variant remain outside the ambient feature.
