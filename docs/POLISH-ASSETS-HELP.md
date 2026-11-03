# Original CCP help animations

The source is the public [Conditioning-Control-Panel snapshot](https://github.com/CC-Labs-it/Conditioning-Control-Panel/tree/fe40ea7587ac0b09e47d0724e59d7178db3336da/ConditioningControlPanel/Controls/HelpLoops), verified through the connected GitHub service on 2026-11-03. The author is CodeBambi. Its `LICENSE` identifies the code as MIT and contains additional source-project terms. Classic's owner explicitly requested this reuse for the fresh Classic application.

Read the Git objects at the pinned public revision. The local source working tree contains older, untracked `.mov` recordings and an outdated tutorial README. Those recordings were not copied. The public revision's README says each former video topic now uses a native drawn loop, which takes precedence over a clip.

## Reused scenes

All paths below are relative to `ConditioningControlPanel/Controls/HelpLoops/Scenes/` in that public revision. Classic preserves the 480 x 270 stage, scene durations, still-frame positions, cursor paths, easing, shapes and palette.

| Public source | Classic topic | Duration / still, ms |
| --- | --- | --- |
| FlashImagesLoop.cs | Flash Images, Library, Getting started | 7000 / 2900 |
| VideoLoop.cs | Mandatory Videos | 8000 / 3800 |
| SubliminalsLoop.cs | Subliminals | 6300 / 1215 |
| SpiralOverlayLoop.cs | Spiral, Loom | 7200 / 3200 |
| PinkFilterLoop.cs | Pink Filter | 7200 / 4400 |
| BubblePopLoop.cs | Bubble Pop, Progress | 7700 / 3700 |
| LockCardLoop.cs | Lock Card | 7600 / 2300 |
| BouncingTextLoop.cs | Bouncing Text | 8000 / 1600 |
| MindWipeLoop.cs | Mind Wipe | 7600 / 3050 |
| BrainDrainLoop.cs | Brain Drain | 7600 / 4300 |
| AudioLoop.cs | Audio | 6800 / 3400 |
| PresetsLoop.cs | Presets | 6800 / 3000 |
| KeywordTriggersLoop.cs | Companion | 7000 / 3300 |

The shared `LoopFrame.cs`, `LoopFrame.Kit.cs`, `LoopMath.cs` and `LoopPalette.cs` are native Avalonia adaptations of the corresponding public drawing kit. `LoopContext` and `LoopGeometry` adapt the original drawing operations without a WPF reference. The background blur uses Avalonia's bounded native effect and leaves the ground layer outside that blur. The font uses Classic's installed font fallback rather than the source's WPF font URI.

Approved wording replaces the original Bouncing Text examples and lock phrase. Scene labels use localized Classic keys. The Audio illustration describes Classic's internal ducking, not the source application's external mixer control. Source-specific reward amounts and currency claims were removed. These are silent illustrations and never start live effects, play recordings, inspect folders, or capture typing.

## Help behavior

`TutorialCatalog` covers all 12 effect editors and Library, Companion, Toys, Presets, Progress and Settings. The guide explains the initial workflow. Existing title keys resolve to their topic; Studio opens the guide and MakeYourOwn opens Spiral. Linux omits Wallpaper. Text instructions describe Classic's implemented behavior instead of copying unrelated paid, account, browser or online features from `TutorialService` or `HelpContentService`.

`MainWindow.Help.cs` supplies `OpenHelp(topicId)` and `HelpButton(topicId)`. It keeps one owned help window, routes key input through the existing shared stop handling, and closes help on a permitted stop or owner close. Closing help alone leaves a session unchanged.

Only the selected scene renders. Hidden or minimized help/owner windows stop its timer. Gentle visuals and Reduced motion display the original scene's still-frame position; manual pause composes with those preferences. Performance mode limits animation to 10 frames per second. Normal animation uses 25 frames per second. The source has no audio and requires no LibVLC, FFmpeg, browser, network connection or additional package.

`TutorialTests` covers scene geometry rendering, a real native bitmap, catalog coverage, owned-window reuse, panic cleanup, visibility, motion composition, topic navigation and localization. The shared build coordinator runs these in the Avalonia UI collection. Screenshots and any temporary public-source inspection copies remain under ignored `artifacts/`.
