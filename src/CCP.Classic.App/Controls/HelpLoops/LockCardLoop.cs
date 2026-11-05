// Adapted from public CCP HelpLoops at fe40ea7, MIT. See docs/POLISH-ASSETS-HELP.md.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;
using DrawingContext = CCP.Classic.App.Controls.HelpLoops.LoopContext;
using StreamGeometry = CCP.Classic.App.Controls.HelpLoops.LoopGeometry;

namespace CCP.Classic.App.Controls.HelpLoops;
internal sealed class LockCardLoop : HelpLoopScene
{
        private const double CharMs = 52;
    private static readonly double[] RoundStarts = { 1400, 2700, 4200 };

    private static readonly Brush DimBrush = LoopPalette.Solid("#07040fcc");
    private static readonly Brush InputFill = LoopPalette.Solid("#0f0b1c");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_lockcard_1", 400, 1300),
        new HelpLoopStep("help_loop_lockcard_2", 1300, 4300),
        new HelpLoopStep("help_loop_lockcard_3", 4300, 6600),
    };

    public override string Id => "LockCard";
    public override double DurationMs => 7600;
    public override double StillMs => 2300;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var phrase = f.Label("Relax");
        var charMs = CharMs * 18 / Math.Max(1, phrase.Length);
        var typoAt = Math.Clamp((int)Math.Round(phrase.Length * 7d / 18), 0, Math.Max(0, phrase.Length - 1));
        f.Desktop();
        var dc = f.Front;
        var p = f.P;

        double inK = LoopMath.EaseOut(LoopMath.Seg(t, 500, 950));
        double outK = LoopMath.EaseInOut(LoopMath.Seg(t, 5900, 6400));
        double dimO = inK * (1 - outK);
        if (dimO > 0)
            using (f.Fade(dc, dimO))
                dc.DrawRectangle(DimBrush, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
        string text = "", wrong = "";
        int done = 0;
        bool flash = false;
        for (int r = 0; r < RoundStarts.Length; r++)
        {
            bool typo = r == 1;
            int len = phrase.Length;
            double tt = t - RoundStarts[r];
            if (tt < 0) continue;
            int n = (int)Math.Floor(tt / charMs);
            if (typo)
            {
                if (n >= typoAt && tt < typoAt * charMs + 320) { n = typoAt; wrong = "x"; }
                else if (tt >= typoAt * charMs + 320) n = typoAt + (int)Math.Floor((tt - typoAt * charMs - 320) / charMs);
            }
            double end = len * charMs + (typo ? 320 : 0);
            if (tt < end) { text = phrase[..Math.Min(n, len)]; if (!(typo && wrong.Length > 0)) wrong = ""; }
            else if (tt < end + 260) { text = phrase; flash = true; done = r + 1; wrong = ""; }
            else { done = r + 1; text = ""; wrong = ""; }
        }
        if (t >= RoundStarts[2] + phrase.Length * charMs + 260) { text = phrase; done = 3; }
        double cardO = 1 - outK;
        double top = LoopMath.Lerp(-170, 58, inK) - LoopMath.Lerp(0, 40, outK);
        if (cardO > 0 && top > -160)
        {
            using (f.Fade(dc, cardO))
            {
                f.Card(new Rect(90, top, 300, 150), p.Accent);
                f.Text(f.Label("Repeats") + " 3", 106, top + 13, 13, p.Dim);
                f.Text(phrase, 106, top + 34, 17, p.Text, bold: true);

                double shake = wrong.Length > 0 ? Math.Sin(t / 18) * 3 : 0;
                var input = new Rect(106 + shake, top + 72, 268, 30);
                Brush border = wrong.Length > 0 ? p.Red : (flash || done == 3 ? p.Mint : p.Border);
                dc.DrawRoundedRectangle(InputFill, new Pen(border, 1), LoopFrame.Inset(input, .5), 6.5, 6.5);

                double x = input.X + 10;
                double midY = input.Y + input.Height / 2;
                x += MonoText(f, text, x, midY, p.Text);
                x += MonoText(f, wrong, x, midY, p.Red);
                bool caretOn = ((int)Math.Floor(t / 400) % 2 != 0) || text.Length > 0;
                using (f.Fade(dc, caretOn ? 1 : .2))
                    dc.DrawRectangle(p.Accent, null, new Rect(x + 1, midY - 7.5, 2, 15));

                var lilacPen = new Pen(p.Lilac, 2);
                var mintPen = new Pen(p.Mint, 2);
                for (int i = 0; i < 3; i++)
                {
                    bool on = i < done;
                    dc.DrawEllipse(on ? p.Mint : null, on ? mintPen : lilacPen, new Point(106 + 5 + i * 16, top + 121), 4, 4);
                }
            }
        }

        f.Chip(390, 232, "Strict", false, inK * (1 - outK));
    }
    private static double MonoText(LoopFrame f, string s, double x, double midY, Brush brush)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var ft = f.Format(s, 13, brush, LoopFrame.Mono, FontWeights.Medium);
        f.Front.DrawText(ft, new Point(x, midY - ft.Height / 2));
        return ft.WidthIncludingTrailingWhitespace;
    }
}
