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
    // AvatarCentre is the mirror axis (CCP's), never moved. The avatar box itself is CCP's 198x306
    // at 88% (owner 2026-11-03: 80%, then 10% bigger), its figure centred on the glass interior (x 262..406,
    // y about 560..868): the clips' figure sits about 17 units right of the box centre at full size.
    public const double AvatarCentre = 329.5;
    public const double AvatarFit = .88, AvatarWidth = 198 * AvatarFit, AvatarHeight = 306 * AvatarFit;
    public const double GlassCentre = 334, AvatarFigureOffset = 17 * AvatarFit;
    // Owner nudge 2026-11-03: "20 px to the right", then 7 px back left, at about .72 px per design unit on their panel.
    public const double AvatarNudge = 18;
    public const double AvatarBoxCentre = GlassCentre - AvatarFigureOffset + AvatarNudge, AvatarFeet = 838;
    // Visible band, symmetric about the avatar's centre so a mirrored tube still fits.
    public const double BandLeft = 229, BandRight = 430;
    public const double BandWidth = BandRight - BandLeft;
    // Painted art edges. ArtRight is where the pipes end (the band's right edge, opaque), so the
    // pipes meet the panel; ArtLeft is the conservative on-screen check edge.
    public const double ArtLeft = 239, ArtRight = 430;
    public const double ArtTop = 380, ArtBottom = 923, ArtHeight = ArtBottom - ArtTop;
    // The painted art's height as a share of the panel's visible height. One-line tweak.
    // Owner found 0.788 (monitor-based) too big on 2048x1104; .38 = about 70% of that at an 800 DIP panel.
    public const double ArtToPanelHeight = .38;
    public const double MinScale = .35, MaxScale = 1;
    // The art overlaps the panel's visible edge by this many DIP, so no daylight shows after
    // rounding; the tube is an owned window above its owner.
    public const double DockOverlap = 1, VerticalNudge = 20;

    public static double MirroredX(double canvasX) => 2 * AvatarCentre - canvasX;

    /// <summary>
    /// Design scale in DIP terms. The art is <see cref="ArtToPanelHeight"/> of the panel's visible
    /// height, clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>], then multiplied by the
    /// player's Companion size; the whole window never grows taller than the work area.
    /// </summary>
    public static double Scale(double panelHeight, double workHeight, double userScale = 1)
    {
        var scale = panelHeight > 0 && double.IsFinite(panelHeight)
            ? Math.Clamp(panelHeight * ArtToPanelHeight / ArtHeight, MinScale, MaxScale) : MaxScale;
        scale *= double.IsFinite(userScale) ? Math.Clamp(userScale, .5, 1.5) : 1;
        return workHeight > 0 && double.IsFinite(workHeight) ? Math.Min(scale, workHeight / CanvasHeight) : scale;
    }

    /// <summary>Window size in DIP for a design scale.</summary>
    public static Size WindowSize(double scale) => new(BandWidth * scale, CanvasHeight * scale);

    /// <summary>
    /// Attached placement against the panel's VISIBLE frame (physical px, no invisible resize
    /// borders). The art's right edge meets main's visible left edge; with no room on the left the
    /// mirrored art docks on main's right; with room on neither side the art stays fully on the work
    /// area on the roomier side. Main is never moved.
    /// </summary>
    public static TubeDock Dock(PixelRect panel, PixelRect workArea, double scaling, double scale)
    {
        if (!(scaling > 0)) scaling = 1;
        var unit = scale * scaling; var overlap = DockOverlap * scaling;
        var height = CanvasHeight * unit;
        // Insets of the painted art from the window's left edge, plain and mirrored.
        double plainLeft = (ArtLeft - BandLeft) * unit, plainRight = (ArtRight - BandLeft) * unit;
        double mirrorLeft = (MirroredX(ArtRight) - BandLeft) * unit, mirrorRight = (MirroredX(ArtLeft) - BandLeft) * unit;

        var leftDock = panel.X + overlap - plainRight;
        var rightDock = panel.Right - overlap - mirrorLeft;
        double left; TubeDockSide side; bool mirrored;
        if (leftDock + plainLeft >= workArea.X) { left = leftDock; side = TubeDockSide.Left; mirrored = false; }
        else if (rightDock + mirrorRight <= workArea.Right) { left = rightDock; side = TubeDockSide.Right; mirrored = true; }
        else
        {
            mirrored = workArea.Right - panel.Right > panel.X - workArea.X;
            double artLeft = mirrored ? mirrorLeft : plainLeft, artRight = mirrored ? mirrorRight : plainRight;
            var minLeft = workArea.X - artLeft; var maxLeft = workArea.Right - artRight;
            left = maxLeft < minLeft ? minLeft : Math.Clamp(mirrored ? rightDock : leftDock, minLeft, maxLeft);
            side = TubeDockSide.Float;
        }

        var top = panel.Y + (panel.Height - height) / 2 + VerticalNudge * unit;
        top = height <= workArea.Height ? Math.Clamp(top, workArea.Y, workArea.Bottom - height)
            : workArea.Y - (height - workArea.Height) / 2;
        return new(new((int)Math.Round(left), (int)Math.Round(top)), side, mirrored);
    }

    /// <summary>
    /// The visible frame: DWM's extended frame bounds when they sit inside the outer frame (Windows
    /// 10/11 outer frames carry invisible resize borders), else the outer frame itself.
    /// </summary>
    public static PixelRect VisibleFrame(PixelRect outer, PixelRect? extended)
    {
        if (extended is not { Width: > 0, Height: > 0 } inner) return outer;
        const int slack = 2;
        return inner.X >= outer.X - slack && inner.Y >= outer.Y - slack
            && inner.Right <= outer.Right + slack && inner.Bottom <= outer.Bottom + slack ? inner : outer;
    }
}
