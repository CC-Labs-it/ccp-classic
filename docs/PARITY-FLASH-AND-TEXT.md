# Flash and typed effect provenance

These ports use the public CCP MIT source at
`fe40ea7587ac0b09e47d0724e59d7178db3336da`. Paths below start at
`ConditioningControlPanel/`. No original working-tree implementation was used.
Historical upstream attribution remains in the packaged third-party notices.

| Source | Git blob |
|---|---|
| Services/Flash/FlashMotion.cs | 0ed172a5d7b89e2a3e67d196d02db658016539c5 |
| Services/Flash/FlashPendulumRig.cs | fccaa230070b103bf57771f47c9e5b046315a6fd |
| Services/Flash/FlashDrag.cs | 116cd6951a1aa7a2647789059db3c721f03086b5 |
| Services/Flash/FlashCorners.cs | 756a5ce53377c540fd2227123eaa09b3e22ebb5a |
| Services/Flash/FlashShatter.cs | b489f3cf3fcd807026a51f1aab9fae59e1bfc38e |
| Services/Flash/FlashDelivery.cs | aff80b9aec8bffce5e994831bcfe6e565a8e69f0 |
| Services/LockCard/LockCardText.cs | 9ecde2c6c9b4509a6fec50c06a02c6d42c12aa62 |
| Services/MindWipeRunRule.cs | 6d58185bc12173f01cc8f7a292d948ad99223999 |

The original motion, pendulum spacing, drag/fling and fragment formulas run in
Core. Avalonia supplies image composition and pointer capture. Original centered
placement weights and orientation sizing are retained. The shared compositor
always supplies the source solid-host behavior. Exit sprites and shards share a
32-item bound. Gentle visuals and reduced motion retain their existing gates.

Lazy image metadata updates aspect without resetting motion or dragging. Linked
Hydra children inherit the parent's total lifetime and audio completion; independent
children receive a fresh lifetime. Approved or selected local recordings play once
per batch. Stop and mute release the batch without waiting for the recording.

Typed lock matching retains optional sentence punctuation while budgeting actual
typed length separately. No microphone, synthetic speech or OCR was added. Phrase
pools contain approved neutral defaults or the user's text. Global typed triggers
have bounded buffers, cooldowns, foreground app boundaries, bounded regular-expression
matching and explicitly selected local actions. Input text is not logged.

MindWipeRunRule was initially misclassified in the gap inventory. It is source
lifecycle policy, not a missing editor. The dedicated audio picker is implemented;
session, enable, loop and audio gates already own its runtime lifecycle.

Validation includes FlashParityTests, FlashTimingTests, LockEffectsTimelineTests,
SettingsTests (KeywordParityTests left with keyword triggers, removed 2026-11-03). Full integrated results and packaging state
are recorded in HANDOFF.md. Remix has separate composition and renderer evidence.
