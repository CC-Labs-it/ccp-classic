using CCP.Classic.Core;
using SkiaSharp;

namespace CCP.Classic.App.Controls;

// Native Skia counterpart of the public Loom field shader and centerpiece compositor.
public static class LoomRenderer
{
    private static readonly Lazy<SKRuntimeEffect> Effect = new(() =>
        SKRuntimeEffect.CreateShader(Shader, out var error) ?? throw new InvalidOperationException("Spiral shader: " + error));

    public static SKBitmap Render(LoomDefinition definition, int longSide, double phase, bool gentle = false, bool performance = false)
    {
        var q = definition.Normalize();
        var (width, height) = LoomMath.Dimensions(q.Format, Math.Min(longSide, performance ? 320 : 512));
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try { using var canvas = new SKCanvas(bitmap); Draw(canvas, width, height, q, phase, gentle, performance); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
    private static void Draw(SKCanvas canvas, int width, int height, LoomDefinition q, double phase, bool gentle, bool performance)
    {
        phase = double.IsFinite(phase) ? phase - Math.Floor(phase) : 0;
        var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
        {
            ["res"] = new SKSize(width, height), ["phase"] = (float)phase, ["px"] = 2f / Math.Min(width, height),
            ["bgKind"] = q.Bg.Kind == "radial" ? 1f : 0f, ["bgA"] = Rgb(q.Bg.Color), ["bgB"] = Rgb(q.Bg.Outer),
            ["glow"] = (float)(gentle ? Math.Min(q.Glow, .2) : q.Glow),
            ["pulse"] = new float[] { gentle ? 0 : (float)q.Pulse.Amp, q.Pulse.Cycles },
            ["wobble"] = new float[] { performance || gentle ? 0 : (float)q.Wobble.Amp, q.Wobble.Freq, q.Wobble.Cycles },
            ["on2"] = q.Layer2.Enabled && !performance ? 1f : 0f
        };
        var hue = Math.Tau * q.HueCycles * phase;
        Upload(uniforms, "1", q.Layer, phase, hue); Upload(uniforms, "2", q.Layer2, phase, hue);
        using var shader = Effect.Value.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
        Centerpiece(canvas, q.Centerpiece, width, height, phase, hue, gentle);
        if (gentle) { using var veil = new SKPaint { Color = new SKColor(0, 0, 0, 48) }; canvas.DrawRect(0, 0, width, height, veil); }
    }
    private static void Upload(SKRuntimeEffectUniforms uniforms, string suffix, LoomLayer layer, double phase, double hue)
    {
        var style = layer.Style switch { "arch" => 1, "ribbon" => 2, "golden" => 3, "tunnel" => 4, "petal" => 5, _ => 0 };
        uniforms["weave" + suffix] = new float[] { layer.Arms, (float)layer.Turns, (float)layer.Duty, style };
        uniforms["thread" + suffix] = new float[] { layer.BandMode == "gradient" ? 1 : 0, layer.Colors.Length, (float)LoomMath.Rotation(layer, phase), 0 };
        var colors = new float[18];
        for (var index = 0; index < 6; index++) Hue(Rgb(layer.Colors[Math.Min(index, layer.Colors.Length - 1)]), hue).CopyTo(colors, index * 3);
        uniforms["colors" + suffix] = colors;
    }
    private static float[] Rgb(string value)
    {
        var color = SKColor.Parse(value);
        return [color.Red / 255f, color.Green / 255f, color.Blue / 255f];
    }
    private static float[] Hue(float[] rgb, double radians)
    {
        var c = Math.Cos(radians); var s = Math.Sin(radians); var r = rgb[0]; var g = rgb[1]; var b = rgb[2];
        return [Channel((.213 + c * .787 - s * .213) * r + (.715 - c * .715 - s * .715) * g + (.072 - c * .072 + s * .928) * b),
            Channel((.213 - c * .213 + s * .143) * r + (.715 + c * .285 + s * .140) * g + (.072 - c * .072 - s * .283) * b),
            Channel((.213 - c * .213 - s * .787) * r + (.715 - c * .715 + s * .715) * g + (.072 + c * .928 + s * .072) * b)];
    }
    private static float Channel(double value) => (float)Math.Clamp(value, 0, 1);
    private static void Centerpiece(SKCanvas canvas, LoomCenterpiece cp, int width, int height, double phase, double hue, bool gentle)
    {
        if (cp.Kind == "none") return;
        var rgb = Hue(Rgb(cp.Color), hue);
        var flash = cp.Kind == "mantra" && cp.FlashCycles > 0 && !gentle ? .5 * (1 + Math.Cos(Math.Tau * phase * cp.FlashCycles)) : 1;
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor((byte)Math.Round(rgb[0] * 255),
            (byte)Math.Round(rgb[1] * 255), (byte)Math.Round(rgb[2] * 255), (byte)Math.Round(flash * 255)) };
        var x = width / 2f; var y = height / 2f; var radius = (float)cp.SizeFrac * Math.Min(width, height) / 2;
        if (cp.Kind == "dot") canvas.DrawCircle(x, y, radius, paint);
        else if (cp.Kind == "star")
        {
            using var path = new SKPath();
            for (var index = 0; index < 10; index++)
            {
                var r = radius * (index % 2 == 0 ? 1 : .42); var angle = -Math.PI / 2 + index * Math.PI / 5;
                var point = new SKPoint(x + (float)(r * Math.Cos(angle)), y + (float)(r * Math.Sin(angle)));
                if (index == 0) path.MoveTo(point); else path.LineTo(point);
            }
            path.Close(); canvas.DrawPath(path, paint);
        }
        else if (cp.Kind is "cross" or "x")
        {
            canvas.Save(); canvas.Translate(x, y); if (cp.Kind == "x") canvas.RotateDegrees(45);
            canvas.DrawRect(-radius, -radius * .32f, radius * 2, radius * .64f, paint);
            canvas.DrawRect(-radius * .32f, -radius, radius * .64f, radius * 2, paint); canvas.Restore();
        }
        else if (cp.Kind == "mantra" && cp.Text.Length > 0 && flash > .01)
        {
            using var face = SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
            using var font = new SKFont(face, Math.Max(8, MathF.Round(radius)));
            var metrics = font.Metrics;
            canvas.DrawText(cp.Text, x, y - (metrics.Ascent + metrics.Descent) / 2, SKTextAlign.Center, font, paint);
        }
    }

    private static string LayerShader(string suffix) => """
        uniform float4 weave$, thread$;
        uniform float3 colors$[6];
        float3 pick$(float index) {
            float3 color = colors$[0];
            for (int i=1; i<6; i++) if (abs(float(i)-index)<0.5) color=colors$[i];
            return color;
        }
        float3 blend$(float position) {
            float index=floor(position);
            return mix(pick$(index),pick$(mod(index+1.0,thread$.y)),fract(position));
        }
        float4 layer$(float r,float th,float wob) {
            float arms=weave$.x, turns=weave$.y, duty=weave$.z, style=weave$.w;
            float count=thread$.y, rot=thread$.z, rc=clamp(r,0.0,1.0), t;
            if(style<0.5)t=pow(rc,0.5714286);
            else if(style<1.5)t=rc;
            else if(style<2.5)t=0.5-sin(asin(1.0-2.0*rc)/3.0);
            else t=log(1.0+rc*6.0)/1.9459101;
            float cov=0.0; float3 col=float3(0.0);
            if(style<4.5) {
                float position, aa;
                if(style<3.5) {
                    position=fract((th-rot-6.2831853*turns*t+wob)/6.2831853)*arms;
                    aa=min(px*arms/(6.2831853*max(rc,0.02)),0.45);
                } else {
                    float helix=floor((turns-0.5)/count+0.5)*count;
                    position=t*arms-rot*arms/6.2831853+helix*th/6.2831853+wob*0.5;
                    aa=min(px*arms*1.5,0.45);
                }
                float band=fract(position);
                cov=smoothstep(0.0,aa,band)*(1.0-smoothstep(duty,duty+aa,band));
                if(glow>0.001) {
                    float k=mix(14.0,4.0,glow);
                    float halo=exp(-max(0.0,band-duty)*k)+exp(-(1.0-band)*k);
                    cov=min(1.0,cov+glow*0.55*halo*(1.0-cov));
                }
                col=thread$.x<0.5?pick$(mod(floor(position),count)):blend$(fract(position/count)*count);
            } else {
                float a=th-rot-6.2831853*turns*t*0.35+wob;
                float m=0.5+0.5*cos(a*arms), aa=min(px*arms*2.0,0.45), threshold=1.0-duty;
                cov=smoothstep(threshold-aa,threshold+aa,m);
                cov*=smoothstep(0.03,0.18,rc)*(1.0-smoothstep(0.85,1.0,rc));
                if(glow>0.001)cov=min(1.0,cov+glow*0.5*smoothstep(threshold-0.35*glow-aa,threshold,m)*(1.0-cov));
                col=thread$.x<0.5?pick$(mod(floor(fract(a/6.2831853)*arms+0.5),count)):blend$(fract(a/6.2831853*arms/count)*count);
            }
            return float4(col,cov);
        }
        """.Replace("$", suffix, StringComparison.Ordinal);

    private static readonly string Shader = """
        uniform float2 res, pulse;
        uniform float phase, px, bgKind, glow, on2;
        uniform float3 bgA, bgB, wobble;
        """ + LayerShader("1") + LayerShader("2") + """
        half4 main(float2 xy) {
            float2 center=res*0.5;
            float2 p=(xy-center)/(length(center)*1.0253048);
            float r=length(p)/(1.0+pulse.x*sin(6.2831853*phase*pulse.y));
            float th=atan(p.y,p.x);
            float3 col=bgKind<0.5?bgA:mix(bgA,bgB,clamp(r*1.45,0.0,1.0));
            float wob=wobble.x>0.001?wobble.x*sin(6.2831853*(wobble.y*clamp(r,0.0,1.0)+phase*wobble.z)):0.0;
            float4 first=layer1(r,th,wob); col=mix(col,first.rgb,first.a);
            if(on2>0.5){float4 second=layer2(r,th,wob);col=mix(col,second.rgb,second.a*0.85);}
            return half4(col,1.0);
        }
        """;
}
