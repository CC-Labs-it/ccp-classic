using Avalonia;

namespace CCP.Classic.App.Companion;

internal enum TubeDockSide { Left, Right, Float }

internal readonly record struct TubeDock(PixelPoint Position, TubeDockSide Side, bool Mirrored);

// CCP's tube design canvas and TubeDockPlacement rules. Pure maths in physical pixels.
// The canvas is 780 x 1020 design units; the window shows only the horizontal band that holds
// the art, the avatar and her bubble, so its transparent sides never sit over the main window.
internal static class TubeLayout
{
    public const double CanvasWidth = 780, CanvasHeight = 1020;
    public const double TubeTop = 150, TubeSize = 780;
    public const double AvatarCentre = 329.5, AvatarFeet = 870, AvatarWidth = 198, AvatarHeight = 306;
    // Visible band, symmetric about the avatar's centre so a mirrored tube still fits.
    public const double BandLeft = 229, BandRight = 430;
    public const double BandWidth = BandRight - BandLeft;
    // Painted art edges: the dock edge and the conservative on-screen check edge.
    public const double ArtLeft = 239, ArtRight = 427;
    public const double DockGap = 3, VerticalNudge = 20;

    public static double MirroredX(double canvasX) => 2 * AvatarCentre - canvasX;

    /// <summary>Design scale from the monitor work area (physical px) and its scaling.</summary>
    public static double Scale(PixelRect workArea, double scaling)
    {
        if (!(scaling > 0) || workArea.Width <= 0 || workArea.Height <= 0) return 1;
        var height = workArea.Height / scaling; var width = workArea.Width / scaling;
        return Math.Clamp(Math.Min(height * .85 / CanvasHeight, width * .30 / CanvasWidth), .4, 1);
    }

    /// <summary>Window size in DIP for a design scale.</summary>
    public static Size WindowSize(double scale) => new(BandWidth * scale, CanvasHeight * scale);

    /// <summary>
    /// Attached placement. The art's right edge sits <see cref="DockGap"/> DIP left of main; with no
    /// room on the left the mirrored art docks on main's right; with room on neither side the art
    /// stays fully on the work area on the roomier side. Main is never moved.
    /// </summary>
    public static TubeDock Dock(PixelRect parent, PixelRect workArea, double scaling, double scale)
    {
        if (!(scaling > 0)) scaling = 1;
        var unit = scale * scaling; var gap = DockGap * scaling;
        var height = CanvasHeight * unit;
        // Insets of the painted art from the window's left edge, plain and mirrored.
        double plainLeft = (ArtLeft - BandLeft) * unit, plainRight = (ArtRight - BandLeft) * unit;
        double mirrorLeft = (MirroredX(ArtRight) - BandLeft) * unit, mirrorRight = (MirroredX(ArtLeft) - BandLeft) * unit;

        var leftDock = parent.X - gap - plainRight;
        var rightDock = parent.Right + gap - mirrorLeft;
        double left; TubeDockSide side; bool mirrored;
        if (leftDock + plainLeft >= workArea.X) { left = leftDock; side = TubeDockSide.Left; mirrored = false; }
        else if (rightDock + mirrorRight <= workArea.Right) { left = rightDock; side = TubeDockSide.Right; mirrored = true; }
        else
        {
            mirrored = workArea.Right - parent.Right > parent.X - workArea.X;
            double artLeft = mirrored ? mirrorLeft : plainLeft, artRight = mirrored ? mirrorRight : plainRight;
            var minLeft = workArea.X - artLeft; var maxLeft = workArea.Right - artRight;
            left = maxLeft < minLeft ? minLeft : Math.Clamp(mirrored ? rightDock : leftDock, minLeft, maxLeft);
            side = TubeDockSide.Float;
        }

        var top = parent.Y + (parent.Height - height) / 2 + VerticalNudge * unit;
        top = height <= workArea.Height ? Math.Clamp(top, workArea.Y, workArea.Bottom - height)
            : workArea.Y - (height - workArea.Height) / 2;
        return new(new((int)Math.Round(left), (int)Math.Round(top)), side, mirrored);
    }
}
