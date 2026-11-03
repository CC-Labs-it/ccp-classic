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
