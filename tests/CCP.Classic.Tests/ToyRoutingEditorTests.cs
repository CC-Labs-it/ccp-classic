using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Classic.App.Controls;
using CCP.Classic.Core.LocalDevices;
using CCP.Classic.Core.Localization;
using Xunit;

namespace CCP.Classic.Tests;

[Collection("Avalonia UI")]
public sealed class ToyRoutingEditorTests
{
    [Fact]
    public Task PatternGraphRendersEveryActualEnvelopeWithoutADeviceConnection() => UiDispatcher.Run(() =>
    {
        foreach (var pattern in Enum.GetValues<LocalHapticPattern>())
        {
            var graph = new ToyPatternPreview(); graph.Configure(pattern, .7, 600, "balanced");
            graph.Measure(new(240, 64)); graph.Arrange(new Rect(0, 0, 240, 64));
            using var proof = new RenderTargetBitmap(new(240, 64), new(96, 96)); proof.Render(graph);
            using var stream = new MemoryStream(); proof.Save(stream, PngBitmapEncoderOptions.Default); Assert.True(stream.Length > 200);
        }
    });

    [Fact]
    public async Task PerDeviceTestTargetsOnlyItsCardAndStopDelegatesToTheSharedPolicy()
    {
        var wire = new Wire(); await using var controller = new LocalDeviceController(() => new(() => wire));
        Assert.True(await controller.ConnectAsync(LocalDeviceProvider.LovenseConnect, 20010, TestContext.Current.CancellationToken));
        ToyRoutingEditor editor = null!; var stops = 0;
        try
        {
            await UiDispatcher.Run(() =>
            {
                editor = new(new(), controller, new LocalizationService("en"), () => { }, () => { stops++; return false; });
                var controls = editor.GetLogicalDescendants().OfType<Control>().ToArray(); Assert.Equal(2, controls.OfType<ToyPatternPreview>().Count());
                controls.OfType<Button>().Single(button => button.Name == "ToyDeviceTest_LovenseConnect_one").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, controller.ActivePulseCount);
                controls.OfType<Button>().Single(button => button.Name == "ToyDeviceStop_LovenseConnect_one").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, stops); Assert.Equal(1, controller.ActivePulseCount);
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            while (wire.Levels.GetValueOrDefault("one") == 0 || !wire.Levels.ContainsKey("two")) await Task.Delay(10, timeout.Token);
            Assert.Equal(0, wire.Levels["two"]); await controller.StopAsync(); Assert.All(wire.Levels.Values, level => Assert.Equal(0, level));
        }
        finally { await UiDispatcher.Run(() => editor?.Dispose()); }
    }
    private sealed class Wire : ILocalDeviceTransport
    {
        public readonly ConcurrentDictionary<string, int> Levels = new();
        public Task OpenAsync(Uri endpoint, CancellationToken token) => Task.CompletedTask;
        public Task<string> ExchangeAsync(string request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(request); var body = json.RootElement;
            if (body.GetProperty("command").GetString() == "GetToys")
                return Task.FromResult("{\"code\":200,\"data\":{\"toys\":{\"one\":{\"name\":\"lush\",\"status\":1},\"two\":{\"name\":\"lush\",\"status\":1}}}}");
            var action = body.GetProperty("action").GetString()!;
            if (body.TryGetProperty("toy", out var target)) Levels[target.GetString()!] = action == "Stop" ? 0 : int.Parse(action.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
            else foreach (var key in Levels.Keys) Levels[key] = 0;
            return Task.FromResult("{\"code\":200}");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
