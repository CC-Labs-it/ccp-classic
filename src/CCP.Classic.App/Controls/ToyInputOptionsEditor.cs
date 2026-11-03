using Avalonia.Controls;
using CCP.Classic.Core.LocalDevices;
using CCP.Classic.Core.Localization;

namespace CCP.Classic.App.Controls;

public sealed class ToyInputOptionsEditor : OptionEditor
{
    public ToyInputOptionsEditor(LocalDevicePreferences options, LocalDeviceController controller, LocalizationService text, Action changed) : base(text, changed)
    {
        Spacing = 7; Heading("ToyInputEnabled"); CheckBox? attention = null;
        Toggle("ToyInputEnabled", options.ToyInputEnabled, value =>
        { options.ToyInputEnabled = value; controller.InputEnabled = value; if (attention is not null) attention.IsEnabled = value; });
        Toggle("ToyAttentionButton", options.AttentionCheckToyButton, value => options.AttentionCheckToyButton = value);
        attention = (CheckBox)Children[^1]; attention.IsEnabled = options.ToyInputEnabled;
        Children.Add(new TextBlock { Text = text["ToyInputHelp"], TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Number("FunScriptIntensity", options.FunScriptIntensity * 100, 0, 100, value => options.FunScriptIntensity = value / 100);
    }
}
