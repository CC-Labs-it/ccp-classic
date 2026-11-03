using System.Text.Json;
using CCP.Classic.App.Media;
using CCP.Classic.Core;

namespace CCP.Classic.App.Effects;

public sealed partial class DesktopEffectsService
{
    private string? libraryRoots, librarySelection;

    private void RefreshInteractiveLibrary(bool force = false)
    {
        if (!running || disposed) return;
        var options = settings.Library;
        var roots = JsonSerializer.Serialize(new[] { options.ImageFolders, options.AudioFolders });
        var selection = JsonSerializer.Serialize(new { options.ExcludedFiles, options.IncludedFiles, options.FolderSelections });
        var rescan = force || libraryRoots != roots;
        if (!rescan && selection == librarySelection) return;
        libraryRoots = roots; librarySelection = selection; imagesScanned = false;
        flashVoices.Clear();
        if (rescan)
        {
            interactiveScan?.Cancel(); interactiveScan?.Dispose(); interactiveScan = new();
            var token = interactiveScan.Token;
            var audioRoots = options.AudioFolders.ToArray(); var imageRoots = options.ImageFolders.ToArray();
            audioLibrary = Task.Run(() => MediaLibrary.Scan(imageDirectories: imageRoots, audioDirectories: audioRoots,
                cancellationToken: token), token);
        }
        ReplaceSelectedImages(audioLibrary?.IsCompletedSuccessfully == true ? audioLibrary.Result.Images : []);
    }

    private void ReplaceSelectedImages(IEnumerable<string> scanned)
    {
        var paths = starter.Images.Concat(scanned).Where(settings.Library.Includes)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (selectedImages is not null && selectedImages.SelectedPaths.SequenceEqual(paths,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) return;
        StopFlashVoice(); remix.Stop();
        selectedImages?.Dispose(); selectedImages = images.Count > 0 ? new(paths, images[0]) : null;
        timeline.ReplaceImages(selectedImages?.Count ?? 0);
        interactive.ReplaceImages(selectedImages?.Count ?? 0);
        if (running) remix.Start();
    }
}
