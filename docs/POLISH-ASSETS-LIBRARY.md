# Library presentation reuse

The Library uses the public CCP asset manager's folder rail, thumbnail wall,
checkbox overlays, media previews and darker filename strip. The original WPF
layout is adapted to Avalonia controls, with twelve items per page and two bounded
image decoders. Existing Classic local media validation and playback are reused.

Read-only source: `CC-Labs-it/Conditioning-Control-Panel`, commit
`fe40ea7587ac0b09e47d0724e59d7178db3336da`, MIT, CodeBambi.

- `ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml`: folder/browser split,
  thumbnail card, checkbox placement, filename strip and preview interaction.
- `ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs`: interaction-driven
  feedback on a working surface instead of a continuous background animation.

Placeholder art uses the already verified original flash, video and audio feature
tiles. Thumbnails show user-selected local files; no user media is copied into the
application or this repository. Starter previews use the existing verified pack.
Audio playback requires an explicit Play action. Video previews are silent at 1x.
