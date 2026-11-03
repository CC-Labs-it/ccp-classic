using Avalonia.Controls;
using CCP.Classic.Core;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class CompanionPlacementEditor : OptionEditor
{
    public CompanionPlacementEditor(CompanionPlacementOptions options, LocalizationService text, Action changed) : base(text, changed)
    {
        Heading("CompanionPlacement"); Spacing = 7;
        Toggle("Detached", options.Detached, value => options.Detached = value);
        Number("CompanionScale", options.Scale * 100, 50, 150, value => options.Scale = value / 100).Increment = 25;
        Toggle("AlwaysOnTop", options.AlwaysOnTop, value => options.AlwaysOnTop = value);
        var reset = new Button { Name = "ResetPlacement", Content = Text["ResetPlacement"] };
        reset.Click += (_, _) => { options.Left = options.Top = null; options.Scale = 1; Changed(); }; Children.Add(reset);
    }
}
