# Companion barks

Owner request 2026-11-03: give the tube companion every gender-neutral line recorded in
Circe's voice for CCP's CCP Default companion, tied to what the player actually does.

## What ships

- `src/CCP.Classic.Core/Barks/barks.json` (embedded in Core): 108 rules, 285 lines. Each rule
  has an event key (`trigger`), priority, cooldown (seconds), repeatable, chance, `always`,
  mood, optional `when` conditions (`{"fact": [min, max]}`, inclusive) and its lines
  (`text`, `clip`, `seconds` = clip length measured with ffprobe).
- `src/CCP.Classic.App/Starter/Barks/*.mp3`: the 285 recordings, unchanged (64 kbps mono),
  18.3 MB, stored in LFS like the other starter recordings. Packaging checks them for pointers.
- Text and recordings are copied verbatim from CCP's
  `Resources/sounds/companion_audio/mods/builtin-ccp-default/` (`bark_rules.json`, 625 rules,
  702 lines). Lines were regrouped into Classic events; no line was rewritten.

## Selection (BarkEngine, Core, pure)

Ported from CCP's `BarkService` gate:

1. Rules for the event key, highest priority first.
2. A rule is skipped when a `when` fact is missing or out of range, when it is a
   once-per-session rule that already spoke this session, inside its own cooldown, or when its
   chance roll fails. The first rule left wins (a failed chance falls through, like CCP's gated
   idle bias).
3. Global gap: nothing speaks within `min(45 s, Bark interval)` of the last line, and an event
   never replaces a line still showing, unless the rule is `always` (clicks, level ups,
   achievements, lock card results, greetings, timed session end, panic switched off).
4. Line pick: never the last line spoken; inside a rule no line repeats until the rule's pool
   is used up (the idle pool is one rule, so it rotates through all 126 idle lines).
5. Panic: `BarkEngine.Panic()` holds every bark, `always` included, for 30 s; nothing is saved
   up. The panic routes in `MainWindow` call `SilenceBarks()`, which also stops the bark lane.
   A stop already ends the bark lane and talk at once (existing behaviour); a stop never speaks.
6. Never synthetic speech: lines whose recording is missing are removed before anything can
   pick them. Barks off, Avatar off, or the tube off screen: nothing is said.

Session timing words stay the five approved words (Breathe, Relax, Focus, Listen, Deeper) and
keep their trigger phrases. The idle timer and avatar clicks now draw from the library.
Each spoken line raises `DesktopEffectsService.LineSpoken(RuleId, LineId, Mood, Text, Seconds)`
(`LineId` = clip stem, as in CCP; `RuleId` "word" for the five words): the seam for the emote
controller. The bark system itself never chooses an animation clip.

## Event map

| Event key | Raised when | Lines |
|---|---|---|
| AppOpened | first idle frame after launch; facts `first`, `days_away` (0 same day, 1-2, 3-6, 7-59, 60+) | 7 |
| StreakMilestone | launch that extends a daily streak to 7/14/30/60/100/365 | 6 |
| SessionStarted | session start; `hour` (late night 0-3), `sessions_today`, `days_away` (first session of the run), `streak`, `level`, `lifetime_minutes` (0 = very first session) | 12 |
| SessionMinutes | 15, 30, 60, 180 minutes into a session | 12 |
| SessionCompleted | a preset's timed end only (never a stop); `minutes`, `lifetime_minutes` | 5 |
| SessionRefocused | panel re-activated after 20 s or more away during a session | 1 |
| SetupReady | 10-15 s after the player changed switches, no session running | 4 |
| Flash / Subliminal | a flash image / a subliminal appears (chance 0.3) | 3 / 3 |
| VideoStarted / VideoEnded | a mandatory video starts / finishes | 6 / 3 |
| AttentionPass / AttentionFail | a video attention target is hit / missed; `misses` in a row, `streak` | 1 / 3 |
| BubblePopped | a bubble is popped (chance 0.4) | 3 |
| BouncingTextCorner | bouncing text hits a corner (once per session) | 1 |
| LockCardCompleted | lock card finished; `mistakes` 0 or more | 2 |
| MindWipe / BrainDrain | a mind wipe completes / brain drain starts | 1 / 1 |
| KeywordTriggered | a typed keyword trigger fires | 3 |
| PreviewLockCard / PreviewVideo | the lock card or video preview button | 1 / 1 |
| LevelUp | level up; `level` 10/25/50/75/100/500 have their own line | 7 |
| Achievement | achievement unlocked; `achievements_left` 0 for the last one | 3 |
| AvatarClicked | click on the tube (1.5 s poke cooldown); `clicks` in the last minute 20/50/100/150 | 29 |
| Idle | the idle bark timer (Bark interval) between sessions; `level`, `streak`, `lifetime_minutes` gates | 126 |
| Opened.Presets / Studio / Progress | the player switches to that page | 2 / 4 / 1 |
| Opened.Settings / Library / Toys | the player opens that window | 3 / 2 / 1 |
| Opened.Effect.&lt;id&gt; | an effect tile's settings window (flash, video, sub, spiral, pink, bubble, lock, bounce, wipe, drain) | 1 each |
| Enabled.&lt;id&gt; | the player turns an effect on (sub, spiral, pink, drain, bubble, lock, bounce, wipe, wall), Strict video (`strict`), keyword triggers, all monitors, start with system, auto run | 1 each |
| BarksOn / Detached / PanicOff | barks turned back on / tube detached / panic switched off | 1 / 1 / 2 |

Switch events come from `BarkSwitchWatch` (polled every 250 ms): only an off-to-on flip by the
player counts; three or more switches flipping together (a preset, a reset) say nothing, and
timed effect changes rebase it.

## Dropped (417 of 702 lines)

| Reason | Lines |
|---|---|
| Chaos run (CCP game mode) | 109 |
| Settings Classic lacks, or slider-value easter eggs with no per-value hook | 75 |
| Possession / The Dose | 40 |
| Foreground-app watching | 29 |
| Webcam tracking | 20 |
| Pages Classic lacks (quests, leaderboard, deeper, lab, discord, takeover...) | 18 |
| Progression systems Classic lacks (quests, mantras, roadmap, skills, quizzes) | 14 |
| UI actions Classic lacks (rerolls, deeper, reset memory, minimise/close) | 14 |
| AI chat, voice commands | 20 |
| Avatar popping effect bubbles (CCP only) | 9 |
| Online features (leaderboard, Discord, Patreon, updates, remote control) | 9 |
| Emergency Exit, Lockdown, Descent | 18 |
| Phone wording (mobile greetings, phone idle lines), webcam idle line | 8 |
| System idle detection, session phases, pauses, relaunch timing, 7-day history, setup stall timer | 16 |
| Pink Rush (CCP event) | 2 |
| Mods, personas, tray wake; scheduler/webcam/counting/visuals feature pages | 8 |
| Counting Practice (replaced by Target Practice), missed bubbles, XP ticks, mute egg | 5 |
| Panic line ("nothing barks during panic"), stop line ("a stop silences barks") | 2 |
| Longer than 160 characters (does not fit the tube bubble) | 1 |

Every kept line is checked by tests: its recording exists in the shipped folder and is not an
LFS pointer, the folder holds no unused clip, and no text names a gender, Bambi, a removed
feature or anything online (girl, boy, sissy, bimbo, bambi, princess, chaster, discord, webcam,
ai, mod, she, he, her, him, his, patreon, phone), and no line exceeds 160 characters.
