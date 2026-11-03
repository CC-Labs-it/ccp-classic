# Asset provenance

These dashboard and branding assets were copied byte-for-byte from the public
`CC-Labs-it/Conditioning-Control-Panel` repository. No private source, mod override
or persona asset directory was used.

Source ref: `origin/main`.
Source commit: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
License: MIT, copyright CodeBambi, retained in the repository LICENSE.

Files were read with Git without changing the source repository. Any LFS object
was accepted only when its size and SHA-256 matched the committed pointer.

The logo is the current `Resources/branding/ccp-logo.jpg` used by the CCP animated
logo control, verified at `ConditioningControlPanel/Controls/AnimatedLogoImage.cs:15`.
It is an original still image; the control supplies its animation.
No fallback logo or regenerated artwork was substituted.

| Local asset | Source path | SHA-256 | Bytes | Retrieval |
|---|---|---|---:|---|
| `flash.png` | `ConditioningControlPanel/Resources/features/flash.png` | `a348d76ac3687874d39be0300b065b44007b6e8134b55b0fa3a86a888f1e8862` | 130894 | Git blob |
| `video.png` | `ConditioningControlPanel/Resources/features/mandatory_videos.png` | `da2cfa0870df0b4e1ef372f659a5182be600428fdd28cf01d7a35fb5cd5dc218` | 511649 | Git blob |
| `sub.png` | `ConditioningControlPanel/Resources/features/subliminal.png` | `0744e50b48d7e7933b98c9c3ffed9e9c03ab7069387dd762db94f7b6ab31962b` | 126513 | Git blob |
| `spiral.png` | `ConditioningControlPanel/Resources/features/spiral_overlay.png` | `fd9318141283ed9613f443da44bfe0a41050d6996290f985cd75576be4b639e2` | 395341 | Git blob |
| `pink.png` | `ConditioningControlPanel/Resources/features/Pink_filter.png` | `4173ea07d5c384ee22420d61bfdee35820618681e251ef367be2f3aedc2e8b13` | 380420 | Git blob |
| `bubble.png` | `ConditioningControlPanel/Resources/features/Bubble_pop.png` | `8109e353773d3a899e087e8b304d671c9b4c84c6bece981155f88ec6819e1b09` | 397083 | Git blob |
| `lock.png` | `ConditioningControlPanel/Resources/features/Phrase_Lock.png` | `00b5cab3ed1275fcd4f5e7ae3053a0e282c98665fe0f7394d02fcf2f05649eac` | 248968 | Git blob |
| `wipe.png` | `ConditioningControlPanel/Resources/features/Mind_Wipers.png` | `7008b20f44bc4b1a3242caeeb70a46205575513f40c1bdcd57146314b027d2ac` | 284685 | Git blob |
| `drain.png` | `ConditioningControlPanel/Resources/features/brain_drain.png` | `de336a345ffcea63b00f028b85439c9e4129437b12105b0d4651b50cee8a4cfb` | 325145 | Git blob |
| `audio.png` | `ConditioningControlPanel/Resources/features/audio_whispers.png` | `ffffeee0364e522c1d5756fe509166a9222870a5e0981ad199e5d1804f88d599` | 148175 | Git blob |
| `wall.png` | `ConditioningControlPanel/Resources/features/wallpaper.png` | `9a9b9893f9ed404250e56e8c8fc6360ba1ea78debe70fd170fcaedec1301fdf3` | 692061 | Git blob |
| `logo.jpg` | `ConditioningControlPanel/Resources/branding/ccp-logo.jpg` | `2a517e3e79903301bed1c0e2dcdc0809b55f7b8f3a9e3b28661220bc8cd2be2a` | 583725 | Git blob |
| `bounce.png` | `ConditioningControlPanel/Resources/features/bouncing_text.png` | `f34951ab7e7bae85fb6374a6fcf357393c30be93e24e23865ed34d30bbaefd91` | 191893 | Verified source checkout matches Git blob |

## Visual review

All 13 candidate images were inspected. Thirteen ship unchanged: twelve tiles and
the current branding logo. No source art was regenerated.

- The owner explicitly selected CCP's original Bouncing Text artwork on 2026-11-03.
  It ships unchanged, including its baked-in wording, replacing the vector placeholder.
- `lock.png` contains "I AM A GOOD DOLL". It has no persona name, but its original
  doll wording is retained visibly in the image.
- Other image text is effect or conditioning wording: WATCH, OBEY, FOCUS, LISTEN,
  WHISPERS, THOUGHTS, WORRIES, BRAIN DRAIN and the program name in the logo.
- The illustrations depict the existing neon mascot. No persona-specific asset
  folder, override, voice recording or private media entered this lift.

The native layered logo renderer was lifted from
`ConditioningControlPanel/Controls/DashboardLogoRenderer.cs` at the same source commit.
Its allocation strategy and original animation maths are retained; the host is
fresh Avalonia code with a single background frame in flight and motion gates.
