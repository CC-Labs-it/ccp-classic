using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Media;

internal sealed class VideoSoundState
{
    public bool Muted { get; private set; }
    public event Action? Changed;
    public void Toggle() { Muted = !Muted; Changed?.Invoke(); }
}

internal sealed partial class VideoWindow
{
    private readonly VideoSoundState sound;
    private double lastGain;
    private Button muteButton = null!;
    private Control MakeControls(LocalizationService text, Func<bool> requestStop)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        muteButton = CornerButton("♪", "VideoMute", text["Volume"]);
        muteButton.Content = VolumeIcon(sound.Muted);
        muteButton.Click += (_, _) => sound.Toggle(); row.Children.Add(muteButton);
        if (!cue.Strict)
        {
            var close = CornerButton("×", "VideoClose", text["Close"]);
            close.Click += (_, _) => { if (!completed) requestStop(); }; row.Children.Add(close);
        }
        sound.Changed += SoundChanged; return row;
    }
    private void SoundChanged()
    {
        if (completed) return;
        muteButton.Content = VolumeIcon(sound.Muted);
        playback.SetGain(sound.Muted ? 0 : lastGain);
    }
    private static Control VolumeIcon(bool muted) => new Avalonia.Controls.Shapes.Path
    {
        Width = 14, Height = 14, Stretch = Stretch.Uniform, Stroke = Brushes.White, StrokeThickness = 1.8,
        Data = Geometry.Parse(muted ? "M2 9h4l5-5v16l-5-5H2z M16 8l6 8 M22 8l-6 8"
            : "M2 9h4l5-5v16l-5-5H2z M15 8q4 4 0 8 M18 5q7 7 0 14")
    };
    private static Button CornerButton(string icon, string name, string caption)
    {
        var button = new Button { Name = name, Content = icon, Width = 24, Height = 24, FontSize = 16,
            Padding = new Thickness(0), Opacity = .6, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
        button.Classes.Add("pill"); ToolTip.SetTip(button, caption);
        Avalonia.Automation.AutomationProperties.SetName(button, caption);
        button.PointerEntered += (_, _) => button.Opacity = 1;
        button.PointerExited += (_, _) => button.Opacity = .6;
        return button;
    }
}
