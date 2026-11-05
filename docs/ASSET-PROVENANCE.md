# Asset provenance

These dashboard and branding assets were copied byte-for-byte from the public
`CC-Labs-it/Conditioning-Control-Panel` repository. No private source, mod override
or persona asset directory was used.

Source ref: `origin/main`.
Source commit: `fe40ea7587ac0b09e47d0724e59d7178db3336da`.
Reuse is authorized by the owner for Classic under MIT. Historical upstream
attribution is retained in THIRD-PARTY-NOTICES.md and the unchanged source notice
at packaging/notices/CCP-UPSTREAM.txt. Classic's own LICENSE names CC labs Srls.

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

## Original companion tube

The owner-selected CCP Default tube is reused unchanged from the public source at
`fe40ea7587ac0b09e47d0724e59d7178db3336da`, under the owner's Classic MIT reuse
instructions. These public bytes differ from the older optimized local checkout;
each download was verified against its public Git blob before entering this repo.
The files use LFS. All three are 192 x 309 transparent GIFs with 120 frames.

| Classic asset | Public source | SHA-256 | Bytes |
|---|---|---|---:|
| `Assets/Companion/idle.gif` | `Resources/avatar0_emotes/idle.gif` | `9a8a1707902c1392d827c8b987a5150deb194818c33dd52fe50fbb0bff6602aa` | 2114976 |
| `Assets/Companion/talkA.gif` | `Resources/avatar0_emotes/talkA.gif` | `5588d0ff0a05a5fee9934a0fd196a760f47ec4aec8c8f8299e4d639cc7c85859` | 2130010 |
| `Assets/Companion/greeting.gif` | `Resources/avatar0_emotes/greeting.gif` | `84cc43cbf520a38623d8d9bfcea452a1be6f269dd78ec022ae9d95f7cc9eaa3a` | 2126396 |

The fresh native host decodes one frame at a time and honors the session's motion
gates. Only the five owner-approved words can enter its bark display.

### Full avatar0 emote set (owner 2026-11-03, animation parity)

The rest of CCP's avatar0 clips came from the public repository's `release/7.2.2` branch at
`5ad49a0c3fea777e32a065badee8a40bad2ed4a6`. Each file's SHA-256 matched its committed LFS pointer
before it was copied; the three clips above are byte-identical there. Every clip is a 192 x 309
transparent GIF of 120 frames at 60 ms. `idle2.gif` is not copied: no CCP rule names it.

| Classic asset | Public source | SHA-256 | Bytes |
|---|---|---|---:|
| `Assets/Companion/adoring.gif` | `Resources/avatar0_emotes/adoring.gif` | `4c0f158a12a13bb122003530d4a6121af34bccc5db65c29552e151e6d52f0848` | 2040016 |
| `Assets/Companion/blowkiss.gif` | `Resources/avatar0_emotes/blowkiss.gif` | `b61a6beca0df2c55f760ea729f2af2fd44b9103f796a6ca8812a57af9ea8d7d1` | 2066184 |
| `Assets/Companion/celebrate.gif` | `Resources/avatar0_emotes/celebrate.gif` | `343ec1a4ad07fe479825857e8b8660d35460244dac2832f9861fb44ccdfa4447` | 1996237 |
| `Assets/Companion/cheer.gif` | `Resources/avatar0_emotes/cheer.gif` | `dddd46669be5d7181135e07134b5584b16442262a83b2972196264ff9f490b8b` | 2133779 |
| `Assets/Companion/comehere.gif` | `Resources/avatar0_emotes/comehere.gif` | `e35490f3ff51cd4c2b62a25862fa1a30caa7d03cf65b90a41a83bceeb0c47914` | 2081113 |
| `Assets/Companion/denial.gif` | `Resources/avatar0_emotes/denial.gif` | `ca7a02b0724b51006189fafa8c49459f22aab0bc6772bd8d161f2f18abfce4f7` | 2095954 |
| `Assets/Companion/entrancing.gif` | `Resources/avatar0_emotes/entrancing.gif` | `b3f2fc12a998755f938666ff535a9e60e51426099bd42c6e3baeff9b29cb6995` | 2076403 |
| `Assets/Companion/flirt.gif` | `Resources/avatar0_emotes/flirt.gif` | `05b2de8e8d34294c855a18aac980e257d68e90b124d0141cac76b6515fc47a2f` | 2029689 |
| `Assets/Companion/giggle.gif` | `Resources/avatar0_emotes/giggle.gif` | `228682c0af3d2a0b57c78254db14369dbaf42191d7d0b6f3cad9038fb2708379` | 2076761 |
| `Assets/Companion/idle3.gif` | `Resources/avatar0_emotes/idle3.gif` | `ce42a54556c0a6d3447e63a42ccb4c1a2ccf287f6e9fecb268d1e8622e63f5b5` | 2114789 |
| `Assets/Companion/idle4.gif` | `Resources/avatar0_emotes/idle4.gif` | `0eadd0ced42b225055f1ecf564523eb9abfb32dcc448c122d95ae0edfa035218` | 2133024 |
| `Assets/Companion/idle5.gif` | `Resources/avatar0_emotes/idle5.gif` | `d354d54cdf7a3d027026f401f154cf6b67e88c028ed5f78dfc4cc9cfa0c7e6b3` | 2129721 |
| `Assets/Companion/praise.gif` | `Resources/avatar0_emotes/praise.gif` | `7b23fdd17894c89ff674c1901fa1afff0a87e4a19b2738c794846e272ba21c7f` | 2089696 |
| `Assets/Companion/shy.gif` | `Resources/avatar0_emotes/shy.gif` | `9e09b0e87f0382734d1322e0f5fd0626758649ef0298072f11dc7a0cc0b731bf` | 2091554 |
| `Assets/Companion/sultry.gif` | `Resources/avatar0_emotes/sultry.gif` | `d0edf61a4145a3b84e1ffa45956c8d99df599ef53f4f0606c938a1d3bf28a6f0` | 2130747 |
| `Assets/Companion/surprised.gif` | `Resources/avatar0_emotes/surprised.gif` | `c2e61b75173180585a8060aeb65fea561b494e526d4ab3154572db52d5deaeac` | 2081871 |
| `Assets/Companion/talkB.gif` | `Resources/avatar0_emotes/talkB.gif` | `0b0299cc0d466767a2e93a2336e5e95700381c3cf1f49d757067ccffd6b3e6b9` | 2143246 |
| `Assets/Companion/talkC.gif` | `Resources/avatar0_emotes/talkC.gif` | `06ca41a5eb19aaa873aeca1791c4c8fa7e80165f4b2c6d0a951d14824c864a9e` | 2152211 |
| `Assets/Companion/talkD.gif` | `Resources/avatar0_emotes/talkD.gif` | `a5fc14ffd0d0c60a35ef63ac5d6bcef60c58137fbab8d2963883e811c2ad8902` | 2106779 |
| `Assets/Companion/talkE.gif` | `Resources/avatar0_emotes/talkE.gif` | `361c043ca0c383e62476aca8ca73c444b1be4a9736c1473ad9f99ba6d2a1351e` | 2094065 |
| `Assets/Companion/talkF.gif` | `Resources/avatar0_emotes/talkF.gif` | `f14f74fcfdb96a78adc2adf0cb85690cbaeb81b3b12ac259aa6489a2b768fe72` | 2171040 |
| `Assets/Companion/tease.gif` | `Resources/avatar0_emotes/tease.gif` | `9374812f1c49827dc758a0d281bb4eb1aaea1c47ce2aa9da56ce69976be19141` | 2098853 |
| `Assets/Companion/tender.gif` | `Resources/avatar0_emotes/tender.gif` | `d2dceee9d9a280d3d96fde1da1809adcb50c6eb7f9fbed34be55fda1a82fa2cb` | 2057866 |
| `Assets/Companion/wink.gif` | `Resources/avatar0_emotes/wink.gif` | `7dccbb06920e4fb7ab03fd85d9fe754a49b0b7ed73d5a10d9ec327f78e42bea0` | 2127109 |

`Assets/Companion/emotes.json` and `clip_timing.json` are trimmed from CCP's
`Resources/avatar0_emotes/emotes.json` and `clip_timing.json` at the same commit: the rule values
(fade, idle weights, talk and expressive pools, pacing, click emotes, stem and mood maps, speaking
windows) are unchanged; comments, `fps`, `longMinSec`, `clipScale`, `layout` and the `confirmed`
flags are dropped, and `clipMs` (7200) records the shared clip length. Classic plays every clip at
80% of its GIF rate (owner request), so a clip lasts 9.0 s.

The glass tube art around the avatar is CCP's `ConditioningControlPanel/Resources/tube.png`,
reused unchanged (identical bytes at `fe40ea7587ac0b09e47d0724e59d7178db3336da` and
public main `c21d38193746fcc61c2406186ae1936a14571906`). It is a 2048 x 2048 RGBA PNG,
stored as a plain binary (not LFS).

| Classic asset | Public source | SHA-256 | Bytes |
|---|---|---|---:|
| `Assets/Companion/tube.png` | `Resources/tube.png` | `c7452e21815caa275d4c7048271bf2da285e1aa3b904c366fd1322e090ceafae` | 855613 |

Classic draws it on CCP's tube design canvas (780 x 1020 units, tube at y 150,
780 x 780; avatar feet at y 870, centred at x 329.5) and shows the band x 229 to 430
that holds the art, the avatar and her bubble.
