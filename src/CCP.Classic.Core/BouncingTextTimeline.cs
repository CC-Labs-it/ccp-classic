namespace CCP.Classic.Core;

// Public CCP BouncingTextService, fe40ea7. Positions are fractions of the measured travel box.
public sealed class BouncingTextTimeline(int seed = 0, BounceMotion? initial = null)
{
    private static readonly string[] Palette = ["#FF69B4", "#FF1493", "#8A2BE2", "#FF00FF", "#00FFFF",
        "#FFFF00", "#00FF00", "#FFA500", "#FF4500", "#32CD32"];
    private readonly Random random = new(seed);
    private readonly List<Logo> logos = [];
    private double clock;
    public Func<string, string, double, (double Width, double Height)>? MeasureText { get; set; }
    public event Action? CornerHit;
    public event Action? WallHit;
    public void Reset() { logos.Clear(); clock = 0; }

    public IReadOnlyList<BouncingTextCue> Step(double dt, double width, double height, double speed, double size,
        double alpha, BouncingTextOptions options, bool gentle, bool reduced)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, .1) : 0;
        width = Math.Max(1, width); height = Math.Max(1, height);
        var wanted = options.SecondText ? 2 : 1;
        while (logos.Count > wanted) logos.RemoveAt(logos.Count - 1);
        while (logos.Count < wanted)
        {
            var logo = new Logo { Text = PickText(options), X = random.NextDouble(), Y = random.NextDouble(),
                Phase = random.NextDouble() * Math.Tau, Hue = random.Next(12),
                Speed = 180 + random.NextDouble() * 120, SignX = random.Next(2) == 0 ? 1 : -1,
                SignY = random.Next(2) == 0 ? 1 : -1 };
            if (logos.Count == 0 && initial is { } start)
            { logo.X = Math.Clamp(start.X, 0, 1); logo.Y = Math.Clamp(start.Y, 0, 1); }
            logo.Color = NextColor(logo, options); logos.Add(logo);
        }
        if (!reduced) clock += dt * (gentle ? .5 : 1);
        var output = new List<BouncingTextCue>(wanted);
        for (var index = 0; index < logos.Count; index++)
        {
            var logo = logos[index]; var fontSize = 72 * size;
            if (logo.Text.Length == 0 || !options.PhrasePool.TryGetValue(logo.Text, out var on) || !on)
                logo.Text = PickText(options);
            if (logo.Text != logo.MeasuredText || fontSize != logo.FontSize || logo.Font != options.FontFamily)
            {
                var measured = MeasureText?.Invoke(logo.Text, options.FontFamily, fontSize)
                    ?? (fontSize * logo.Text.Length * .6, fontSize * 1.2);
                logo.Width = Math.Clamp(double.IsFinite(measured.Item1) ? measured.Item1 : 1, 1, 500000);
                logo.Height = Math.Clamp(double.IsFinite(measured.Item2) ? measured.Item2 : 1, 1, 500000);
                logo.MeasuredText = logo.Text; logo.Font = options.FontFamily; logo.FontSize = fontSize;
            }
            var travelX = Math.Max(0, width - logo.Width); var travelY = Math.Max(0, height - logo.Height);
            if (!reduced)
            {
                var motionDt = dt * (gentle ? .5 : 1);
                var vx = logo.Speed * speed / 10 * logo.SignX;
                var vy = logo.Speed * speed / 10 * logo.SignY;
                if (index == 0 && initial is { } start)
                { vx = start.VelocityX * travelX * speed / 5; vy = start.VelocityY * travelY * speed / 5;
                    vx = Math.Abs(vx) * logo.SignX; vy = Math.Abs(vy) * logo.SignY;
                    if (!logo.InitialDirection) { logo.SignX = Math.Sign(start.VelocityX); logo.SignY = Math.Sign(start.VelocityY);
                        vx = start.VelocityX * travelX * speed / 5; vy = start.VelocityY * travelY * speed / 5; logo.InitialDirection = true; } }
                var over = Overhang(logo.Width, logo.Height, logo.ScaleX, logo.ScaleY, logo.Angle);
                var marginX = Math.Min(over.X, travelX / 2); var marginY = Math.Min(over.Y, travelY / 2);
                var hitX = Advance(ref logo.X, ref logo.SignX, vx, motionDt, travelX, marginX);
                var hitY = Advance(ref logo.Y, ref logo.SignY, vy, motionDt, travelY, marginY);
                var corner = (hitX || hitY) && (logo.X * travelX <= 15 || (1 - logo.X) * travelX <= 15)
                    && (logo.Y * travelY <= 15 || (1 - logo.Y) * travelY <= 15);
                if (hitX || hitY)
                {
                    if (!gentle) logo.Color = NextColor(logo, options);
                    if (options.SquashStretch && !gentle) { logo.Squash = 0; logo.SquashX = hitX; }
                    if (random.NextDouble() < .1) logo.Text = PickText(options);
                    WallHit?.Invoke();
                }
                if (corner)
                { if (options.CornerBurst && !gentle) { logo.Burst = 0;
                        logo.BurstX = (logo.X * travelX + logo.Width / 2) / width;
                        logo.BurstY = (logo.Y * travelY + logo.Height / 2) / height; }
                    CornerHit?.Invoke(); }
                Transform(logo, options, motionDt, gentle);
            }
            if (options.ColorMode == 1) logo.Color = LockCardOptions.ValidColor(options.FixedColor, "#FF69B4");
            output.Add(new(logo.Text, logo.X, logo.Y, size, alpha) { Colour = logo.Color, FontFamily = options.FontFamily,
                ScaleX = logo.ScaleX, ScaleY = logo.ScaleY, Angle = logo.Angle, Outline = options.Outline,
                BurstSeconds = gentle || reduced || !options.CornerBurst ? -1 : logo.Burst,
                BurstCenterX = logo.BurstX, BurstCenterY = logo.BurstY });
        }
        return Array.AsReadOnly(output.ToArray());
    }

    private void Transform(Logo logo, BouncingTextOptions options, double dt, bool gentle)
    {
        var sx = 1d; var sy = 1d; var angle = 0d;
        if (options.Breathing) sx = sy = 1 + (gentle ? .04 : .08) * Math.Sin(clock * Math.Tau / 3.2 + logo.Phase);
        if (options.Wobble) angle += (gentle ? 3 : 6) * Math.Sin(clock * Math.Tau / 2.3 + logo.Phase);
        if (options.Spin) { if (!gentle) logo.Spin = (logo.Spin + 40 * dt) % 360; angle += logo.Spin; }
        if (options.VelocityTilt)
        {
            var target = Math.Clamp(Math.Atan2(logo.SignY * logo.SignX, 1) * 180 / Math.PI * .25, -12, 12);
            logo.Tilt += (target - logo.Tilt) * Math.Min(1, dt * 6); angle += logo.Tilt;
        }
        else logo.Tilt = 0;
        if (logo.Squash >= 0)
        {
            logo.Squash += dt;
            if (logo.Squash > .45 || gentle || !options.SquashStretch) logo.Squash = -1;
            else { var deform = .35 * Math.Exp(-6 * logo.Squash) * Math.Cos(24 * logo.Squash);
                if (logo.SquashX) { sx *= 1 - deform; sy *= 1 + deform * .7; }
                else { sy *= 1 - deform; sx *= 1 + deform * .7; } }
        }
        if (logo.Burst >= 0)
        {
            logo.Burst += dt;
            if (logo.Burst > .55 || gentle || !options.CornerBurst) logo.Burst = -1;
            else if (logo.Burst <= .5) { var pop = 1 + .4 * Math.Exp(-logo.Burst / .15); sx *= pop; sy *= pop; }
        }
        logo.ScaleX = sx; logo.ScaleY = sy; logo.Angle = angle;
    }

    public static (double X, double Y) Overhang(double width, double height, double sx, double sy, double angle)
    {
        var radians = angle * Math.PI / 180; var cos = Math.Abs(Math.Cos(radians)); var sin = Math.Abs(Math.Sin(radians));
        return (Math.Max(0, (width * sx * cos + height * sy * sin - width) / 2),
            Math.Max(0, (width * sx * sin + height * sy * cos - height) / 2));
    }
    private static bool Advance(ref double fraction, ref int sign, double velocity, double dt, double travel, double margin)
    {
        if (travel <= 0) { fraction = .5; return false; }
        if (dt <= 0 || velocity == 0) return false;
        var pos = fraction * travel + velocity * dt; var low = margin; var high = travel - margin;
        var hit = pos <= low && velocity < 0 || pos >= high && velocity > 0;
        if (pos <= low) { pos = low; sign = 1; } else if (pos >= high) { pos = high; sign = -1; }
        fraction = Math.Clamp(pos / travel, 0, 1); return hit;
    }
    private string PickText(BouncingTextOptions options)
    {
        var phrases = options.PhrasePool.Where(p => p.Value && !string.IsNullOrWhiteSpace(p.Key)).Select(p => p.Key).ToArray();
        return phrases.Length == 0 ? "Focus" : phrases[random.Next(phrases.Length)];
    }
    private string NextColor(Logo logo, BouncingTextOptions options)
    {
        if (options.ColorMode == 1) return LockCardOptions.ValidColor(options.FixedColor, "#FF69B4");
        if (options.ColorMode != 2) return Palette[random.Next(Palette.Length)];
        logo.Hue = (logo.Hue + 1) % 12;
        var h = logo.Hue / 2d; var x = (int)Math.Round(255 * (1 - Math.Abs(h % 2 - 1)));
        var (r, g, b) = ((int)h) switch { 0 => (255, x, 0), 1 => (x, 255, 0), 2 => (0, 255, x),
            3 => (0, x, 255), 4 => (x, 0, 255), _ => (255, 0, x) };
        return $"#{r:X2}{g:X2}{b:X2}";
    }
    private sealed class Logo
    {
        public string Text = "", MeasuredText = "", Font = "", Color = "#FF69B4";
        public double X, Y, Speed, Phase, Spin, Tilt, Width, Height, FontSize, Angle, ScaleX = 1, ScaleY = 1;
        public double Squash = -1, Burst = -1, BurstX, BurstY;
        public int SignX, SignY, Hue;
        public bool SquashX, InitialDirection;
    }
}
