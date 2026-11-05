# Native Flash Remix

Reference: public CCP commit `fe40ea7587ac0b09e47d0724e59d7178db3336da`,
`Services/Remix/JackpotRemixDirector.cs`, `JackpotRemixBuilder.cs` and
`Resources/web/remix/engine/`. The pinned files were read without modifying the lift source.

Classic preserves the scheduled one-in-100 roll, carried hit, quiet preparation,
20-second checks, ten-minute failed-build backoff, GIF-only shuffled selection,
12 MiB compressed source limit, cycling to eight tiles, and centered geometry.
The owner-approved local feature has no account or purchase gate.

The native composition retains the source three-second, 45-frame, 15 fps clock,
seed mixing, auto-layout weights, clean-second and effect-coverage budgets,
all 16 layout formulas, cover-fit crop, transforms, internal gutters, plates and
sparse-layout backdrop. Canvas effects are the source tint wash/creep,
spiral over/through, ghost/double glitch and focus ring/dark. Automatic Jackpot
composition never selected the separate caption/drain blocks. Clean, snap and
seamless loop endings are implemented. The source snap word is `drop`.
No watermark is drawn, matching the original Jackpot builder.

Classic uses an existing native Skia image compositor instead of WebView2 and
an encoded temporary GIF. One frame is rasterized on demand, at most 15 times
per second, into a single bitmap up to 480 pixels per edge. This avoids the
browser, export/quantization step and generated-file cache. Pure layout and plan
results have golden comparisons captured from the pinned JavaScript engine.
This is not a claim of identical browser/Skia antialiasing or text rasterization.
The snap word uses bundled Inter instead of the source system font stack.

Only explicit selected local GIF paths are opened. Each GIF uses the existing
verified local decoder and retains its frame disposal and timing, resampled on
the source 15 fps strip clock. Resolution adapts to a 4 MiB frame budget per
unique GIF, with at most eight unique GIFs, one prepared scene, one active scene
and one preparation worker. No source files are copied. Preparation checks
actual free system memory using the shared 75 percent system RAM policy and
1.5 GiB reservation. Stop cancels the worker and rejects late publication.

Remix uses the existing Flash fade, opacity, audio lifetime and click exit.
It creates one centered cue per scheduled hit and never creates Hydra children.
Gentle, Reduced Motion and Performance freeze its animation and prevent new
Remix selection. These are Classic's explicit motion preferences.

Validation: seeded plan/layout golden tests, all-layout finite geometry checks,
native block rendering, GIF strip timing, motion freeze, late-worker cancellation,
centered scheduled cue and no-Hydra tests. `RemixProofTests` writes a 16-layout
native montage when `CCP_CLASSIC_PROOF_DIR` is set, using the pinned public test
GIFs when available or generated test frames otherwise. Root coordinates the
Release execution and final integration evidence.
