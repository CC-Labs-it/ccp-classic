# Existing feature parity review

Audit date: 2026-11-03. This is a gap inventory, not a completion claim.
Classic baseline: preview.6, `88ce043e9b6c01ba00d13407eaaa405508bf43b2`,
plus the explicitly named in-progress patches below. Update rows as owners land changes.
Reference: public [CCP commit fe40ea7587ac0b09e47d0724e59d7178db3336da](https://github.com/CC-Labs-it/Conditioning-Control-Panel/tree/fe40ea7587ac0b09e47d0724e59d7178db3336da/ConditioningControlPanel).
Every source path below is relative to that commit's `ConditioningControlPanel/`.
Initial files were read through the public connector; subsequent ports use git show
at the same pin. The original working tree is not an implementation source.

Scope: the 12 Classic effects, Library, Companion's existing local behavior and
typed triggers, and Toys. Paid gates in CCP do not exclude local functionality.
Accounts, downloads, cloud providers, games and other feature families remain out.
Owner explicitly chose typed input only: no microphone/voice completion or screen
reading/OCR. Existing approved neutral defaults and user-authored text only.
Camera/gaze controls are recorded separately below; they are not implemented.

P0 identifies a different core behavior or a specifically requested missing editor.
P1 identifies missing controls or common runtime behavior. P2 identifies remaining
local advanced controls. Owners are assignments, not claims of completion.

## Current validation and remaining work

All main controls below are integrated. All 774 Windows Release checks pass with
zero skips (`artifacts/test-results/parity-release-verified.trx`), including native
Remix and wallpaper renders, actual desktop capture, selected/default audio output
identity and offline FFmpeg analysis. Linux CI is pending for this checkpoint.
The remaining gaps are recorded below. Preview.6 does not contain these changes.

## Flash Images

Source: `Features/FlashFeatureControl.xaml:82-468`, its `.xaml.cs`,
`Features/VisualsFeatureControl.xaml:54-143` and its `.xaml.cs`.
Runtime: `Services/Flash/FlashService.cs`, `FlashMotion.cs`, `FlashDelivery.cs`,
`FlashDrag.cs`, `FlashShatter.cs`, `FlashCorners.cs` and `FlashPendulumRig.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| FlashFrequency; SimultaneousImages; SimultaneousImagesRandom/Min; HydraLimit | Source burst count/random range, 180/hour ceiling and bounded Hydra capacity implemented. | Implemented / Root |
| ImageScale; FlashOpacity; FadeDuration; FlashDuration | Source 50-250% scale fits 40% of the monitor with image aspect preserved; duration/fade/opacity are editable. Schema1 settings and presets migrate old size values. Lazy metadata now refreshes geometry. | Implemented / Root + local_library |
| FlashClickable; FlashStayUntilPopped | Click dismissal and stay-until-popped implemented with stop cleanup and bounded capacity. | Implemented / Root |
| FlashExitStyle; FlashShatterEnabled | All original exits plus original shard cuts/physics integrated. Pendulum shatter freezes the existing pose; exits and shards share a 32-item transient cap. | Implemented / Root |
| CorruptionMode; HydraLinkedTiming; FlashGlowEnabled; FlashSolidMode | Existing Hydra is source CorruptionMode. Linked children use the parent total lifetime; independent children get a fresh lifetime. Real blurred halo is toggled and gated by performance. Classic always uses the shared compositor, so no inactive alternate-host switch is exposed. | Implemented / Root |
| FlashAvoidCenter; FlashCenterExclusionPercent | Pinned adaptive weighted-band placement implemented, using a square on the short monitor edge. This global preference remains outside presets. | Implemented / Root |
| FlashMotionStyle; FlashDriftSpeed; FlashDriftBounce; FlashPendulum; FlashDraggable; FlashRoundedCorners | Original drift/pendulum/spacing/drag/fling helpers wired with motion preferences, radius and speed controls. | Implemented / Root |
| FlashGifSpeedMultiplier; FlashAudioEnabled | Native GIF timing and independent flash GIF speed implemented. One approved or user-local recording per batch holds images through actual completion; linked Hydra does not replay the clip. | Implemented / Root |
| JackpotRemixEnabled | Pinned seeded composition, all 16 layout formulas and native effects renderer are integrated. Golden JavaScript comparisons, bounded worker/lifetime tests and a 16-layout native proof pass. Browser/Skia raster differences are explicit in PARITY-REMIX.md. | Implemented / local_library + Root |
| FlashGazePopEnabled; FlashGazeLingerEnabled/ExtensionMs | Camera/gaze is outside the retained keyboard and pointer input scope. No camera capability is claimed. | Input scope exception / Root |

## Mandatory Videos

Source: `Features/VideoFeatureControl.xaml:79-385` and its `.xaml.cs`.
Runtime: `Services/Video/VideoService.cs` and `AttentionTargets.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| VideosPerHour; MandatoryVideosEnabled; StrictLockEnabled; LockdownStrictHold | Frequency and strict close blocking retained. Source corner mute and nonstrict close now exist. Authoritative panic/app-close deliberately replace CCP's strict Escape grace door, per owner direction. | Implemented / Roadmap; policy exception explicit |
| VideoMinDurationSeconds; VideoMaxDurationSeconds; VideoCutRandomStart | Known short clips are filtered; unknown metadata stays eligible for its first cycle like CCP. Seconds cut narrows the existing actual-playback cap. Initial random seek leaves a whole slice and is excluded from allowance; all playback stays1x. | Implemented / Roadmap; native cap checks retained |
| MandatoryVideoFloating; SizePercent; SpeedPercent; Count; Together; KeepSound | Original pure drag/throw helper, size15-60%, throw speed10-100%,1-4 windows, together launches and sound recency are implemented. Visible unfocused clips continue; hidden/minimized views pause. | Implemented / Roadmap |
| VideoTargetMonitor | Per-video follow/all/index picker and shared-decoder monitor mirrors are implemented. | Implemented / Roadmap |
| AttentionChecksEnabled; AttentionDensity/Lifespan/Size; RandomizeAttentionTargets; AttentionPool | Original moving clickable text targets replace the counting prompt. Count/randomization, lifetime, size, pool, font, outlined/gradient panel and border options are implemented. Mirror hits share one model. | Implemented / Roadmap; native render proof |
| MandatoryVideoReplayAfterFailedCheck; MercySystemEnabled; MercyAfterFails | Failed checks replay according to the setting; strict replays remain bounded by optional2-10 attempt mercy. The source10% surprise replay shares the same policy. | Implemented / Roadmap |
| VideoGazeClickEnabled | Camera/gaze is outside the retained keyboard and pointer input scope. | Input scope exception / Root |

## Subliminals

Source: `Features/SubliminalFeatureControl.xaml:79-174` and its `.xaml.cs`.
Runtime: `Services/Subliminal/SubliminalService.cs`, compositor `SubliminalLayer.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| SubliminalFrequency; Duration; Opacity; Enabled | Controls present. Classic retains its existing 1-12 frame range and neutral defaults while covering the source 1-10 range. | Implemented / Root |
| SubliminalPool; UserAddedSubliminals; RemovedDefaultSubliminals | Editable local phrase pool, add/remove and approved neutral defaults implemented. No source persona defaults were imported. | Implemented / Root |
| SubliminalFont; SubliminalSolidMode | Installed-font selection is wired. The shared native compositor provides the stable source solid-host path without a redundant backend toggle. | Implemented / Root |
| SubAudioEnabled; SubAudioVolume; SpokenClipRule | Independent whisper gain, explicit local mapping and filename/approved-recording resolution implemented. No synthetic speech. | Implemented / Root |

## Spiral and Loom

Source: `Features/SpiralFeatureControl.xaml:83-212` and its `.xaml.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| SpiralEnabled; SpiralOpacity; local Loom creation and selection | Opacity plus saved procedural Loom compositions exist. This does not replace the source local GIF spiral library. | Existing Loom / no action to remove it |
| SpiralPath; SpiralRandomize; folder refresh/select GIF | Local GIF/still picker, explicit folder pool, one-session random selection without immediate repeats, real animation and refresh implemented. Original starter GIFs are packaged. | Implemented / local_library |
| SpiralTargetMonitor | Per-effect monitor selection implemented; existing procedural/Loom picker remains functional alongside GIFs. | Implemented / local_library + Root |
| SessionCornerGifAllowed; corner GIF editor | Two original corner slots with local file/fallback, position, size, opacity and stagger implemented. Automatic program-day cues belong to excluded program features. | Implemented / local_library |

## Pink Filter

Source: `Features/PinkFilterFeatureControl.xaml:81-132` and its `.xaml.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| PinkFilterEnabled; PinkFilterOpacity | Present. Classic keeps 0-60%, covering the source5-50% range while retaining fully-off opacity. | Implemented / Root |
| PinkFilterColor | Editable validated color and visible swatch implemented. | Implemented / Root |
| PinkFilterUnderEffects | Layer-order choice implemented in the actual renderer. | Implemented / Root |
| PinkFilterTargetMonitor | Per-effect target routed independently from the global host selection. | Implemented / Root |
| Classic-only follow setting | Removed the inactive setting and its misleading help text. | Closed / Root + local_library |

## Bubble Pop

Source: `Features/BubblePopFeatureControl.xaml:86-307` and its `.xaml.cs`,
`Services/BubbleService.cs`, `BubbleFace.cs`, `AmbientBubbleMotion.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| BubblesEnabled/Frequency; original sprite; click pop and recorded Pop/Pop2/Pop3 | Source60/minute range and all three unchanged recorded pops are implemented, with original sound assets verified. | Implemented / production_audit and Root |
| BubblesSize; BubbleSpeedBoost; BubblesVolume; solid/input mode | Size, speed and independent gain are implemented. Classic always uses one bounded shared host, supplying source Solid Mode stability without a dummy switch to per-bubble windows. | Implemented / Roadmap and Root |
| BubbleMotionStyle; local Rain/SpiralIn and other styles | Plain FloatUp/Rain/SpiralIn/Mix, source sway/spin/breath/pop/ballistic particles and reduced/gentle/performance gates are implemented. Effect bubbles retain benign FloatUp. | Implemented / Roadmap and Root |
| BubbleTriggersEnabled/Chance; BubbleTriggerVariants | Arbitrary enabled subset, Glitch image wash, local animated cascade and distinct old image-wash/melt variants are implemented. Source tile counts/sizes/timing and selected local media pool are retained. | Implemented / Roadmap; current checks pending |
| BubbleBrainDrainEnabled; original effect faces and transitions | The real10second melt uses the desktop capture worker. Nine exact public sprites, source sizing/lifetime and slow violet pulse are implemented. | Implemented / Roadmap; visual proofs |
| BubbleGazePopEnabled | Camera/gaze is outside the retained keyboard and pointer input scope. | Input scope exception / Root |

## Lock Card

Source: `Features/LockCardFeatureControl.xaml:89-240`, its `.xaml.cs`,
`Services/LockCard/LockCardText.cs`, `LockCardService.cs`, `Windows/LockCardWindow.xaml.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| LockCardPhrases | Enabled editable phrase pool with approved defaults, fullscreen mirrored typed cards and a real preview implemented. | Implemented / Root |
| LockCardFrequency; LockCardRepeats; LockCardStrict | Source1-10/hour rate and first-delay/jitter scheduling integrated; old minute intervals migrate. Strict card gates preserve authoritative panic/app-close behavior. | Implemented / Root |
| LockCardRandomRepeats/Min; TargetLengthEnabled/Length/Variance | Random range and target typed-character budget controls implemented; uses source TypedLength separately from answer normalization. | Implemented / Root |
| LockCardResetOnTypo; LockCardStrictCopy; normalization/matching | Reset-on-typo, paste/undo/drag restrictions and pinned punctuation/whitespace/case matching implemented. Tests cover manual cards with schedule disabled. | Implemented / Root |
| LockCardVoiceMode; microphone consent | Deliberately excluded by owner. Keep typed completion only. | Approved exception / Root |

## Bouncing Text

Source: `Features/BouncingTextFeatureControl.xaml:91-305`, its `.xaml.cs`,
`Services/Subliminal/BouncingTextService.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| BouncingTextPool | Editable enabled pool, add/remove and bounded custom text are implemented. Defaults remain the five approved neutral words. | Implemented / Roadmap |
| Speed; Size; Opacity | Source1-10 speed,50-300% of72DIP size and10-100% opacity now use actual measured native glyph bounds. Saved legacy sizes migrate. | Implemented / Roadmap and Root |
| ColorMode random/fixed/rainbow; FixedColor; Font; Outline | Original palettes/rainbow, fixed color/reset, installed fonts plus verified bundled Fredoka and native outline are implemented. | Implemented / Roadmap; native proof |
| SecondText; BounceAsync; AlwaysOnTop | Two independent text lines and explicit show-over-videos policy are implemented. Each line has separate motion/color/effect state. | Implemented / Roadmap |
| FxBreathing; FxWobble; FxSpin; FxVelocityTilt; FxSquashStretch; FxCornerBurst | All six original formulas and corner ring are ported, with reduced/gentle gates. Source15DIP near-corner tolerance replaces the old exact-corner substitute. | Implemented / Roadmap |
| Ordinary wall hits and rewards | Each wall hit emits one haptic and uses the source shared 15 XP, two-second cooldown and 150 XP/minute cap. Corner achievements stay separate. | Implemented / Roadmap + Root |
| Multi-monitor travel | Classic currently mirrors local normalized positions; source travel crosses the full virtual desktop. | Remaining / Roadmap |

## Mind Wipe

Source: `Features/MindWipeFeatureControl.xaml:87-162` and its `.xaml.cs`,
`Services/LockCard/MindWipeService.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| MindWipeEnabled/Frequency/Volume/Loop | Source frequency 1-180/hour, volume and loop are implemented. Schema 1 minute intervals migrate to hourly rates. | Implemented / Root |
| MindWipeAudioPath; custom select/reset | Dedicated local file selection/reset is implemented, with Library or approved starter fallback when unset. | Implemented / Root |
| MindWipeRunRule | This is internal source lifecycle policy, not an editor. Classic starts only with a session, follows live enable/loop/mute flags, releases failed reservations, and advances on actual clip completion. Existing lifecycle tests cover disabling and late completion. | Implemented / Root |

## Brain Drain

Source: `Views/Controls/Studio/BrainDrainFeatureControl.xaml:161-399`, its `.xaml.cs`,
`Services/Compositor/BrainDrainLayer.cs`, `BrainDrainCapturePump.cs`,
`Services/LockCard/BrainDrainService.cs`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Live desktop capture and intensity-driven blur | Original Windows GDI desktop capture and native blur implemented. Native memory reuse/stride probe passes; desktop content is never saved. Linux explicitly reports this capability unavailable. | Implemented Windows / Root + local_library |
| BrainDrainIntensity; BlurStrength; MeltEnabled | Independent audio rate, visual blur strength and cached Perlin melt implemented, including real10-second bubble pulse. | Implemented / Root + local_library |
| KeepPicturesClear; AllowOverlayCapture | Keep-clear layer ordering and capture exclusion policy implemented; plain SRCCOPY avoids recursive layered-overlay capture. | Implemented / local_library |
| BrainDrainHighRefresh | Original audio scheduling cadence and bounded native capture clock are wired. Balanced capture is30fps, performance15 and motion-limited10. | Implemented / local_library |
| BrainDrainVolume; local braindrain folder, clip count, open/refresh | Independent clip gain and local-folder scan/refresh implemented; active recording is not restarted, and stop shuts audio down first. | Implemented / Root + local_library |

## Audio

Source: `Views/Controls/AppSettings/AudioSettingsSection.xaml:82-275`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Master and video gain; ducking toggle/amount | Independent duck-enable setting implemented and wired across existing audio lanes. | Implemented / production_audit + Root |
| Output device list/refresh; test playback | Real DirectSound GUID enumeration and LibVLC selection/default reset verified on Windows with exact native output identity and muted recorded playback. Linux retains the existing Pulse path. DirectSound remains forced; no mixer fallback. | Implemented / production_audit + Root |
| Layered audio editor | Missing local layered playback/mix editor. Existing separate internal cue lanes are not a user editor. | P2 / unassigned |
| AudioSync latency and intensity | Source-time offset, live intensity and four-pass source DSP implemented in Toys. | Implemented / production_audit |
| Cloud-specific duck exclusion | Excluded with cloud/network providers, not a missing local control. | Scope exception |

## Wallpaper

Source: `Services/Video/WallpaperService.cs:126-170,301-337`.

| Source properties / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Capture original, apply local image, restore on stop/crash recovery | Classic implements journaled restore and guarded writes. Keep those protections. | Present restore contract |
| WallpaperSourceFolder; random selection and Shuffle | Dedicated folder/own image/random nonrepeat/shuffle and bounded rotation implemented with generation-safe restore. Fake writer/cover tests added; no real wallpaper changed. | Implemented / production_audit |
| Local collage support | Original three static plans and native moving GIF/WebP collage now share geometry, feathering, focus, hero sharpener and color passes. Moving scenes retain source19-tile selection, frame budgets and animation timing; build/resource/cancel/offscreen proof checks added. | Implemented / production_audit and Roadmap; current verification pending |
| Classic dynamic video mode | Present as an additional local path with native video. Verify it separately; it does not supply static shuffle/collage parity. Linux wallpaper remains absent by design. | Platform exception / Root |

## Library

Source: `Views/Tabs/AssetsTabView.xaml:95-136,734-950`.

| Source controls / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Local folder choice, refresh, tree selection, media count, thumbnail previews | Bounded hierarchical tree, tri-state folder selection, verified local open-folder, counts/search/previews implemented. Live selection edits replace image payloads and re-filter future videos; folder changes/Refresh cancel and replace scans. Existing videos finish with their captured path. | Implemented / local_library + Root + Roadmap |
| Asset selection presets: save/update/delete | Sixteen bounded named selection snapshots preserve exclusions/folder rules without copying files or replacing roots. | Implemented / local_library |
| Local image/video/audio preview options | Real native GIF animation and existing local video/audio previews are wired with shared stop/disposal rules. | Implemented / local_library |
| Downloaded packs, remote sources/cache/ratios/subscriptions | Excluded by offline/no-account scope. Do not copy server code or private pack assets. | Scope exception |

## Companion and typed triggers

Source: `Views/Tabs/AwarenessTabView.xaml:216-581`,
`Views/Controls/Companion/KeywordTriggersPanel.xaml:72-206`, `Services/KeywordTriggerService.cs`.
Existing avatar placement source: `AvatarTube/AvatarTubeWindow.Windowing.cs`.

| Source controls / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Avatar display, recorded barks, local placement | Original avatar and approved recordings retained; detach/drag/0.5-1.5 scale/topmost and persisted on-screen position added. | Implemented / production_audit |
| Custom typed trigger list, enable/edit/remove/import/test; per-trigger action | Editable plain/regex rules with composable local audio, visuals, haptics andXP implemented. Phrase-pool import avoids duplicate words and resolves approved/local clips. Real Test currently requires an active session. | Implemented with preview limit / Root |
| Buffer timeout; per-trigger/global/same-word cooldown; session multiplier | Bounded ephemeral buffer, source cooldown controls/defaults and sessionXP multiplier implemented. Matching has per-regex and batch time budgets. | Implemented / Root |
| Typed app scope/list; ignore own UI/focus; loop protection | Global and per-rule process scope, own-focus choice and configurable temporal mute implemented. Foreground changes discard partial text. External process identification is currently Windows only. | Implemented Windows / Root |
| OCR/screen highlighting and voice input | Deliberately excluded by owner; typed input only. | Approved exception / Root |
| AI chat, accounts, private persona recordings/content | Excluded. Keep existing approved neutral phrases or text the user writes. | Scope exception |

## Toys

Source: `Views/Tabs/HapticsTabView.xaml:749-886,958-1221,1341-1605`.

| Source controls / behavior | Classic state and concrete gap | Priority / owner |
|---|---|---|
| Local Intiface/Lovense connect, global intensity, six patterns, test/stop | Present with localhost-only endpoints and immediate zero on permitted stop/disconnect. | Present bounded local path |
| Device cards, per-device limits/routing, pattern target selection | Connected-device enabled/trim/nickname/role, event-role and continuous-role routing, targeted manual patterns and real envelope graph implemented. Provider-qualified identity intentionally avoids source fuzzy same-name false merges. | Implemented local controls / production_audit |
| Maximum power; override cooldown; temperament | Source .70 default hard cap, 800ms rise, immediate falls, perceptible floor and original five profiles/concurrency implemented. Source strength-change cooldown depends on speculative Lovense input; no confirmed Intiface strength-change producer. | Implemented controls; input distinction / production_audit |
| Per-event intensity/pattern | Existing transient events have patterns. AudioSync and Video use continuous controls and omit dead patterns; muted video retains its floor. Script has enable/conversion/role; optional independent script scale is the remaining strength knob. No generic layer framework gap. | Optional script scale / production_audit |
| Audio sync delay/power; sensitivity/smoothing/bass/RMS/onset/max | Controls implemented and wired to original chunk-normalized four-pass DSP, including median quiet gate and transient overrides. Tests cover source-time offset, real tones and source pulse rules. Native hardware check remains manual. | Implemented / production_audit |
| FunScript; luminance; band split; device input/attention | Local script/brightness/band split implemented. Pinned ButtplugProviderV2.cs778-818 has advertised Button Subscribe/input callbacks that Classic still lacks, followed by source350ms debounce/attention. Lovense speculative handshake is not functioning parity evidence. | Confirmed Intiface button gap / production_audit |
| Pattern graph/preview and per-toy manual play | Each connected card has actual six-pattern graph, intensity/duration and bounded targeted test. Stop delegates shared policy. Pure mixer, offscreen graph and fake-wire tests added. | Implemented / production_audit |
| Auto-connect | Missing by prior explicit-connect policy. Reconcile user opt-in policy before adding it; never enable automatically. | Policy review / Root |

## Acceptance and ownership

This audit does not authorize importing unapproved default prose/audio, adding
accounts/network providers or introducing excluded input modes. It does identify
local controls hidden behind CCP paid gates as real parity gaps.
Implement only assigned slices, update these rows with concrete behavior/tests,
and keep shared build/package coordination. No broad rerun is needed for an
unchanged feature; changed slices need appropriate Release checks and native
render/playback evidence where applicable.
