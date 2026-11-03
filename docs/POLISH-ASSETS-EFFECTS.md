# Flash image click outros

Source: public CCP [FlashExit.cs](https://github.com/CC-Labs-it/Conditioning-Control-Panel/blob/fe40ea7587ac0b09e47d0724e59d7178db3336da/ConditioningControlPanel/Services/Flash/FlashExit.cs) and compositor [FlashLayer.cs](https://github.com/CC-Labs-it/Conditioning-Control-Panel/blob/fe40ea7587ac0b09e47d0724e59d7178db3336da/ConditioningControlPanel/Services/Compositor/FlashLayer.cs), verified public snapshot `fe40ea7587ac0b09e47d0724e59d7178db3336da`, By CodeBambi.

Classic previously used its opacity envelope for both image expiry and clicks. The port adds the five free click exits: Pop, TV off, Spiral, Melt and Glitch. Mix uses the original nonrepeating choice rule. None cuts immediately when motion preferences permit; Gentle and Reduced motion retain their soft-fade policy. Timed expiry and disabling an ordinary live image retain the existing soft fade. The separate owned Shatter prize is outside this port.

The original durations (.22/.24/.38/.48/.28 seconds), quadratic easing, swell, inward spin, top-pivot stretch, TV brightness/line collapse, seven glitch slices, seeded slice offsets and pink spark formulas are retained. Avalonia draws the transforms, rounded clips, slices and multiply-tinted red/cyan ghosts directly. No source images, videos or other assets are copied. Render proofs use the existing approved starter spiral.

Gentle visuals uses a .75-second plain fade. Reduced motion uses the original .12-second fade. Performance uses the original reduced variant with fewer sparks and no spin/flicker. Turning a motion preference on during an outro freezes its current pose and fades it continuously. Exits are immutable snapshots, stop clears them immediately, exited images release pointer input, and retained exits are capped at 32 alongside the live-image maximum.

The Flash editor exposes the existing source choice under More, defaulting to Mix. A selected flash-bubble image can be passed explicitly to `TriggerFlash`; the existing opt-in and image-count checks still apply. `TriggerPinkPulse` follows the existing short spiral pulse lifecycle for effect bubbles. Bubble provenance is documented separately in `POLISH-ASSETS-BUBBLES.md`.

`FlashExitTests` covers the source choice/duration contracts, no repeated Mix pick, explicit image selection, expiry versus click behavior, motion gates, immutable snapshots and panic cleanup. Five deterministic native midframe proofs export as `flash-outro-*.png` when `CCP_CLASSIC_PROOF_DIR` is set.
