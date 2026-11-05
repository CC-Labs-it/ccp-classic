// Adapted from public CCP HelpLoops at fe40ea7, MIT. See docs/POLISH-ASSETS-HELP.md.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;
using DrawingContext = CCP.Classic.App.Controls.HelpLoops.LoopContext;
using StreamGeometry = CCP.Classic.App.Controls.HelpLoops.LoopGeometry;
using static CCP.Classic.App.Controls.HelpLoops.LoopMath;

namespace CCP.Classic.App.Controls.HelpLoops
{
    internal sealed class SubliminalsLoop : HelpLoopScene
    {
        private const double FlashMs = 48;
        private static readonly double[] Flashes = { 1200, 2900, 4600 };

        private static readonly Brush Ghost = LoopPalette.Solid("#ffb3dc");
        private static readonly Brush CellOff = LoopPalette.Solid("#3b3060");

        public override string Id => "Subliminals";
        public override double DurationMs => 6300;
        public override double StillMs => 1215;

        public override IReadOnlyList<HelpLoopStep> Steps { get; } = new[]
        {
            new HelpLoopStep("help_loop_subliminals_1", 800, 2600),
            new HelpLoopStep("help_loop_subliminals_2", 2600, 4200),
            new HelpLoopStep("help_loop_subliminals_3", 4200, 6000),
        };

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop(new Rect(40, 30, 400, 190));
            var dc = f.Front;

            bool on = false;
            double last = -1e9;
            foreach (var fl in Flashes)
            {
                if (t >= fl && t < fl + FlashMs) on = true;
                if (t >= fl) last = fl;
            }
            var ghost = t >= last + FlashMs ? .3 * (1 - Seg(t, last + FlashMs, last + 900)) : 0;
            if (ghost > 0)
            {
                using (f.Fade(dc, ghost))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var a = i * Math.PI / 4;
                        using (f.Fade(dc, .35))
                            DrawWord(f, dc, Ghost, Math.Cos(a) * 3, Math.Sin(a) * 3);
                    }
                    DrawWord(f, dc, Ghost, 0, 0);
                }
            }

            if (on)
            {
                var glow = LoopPalette.Solid(f.P.AccentColor, .22);
                for (int i = 0; i < 12; i++)
                {
                    var a = i * Math.PI / 6;
                    DrawWord(f, dc, glow, Math.Cos(a) * 6, Math.Sin(a) * 6);
                }
                DrawWord(f, dc, f.P.White, 0, 0);
            }

            DrawFrames(f, dc, t, on);

            var wk = Seg(t, last, last + 700);
            f.Speaker(418, 196, t >= 4200 && last >= 4600 ? wk : 0);
        }
        private static void DrawWord(LoopFrame f, DrawingContext dc, Brush brush, double dx, double dy)
        {
            var word = f.Label("Relax");
            const double size = 40, spacing = 4;
            var widths = new double[word.Length];
            double total = 0;
            for (int i = 0; i < word.Length; i++)
            {
                widths[i] = f.Measure(word[i].ToString(), size, LoopFrame.Display, bold: true).Width;
                total += widths[i] + spacing;
            }
            double x = LoopFrame.StageWidth / 2 - total / 2 + spacing / 2 + dx;
            double y = 108 + 5 + dy;
            for (int i = 0; i < word.Length; i++)
            {
                f.DrawText(dc, word[i].ToString(), x, y, size, brush, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Left);
                x += widths[i] + spacing;
            }
        }
        private static void DrawFrames(LoopFrame f, DrawingContext dc, double t, bool on)
        {
            var label = f.Format("16 ms", 9, f.P.Dim, LoopFrame.Mono, FontWeights.Medium);
            double inner = 10 * 9 + 9 * 3 + 3 + 6 + label.Width;
            double w = inner + 16 + 2, h = 28;
            var box = new Rect(LoopFrame.StageWidth - 14 - w, 14, w, h);
            bool hot = t > 2600 && t < 4200;
            dc.DrawRoundedRectangle(f.P.Glass, new Pen(hot ? f.P.Accent : f.P.Border, 1), LoopFrame.Inset(box, .5), 7.5, 7.5);

            int idx = (int)Math.Floor(t / 60 % 10);
            double x = box.X + 9;
            for (int i = 0; i < 10; i++)
            {
                var cell = new Rect(x, box.Y + 7, 9, 14);
                bool lit = on && i == idx;
                if (lit) dc.DrawRoundedRectangle(LoopPalette.Solid(f.P.AccentColor, .4), null, LoopFrame.Inset(cell, -3), 5, 5);
                dc.DrawRoundedRectangle(lit ? f.P.Accent : CellOff, null, cell, 2, 2);
                x += 12;
            }
            dc.DrawText(label, new Point(x + 6, box.Y + (h - label.Height) / 2));
        }
    }
}
