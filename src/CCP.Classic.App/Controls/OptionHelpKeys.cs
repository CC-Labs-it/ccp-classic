using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

internal static class OptionHelpKeys
{
    private static readonly Dictionary<string, string> Keys = Build();
    private static Dictionary<string, string> Build()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string help, params string[] keys) { foreach (var key in keys) result[key] = help; }
        Add("HelpFrequency", "PerHour", "PerMinute", "Every", "st4_braindrain_audio_rate", "BarkInterval");
        Add("HelpSize", "Size", "VideoFloatingSize", "VideoTargetSize", "CompanionScale");
        Add("HelpOpacity", "Opacity");
        Add("HelpSpeed", "Speed", "FlashDriftSpeed", "VideoFloatingSpeed", "LoomLayerSpeed");
        Add("HelpVolume", "Volume", "Master", "Video", "WhisperVolume", "LayeredAudioMaster");
        Add("HelpColor", "TextColour", "FilterColour", "setting_text_color", "LockBackground", "LockInputBackground", "LockInputText", "LockAccent", "VideoTargetColor1", "VideoTargetColor2", "VideoTargetText", "VideoTargetBorderColor", "AccentColour", "BackgroundColour", "LoomColour", "LoomBackground", "LoomOuterColour");
        Add("HelpFont", "Font", "label_font");
        Add("HelpDisplay", "TargetMonitor", "Monitor", "VideoMonitor", "FillAllMonitors", "VideoAllMonitors");
        Add("HelpEnabled", "Enabled", "DeviceEnabled", "LayeredTrackEnabled", "LayeredAudioEnabled", "EnableEffect");
        Add("HelpAudioPick", "SelectAudio", "MindWipeAudio", "LayeredAudioAdd");
        Add("HelpAudioClear", "ClearAudio");
        Add("HelpPhrase", "PhrasePool", "LockPhrases", "LockNewPhrase", "BounceNewPhrase", "VideoTargetPhrases", "VideoNewPhrase", "LoomWord");
        Add("HelpAdd", "Add", "KeywordNew", "KeywordAddAction");
        Add("HelpRemove", "Remove", "Delete");
        Add("HelpCapacity", "MaxOnScreen", "MostAtOnce", "MaximumConcurrentPulses");
        Add("HelpClickDismiss", "ClickToDismiss");
        Add("HelpStay", "FlashStay"); Add("HelpLinkAudio", "FlashAudio");
        Add("HelpRemix", "FlashRemix"); Add("HelpHydra", "Hydra"); Add("HelpHydraTime", "FlashHydraTiming");
        Add("HelpFlashCount", "FlashCount", "VideoTargetCount");
        Add("HelpRandomCount", "FlashRandomCount", "VideoRandomTargets", "LockRandomRepeats");
        Add("HelpMinimumCount", "FlashMinimumCount", "LockMinimumRepeats");
        Add("HelpHold", "FlashHold"); Add("HelpFlashMotion", "FlashMotion"); Add("HelpGifSpeed", "FlashGifSpeed");
        Add("HelpDrag", "FlashDraggable"); Add("HelpShatter", "FlashShatter"); Add("HelpGlow", "FlashGlow", "LoomGlow");
        Add("HelpRounded", "FlashRounded"); Add("HelpAvoidCenter", "FlashAvoidCenter"); Add("HelpCenterSize", "FlashCenterExclusion");
        Add("HelpFade", "Fade"); Add("HelpExit", "ImageExit"); Add("HelpStrict", "Strict", "StrictLock");
        Add("HelpFloating", "FloatingWindows"); Add("HelpAttention", "AttentionCheck"); Add("HelpTargetLife", "VideoTargetLife");
        Add("HelpReplay", "VideoReplay"); Add("HelpMercy", "VideoMercy", "VideoMercyFails");
        Add("HelpClipMin", "VideoMinimum"); Add("HelpClipMax", "VideoMaximum"); Add("HelpRandomStart", "VideoRandomStart");
        Add("HelpTogether", "VideoTogether"); Add("HelpKeepSound", "VideoKeepSound"); Add("HelpBackground", "VideoBlurredBackground");
        Add("HelpTargetFloating", "VideoTargetFloating"); Add("HelpBorder", "VideoTargetBorder");
        Add("HelpDuck", "Duck", "AudioDucking"); Add("HelpOutput", "AudioOutput", "OutputDevice");
        Add("HelpTest", "Test", "TestVideo", "TestAudio", "ToyPatternPreview"); Add("HelpLoop", "Loop");
        Add("LayeredAudioHelp", "AudioLayers", "LayeredAudio"); Add("LayeredAudioOnlyHelp", "LayeredAudioOnly");
        Add("ToyInputHelp", "ToyInputEnabled", "ToyAttentionButton"); Add("FunScriptHelp", "FunScriptEnabled");
        Add("GentleVisualsHelp", "GentleVisuals"); Add("StartHiddenHelp", "StartHidden");
        Add("HelpLanguage", "Language"); Add("HelpStartup", "StartWithSystem"); Add("HelpAutoRun", "AutoRun", "StartOnLaunch");
        Add("HelpPanic", "PanicKey"); Add("HelpNoPanic", "NoPanic"); Add("HelpReducedMotion", "ReducedMotion"); Add("HelpPerformance", "Performance", "HighRefresh");
        Add("HelpName", "DeviceNickname", "PresetName", "LibrarySelectionName", "LoomName");
        Add("HelpIntensity", "Intensity", "GlobalIntensity", "MaximumPower", "MinimumIntensity", "MaximumIntensity", "DeviceTrim", "FunScriptIntensity", "LuminanceIntensity", "AudioLiveIntensity");
        Add("HelpAudioSync", "AudioSync"); Add("HelpSensitivity", "Sensitivity"); Add("HelpSmoothing", "Smoothing"); Add("HelpBass", "BassWeight"); Add("HelpRms", "RmsWeight"); Add("HelpOnset", "OnsetWeight"); Add("HelpOffset", "AudioSyncDelay"); Add("HelpBandSplit", "AudioBandSplit");
        Add("HelpRole", "ToyRole"); Add("HelpPattern", "HapticPattern"); Add("HelpToyTime", "ToyDuration"); Add("HelpSoftRamp", "ToySoftRamp"); Add("HelpTemperament", "ToyTemperament"); Add("HelpLuminance", "LuminanceSync"); Add("HelpScriptVibration", "FunScriptToVibration");
        Add("HelpCooldown", "KeywordCooldown", "KeywordGlobalCooldown", "KeywordSameCooldown"); Add("HelpTypingBuffer", "KeywordBuffer"); Add("HelpKeyword", "KeywordText"); Add("HelpMatch", "KeywordMatch"); Add("HelpApps", "KeywordAppScope", "KeywordApps"); Add("HelpIgnoreOwn", "KeywordIgnoreOwn"); Add("HelpLoopGuard", "KeywordLoopProtection", "KeywordLoopTime"); Add("HelpAction", "KeywordActions", "KeywordVisual", "TimedEffect");
        Add("HelpRepeat", "Repeats"); Add("HelpTargetLength", "LockTargetEnabled", "LockTargetLength"); Add("HelpVariance", "LockTargetVariance"); Add("HelpTypo", "LockResetTypo"); Add("HelpPaste", "LockStrictCopy");
        Add("HelpBehind", "UnderEffects"); Add("HelpOverVideo", "setting_show_over_videos"); Add("HelpSecondText", "setting_second_text"); Add("HelpBreathing", "setting_fx_breathing"); Add("HelpWobble", "setting_fx_wobble"); Add("HelpSpin", "setting_fx_spin"); Add("HelpLean", "setting_fx_velocity_tilt"); Add("HelpSquash", "setting_fx_squash_stretch"); Add("HelpCornerBurst", "setting_fx_corner_burst"); Add("HelpOutline", "setting_fx_outline"); Add("HelpColorMode", "setting_color_mode");
        Add("HelpBlur", "st4_braindrain_blur_strength"); Add("HelpMelt", "st4_braindrain_melt", "BrainDrainMelt"); Add("HelpCapture", "st4_braindrain_allow_capture", "BrainDrainCapture");
        Add("HelpFolder", "Folders", "ChooseFolder", "WallpaperSource", "st4_braindrain_clip_library"); Add("HelpRefresh", "Refresh", "RefreshOutputs"); Add("HelpSearch", "LibrarySearch"); Add("HelpInclude", "LibraryInclude", "LibraryFolderInclude"); Add("HelpFilter", "LibraryFilter"); Add("HelpSelection", "LibrarySelection");
        Add("HelpRunFor", "RunFor"); Add("HelpAtMinute", "AtMinute"); Add("HelpWallpaperRotate", "WallpaperRotate", "WallpaperInterval", "ShuffleWallpaper"); Add("HelpCollage", "WallpaperCollage", "LayoutSideHero", "LayoutFillerOnly", "LayoutCenterHero");
        Add("HelpPosition", "Position"); Add("HelpTopmost", "AlwaysOnTop"); Add("HelpDetached", "Detached"); Add("HelpBarks", "Barks"); Add("HelpTriggerPhrases", "TriggerPhrases"); Add("HelpRandomSpiral", "setting_spiral_randomize");
        Add("HelpVolume", "MasterVolume"); Add("HelpPhrase", "btn_edit_phrases", "KeywordImportPhrases"); Add("HelpOutput", "SystemDefault");
        Add("HelpFlashBody", "FlashImages"); Add("HelpVideoBody", "MandatoryVideos"); Add("HelpSubBody", "Subliminals", "Whispers", "Frames"); Add("HelpSpiralBody", "Spiral", "SpiralLibrary", "SelectGif", "CornerGifOne", "CornerGifTwo", "MakeYourOwn");
        Add("HelpPinkBody", "PinkFilter"); Add("HelpBubbleBody", "BubblePop", "ClickToPop", "Sound", "EffectBubbles", "EffectBubbleChance", "EffectBubbleType", "Glitch", "GifRain", "ImageWash", "Mode"); Add("HelpLockBody", "LockCard"); Add("HelpBounceBody", "BouncingText"); Add("HelpWipeBody", "MindWipe"); Add("HelpDrainBody", "BrainDrain"); Add("HelpAudioBody", "Audio"); Add("HelpWallBody", "Wallpaper", "WallpaperOwnImage", "ChooseImage"); Add("HelpCompanionBody", "Avatar", "Companion", "KeywordTriggers"); Add("HelpToysBody", "Intiface", "LovenseConnect"); Add("HelpProgressBody", "Achievements", "Level");
        Add("HelpReset", "Reset"); Add("HelpResetPlacement", "ResetPlacement"); Add("HelpExport", "Export"); Add("HelpStopTest", "Stop", "StopAudioTest");
        Add("HelpXpMultiplier", "KeywordSessionXp"); Add("HelpDurationCap", "DurationCap"); Add("HelpTargetCount", "VideoTargetCount"); Add("HelpPulseCapacity", "MaximumConcurrentPulses");
        Add("HelpHighRefresh", "HighRefresh"); Add("HelpBarkInterval", "BarkInterval"); Add("HelpAllDisplays", "FillAllMonitors", "VideoAllMonitors"); Add("HelpLaunchEffect", "StartOnLaunch");
        Add("HelpPop", "ClickToPop"); Add("HelpPopSound", "Sound"); Add("HelpEffectBubbles", "EffectBubbles"); Add("HelpEffectChance", "EffectBubbleChance"); Add("HelpEffectType", "EffectBubbleType");
        Add("HelpBubbleMotion", "BubbleMotion"); Add("HelpWallpaperMode", "WallpaperMode"); Add("HelpWhispers", "Whispers"); Add("HelpFrames", "Frames");
        Add("HelpChooseImage", "WallpaperOwnImage", "ChooseImage"); Add("HelpDefaultOutput", "SystemDefault"); Add("HelpLayoutSide", "LayoutSideHero"); Add("HelpLayoutTiles", "LayoutFillerOnly"); Add("HelpLayoutCenter", "LayoutCenterHero");
        Add("HelpBubbleVariant", "BubbleVariant"); Add("HelpEventRouting", "EventRouting");
        return result;
    }
    public static string Resolve(string key) => Keys.TryGetValue(key, out var value) ? value : "OptionHelp_" + key;
}
