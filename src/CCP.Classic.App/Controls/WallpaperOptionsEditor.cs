using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class WallpaperOptionsEditor : OptionEditor
{
    public WallpaperOptionsEditor(WallpaperOptions options, LocalizationService text, Action changed, Action shuffle) : base(text, changed)
    {
        Heading("Wallpaper"); Spacing = 7;
        String("WallpaperSource", options.SourceFolder ?? "", value => options.SourceFolder = value);
        var chooseFolder = new Button { Content = Text["ChooseFolder"] };
        chooseFolder.Click += async (_, _) =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            try
            {
                var result = await provider.OpenFolderPickerAsync(new() { AllowMultiple = false });
                if (result.FirstOrDefault()?.TryGetLocalPath() is { } path)
                    this.GetVisualDescendants().OfType<TextBox>().First(input => input.Name == "WallpaperSource").Text = path;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
        }; Children.Add(OptionHelp.Wrap(chooseFolder, "ChooseFolder", text));
        String("WallpaperOwnImage", options.OwnImagePath ?? "", value => options.OwnImagePath = value);
        var own = new Button { Content = Text["ChooseImage"] };
        own.Click += async (_, _) =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            try
            {
                var result = await provider.OpenFilePickerAsync(new() { AllowMultiple = false, FileTypeFilter =
                    [new FilePickerFileType(Text["Images"]) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif"] }] });
                if (result.FirstOrDefault()?.TryGetLocalPath() is { } path)
                    this.GetVisualDescendants().OfType<TextBox>().First(input => input.Name == "WallpaperOwnImage").Text = path;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
        }; Children.Add(OptionHelp.Wrap(own, "ChooseImage", text));
        Toggle("WallpaperCollage", options.Collage, value => options.Collage = value);
        foreach (var (flag, key) in new[] { (1, "LayoutSideHero"), (2, "LayoutFillerOnly"), (4, "LayoutCenterHero") })
            Toggle(key, (options.LayoutMask & flag) != 0, value => options.LayoutMask = value ? options.LayoutMask | flag : options.LayoutMask & ~flag);
        Toggle("WallpaperRotate", options.Rotate, value => options.Rotate = value);
        Number("WallpaperInterval", options.IntervalSeconds, 10, 86400, value => options.IntervalSeconds = (int)value);
        Color("AccentColour", options.Accent, value => options.Accent = value);
        Color("BackgroundColour", options.Background, value => options.Background = value);
        var next = new Button { Content = Text["ShuffleWallpaper"], Name = "ShuffleWallpaper" }; next.Click += (_, _) => shuffle(); Children.Add(OptionHelp.Wrap(next, "ShuffleWallpaper", text));
    }
}
