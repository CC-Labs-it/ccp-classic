// Adapted from public CCP HelpLoops at fe40ea7, MIT. See docs/POLISH-ASSETS-HELP.md.
using System;
using Avalonia;
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;
using DrawingContext = CCP.Classic.App.Controls.HelpLoops.LoopContext;
using StreamGeometry = CCP.Classic.App.Controls.HelpLoops.LoopGeometry;
using static CCP.Classic.App.Controls.HelpLoops.LoopMath;

namespace CCP.Classic.App.Controls.HelpLoops
{
    internal enum PhotoLook { Pink, Stripes, Sea }
    internal sealed partial class LoopFrame
    {
        private static readonly Brush InputFill = LoopPalette.Solid("#0f0b1c");
        private static readonly Brush EyeWhite = LoopPalette.Solid("#f3ecff");
        private static readonly Brush EyePupil = LoopPalette.Solid("#1a1030");
        private static readonly Brush EyeGlint = LoopPalette.Solid("#ffffffdd");
        private static readonly Brush GazeCore = LoopPalette.Solid("#ffffffee");
        private static readonly Brush RingRest = LoopPalette.Solid("#ffffff26");
        private static readonly Brush MeterTrack = LoopPalette.Solid("#0d0a18e6");
        public static string Typed(string text, double k) =>
            string.IsNullOrEmpty(text) ? "" : text[..(int)Math.Floor(Clamp(k) * text.Length + 1e-9)];
        public double TypeInto(Rect field, string text, double k, double t, bool caret = true,
            Brush? brush = null, Brush? border = null, double size = 13, bool box = true)
        {
            var dc = Front;
            if (box)
                dc.DrawRoundedRectangle(InputFill, new Pen(border ?? P.Border, 1), Inset(field, .5), 6.5, 6.5);
            var shown = Typed(text, k);
            double x = field.X + (box ? 10 : 0);
            double midY = field.Y + field.Height / 2;
            if (shown.Length > 0)
            {
                var ft = Format(shown, size, brush ?? P.Text, Mono, FontWeights.Medium);
                dc.DrawText(ft, new Point(x, midY - ft.Height / 2));
                x += ft.WidthIncludingTrailingWhitespace;
            }
            if (caret)
            {
                bool typing = k > 0 && k < 1;
                bool on = typing || (int)Math.Floor(t / 400) % 2 != 0;
                using (Fade(dc, on ? 1 : .2))
                    dc.DrawRectangle(P.Accent, null, new Rect(x + 1, midY - size * .58, 2, size * 1.15));
            }
            return x;
        }
        public void Eye(double x, double y, double openK, double lookDx = 0, double lookDy = 0, double scale = 1)
        {
            var dc = Front;
            double w = 22 * scale, h = 12 * scale * Clamp(openK);
            var lidPen = new Pen(P.Lilac, 2 * scale) { LineCap = PenLineCap.Round };
            if (h < .8)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - w, y), false, false);
                    c.QuadraticBezierTo(new Point(x, y + 5 * scale), new Point(x + w, y), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, lidPen, g);
                return;
            }
            var almond = new StreamGeometry();
            using (var c = almond.Open())
            {
                c.BeginFigure(new Point(x - w, y), true, true);
                c.QuadraticBezierTo(new Point(x, y - h * 2), new Point(x + w, y), true, false);
                c.QuadraticBezierTo(new Point(x, y + h * 2), new Point(x - w, y), true, false);
            }
            almond.Freeze();
            dc.DrawGeometry(EyeWhite, null, almond);
            using (ClipTo(dc, almond))
            {
                double ir = 8 * scale;
                var ic = new Point(x + Clamp(lookDx, -1, 1) * w * .45, y + Clamp(lookDy, -1, 1) * 5 * scale);
                dc.DrawEllipse(P.Accent, null, ic, ir, ir);
                dc.DrawEllipse(EyePupil, null, ic, ir * .48, ir * .48);
                dc.DrawEllipse(EyeGlint, null, new Point(ic.X + ir * .35, ic.Y - ir * .35), ir * .2, ir * .2);
            }
            dc.DrawGeometry(null, lidPen, almond);
        }
        public void GazeDot(double x, double y, double opacity = 1)
        {
            if (opacity <= 0) return;
            var dc = Front;
            using (Fade(dc, opacity))
            {
                for (int i = 3; i >= 1; i--)
                    using (Fade(dc, .14))
                        dc.DrawEllipse(P.Accent, null, new Point(x, y), 5 + i * 4, 5 + i * 4);
                dc.DrawEllipse(P.Accent, null, new Point(x, y), 5.5, 5.5);
                dc.DrawEllipse(GazeCore, null, new Point(x, y), 2.2, 2.2);
            }
        }
        public void DwellRing(double x, double y, double r, double k, Brush? brush = null, double thickness = 3)
        {
            var dc = Front;
            var c = new Point(x, y);
            dc.DrawEllipse(null, new Pen(RingRest, thickness), c, r, r);
            k = Clamp(k);
            if (k <= .002) return;
            var pen = new Pen(brush ?? P.Mint, thickness) { LineCap = PenLineCap.Round };
            if (k >= .998) { dc.DrawEllipse(null, pen, c, r, r); return; }
            var a = (k * 360 - 90) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
                ctx.ArcTo(new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r), new Size(r, r), 0, k > .5,
                    SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        public static void Sweep(DrawingContext dc, Point c, double r, double deg, Brush brush)
        {
            if (deg <= 0.5) return;
            if (deg >= 359.5) { dc.DrawEllipse(brush, null, c, r, r); return; }
            var a = (deg - 90) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(c, true, true);
                ctx.LineTo(new Point(c.X, c.Y - r), false, false);
                ctx.ArcTo(new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r), new Size(r, r), 0, deg > 180,
                    SweepDirection.Clockwise, false, false);
            }
            g.Freeze();
            dc.DrawGeometry(brush, null, g);
        }

        private static readonly Brush PhotoPinkFill = LoopPalette.Freeze(LoopPalette.Linear(
            LoopPalette.Css("#ff8fc8"), LoopPalette.Css("#9b5cff"), new Point(0, 0), new Point(1, 1)));
        private static readonly Brush PhotoPinkShine = LoopPalette.Freeze(new RadialGradientBrush
        {
            Center = new RelativePoint(.4, .35, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.4, .35, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.85, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#ffffff99"), 0),
                new GradientStop(LoopPalette.Css("#ffffff00"), .6),
            }
        });
        private static readonly Brush PhotoSeaFill = LoopPalette.Freeze(LoopPalette.Linear(
            LoopPalette.Css("#5fffd0"), LoopPalette.Css("#2b6bff"), new Point(.33, .03), new Point(.67, .97)));
        private static readonly Brush PhotoSeaShade = LoopPalette.Freeze(LoopPalette.Linear(
            LoopPalette.Css("#00000000"), LoopPalette.Css("#00000066"), new Point(0, 0), new Point(0, 1)));
        private static readonly Brush PhotoStripeA = LoopPalette.Solid("#ff6fb5");
        private static readonly Brush PhotoStripeB = LoopPalette.Solid("#2b124a");
        public void Photo(Rect r, PhotoLook look, bool shadow = true) => Photo(Front, r, look, shadow);

        public void Photo(DrawingContext dc, Rect r, PhotoLook look, bool shadow = true)
        {
            if (shadow) SoftShadow(dc, r, 4, 10, 22, 0xAA);
            dc.DrawRoundedRectangle(Brushes.White, null, r, 4, 4);
            var inner = Inset(r, 3);
            using (ClipTo(dc, new RectangleGeometry(inner, 1.5, 1.5)))
            {
                switch (look)
                {
                    case PhotoLook.Pink:
                        dc.DrawRectangle(PhotoPinkFill, null, inner);
                        dc.DrawEllipse(PhotoPinkShine, null,
                            new Point(inner.X + inner.Width * .5, inner.Y + inner.Height * .5),
                            inner.Width * .2, inner.Height * .22);
                        break;
                    case PhotoLook.Stripes:
                        dc.DrawRectangle(PhotoStripeB, null, inner);
                        PhotoWedges(dc, inner);
                        break;
                    default:
                        dc.DrawRectangle(PhotoSeaFill, null, inner);
                        dc.DrawRectangle(PhotoSeaShade, null, new Rect(inner.X, inner.Bottom - inner.Height * .4, inner.Width, inner.Height * .4));
                        break;
                }
            }
        }
        private static void PhotoWedges(DrawingContext dc, Rect r)
        {
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 18; i += 2)
                {
                    double a0 = (i * 20 - 90) * Math.PI / 180, a1 = ((i + 1) * 20 - 90) * Math.PI / 180;
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(a0) * rad, c.Y + Math.Sin(a0) * rad), false, false);
                    ctx.LineTo(new Point(c.X + Math.Cos(a1) * rad, c.Y + Math.Sin(a1) * rad), false, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(PhotoStripeA, null, g);
        }
        public void Meter(Rect r, double v, Brush? fill = null, string? label = null, double opacity = 1)
        {
            if (opacity <= 0) return;
            var dc = Front;
            using (Fade(dc, opacity))
            {
                double rad = r.Height / 2;
                dc.DrawRoundedRectangle(MeterTrack, new Pen(P.Border, 1), Inset(r, -.5), rad + .5, rad + .5);
                var w = r.Width * Clamp(v);
                if (w > .5) dc.DrawRoundedRectangle(fill ?? P.Accent, null, new Rect(r.X, r.Y, Math.Max(w, r.Height), r.Height), rad, rad);
                if (!string.IsNullOrEmpty(label))
                    DrawText(dc, label, r.X, r.Y - 14, 9, P.Dim, Mono, FontWeights.Medium, TextAlignment.Left);
            }
        }
    }
}
