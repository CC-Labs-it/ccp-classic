// Adapted from public CCP HelpLoops at fe40ea7, MIT. See docs/POLISH-ASSETS-HELP.md.
using System;
using System.Globalization;
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;
using DrawingContext = CCP.Classic.App.Controls.HelpLoops.LoopContext;
using StreamGeometry = CCP.Classic.App.Controls.HelpLoops.LoopGeometry;

namespace CCP.Classic.App.Controls.HelpLoops
{
    internal sealed partial class LoopPalette
    {
        public static readonly Color DefaultAccent = Css("#ff6fb5");

        public LoopPalette(Brush? accent = null)
        {
            var a = accent is SolidColorBrush s ? s.Color : DefaultAccent;
            AccentColor = a;
            Accent = Freeze(new SolidColorBrush(a));
        }

        public Color AccentColor { get; }

        public Brush Ground { get; } = Solid("#110d1c");
        public Brush Panel { get; } = Solid("#1b1530");
        public Brush Panel2 { get; } = Solid("#241c3d");
        public Brush Border { get; } = Solid("#342a55");
        public Brush Window { get; } = Solid("#221a38");
        public Brush WindowBorder { get; } = Solid("#3b3060");
        public Brush WindowBar { get; } = Solid("#2d2349");
        public Brush LineA { get; } = Solid("#4a3d73");
        public Brush LineB { get; } = Solid("#5b4b8c");
        public Brush Track { get; } = Solid("#3b3060");
        public Brush Glass { get; } = Solid("#0d0a18d9");
        public Brush Text { get; } = Solid("#f3ecff");
        public Brush Dim { get; } = Solid("#a497c4");
        public Brush Ink { get; } = Solid("#1a1030");
        public Brush Accent { get; }
        public Brush Lilac { get; } = Solid("#b99cff");
        public Brush Mint { get; } = Solid("#5fffd0");
        public Brush Gold { get; } = Solid("#ffcf6b");
        public Brush Red { get; } = Solid("#ff5a6e");
        public Brush White { get; } = Solid("#ffffff");
        public static Brush Solid(string css) => Freeze(new SolidColorBrush(Css(css)));
        public static Brush Solid(Color c, double alpha) =>
            Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * LoopMath.Clamp(alpha)), c.R, c.G, c.B)));
        public static Color Css(string css)
        {
            var h = css.TrimStart('#');
            if (h.Length == 3 || h.Length == 4)
            {
                var e = "";
                foreach (var ch in h) e += new string(ch, 2);
                h = e;
            }
            byte P(int i) => byte.Parse(h.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return h.Length == 8
                ? Color.FromArgb(P(6), P(0), P(2), P(4))
                : Color.FromRgb(P(0), P(2), P(4));
        }

        public static T Freeze<T>(T f) 
        {
            
            return f;
        }
    }
}
