using System.Numerics;

namespace CCP.Classic.App.Media;

public readonly record struct AudioSpectralFeatures(double Rms, double Bass, double Onset);

// Public CCP AudioAnalyzer: 2048-point Hann FFT, 512-frame hop, bass and positive spectral flux.
// The streaming adapter keeps source timestamps and bounded feature bins; it never samples the mixer.
internal sealed class PcmSpectralFeatures(RmsEnvelope envelope, int rate)
{
    private const int Size = 2048, Hop = 512;
    private readonly double[] samples = new double[Size], priorSpectrum = new double[Size / 2];
    private readonly Complex[] fft = new Complex[Size];
    private int count, cursor, hop;
    private bool haveSpectrum;
    private double previousTime = double.NaN, chunkStart = double.NaN;
    public void Append(double sample, double seconds)
    {
        if (!double.IsFinite(seconds) || rate < 20) return;
        if (!double.IsFinite(chunkStart)) chunkStart = seconds;
        if (seconds - chunkStart >= 300)
        { envelope.FeatureTrack.Seal(); count = cursor = hop = 0; chunkStart = seconds; }
        if (double.IsFinite(previousTime) && Math.Abs(seconds - previousTime - 1d / rate) > 2d / rate)
        { envelope.FeatureTrack.Seal(); count = cursor = hop = 0; haveSpectrum = false; Array.Clear(priorSpectrum); chunkStart = seconds; }
        var filling = count < Size;
        previousTime = seconds; samples[cursor] = sample; cursor = (cursor + 1) % Size; count = Math.Min(Size, count + 1);
        if (count < Size) return;
        if (filling) hop = Hop - 1;
        if (++hop < Hop) return; hop = 0;
        double squares = 0;
        for (var i = 0; i < Size; i++)
        {
            var value = samples[(cursor + i) % Size] * .5 * (1 - Math.Cos(Math.Tau * i / (Size - 1)));
            fft[i] = new(value, 0); squares += value * value;
        }
        Transform(fft);
        double bass = 0, onset = 0, highEnergy = 0, bassFlux = 0, highFlux = 0;
        var low = Math.Max(1, (int)Math.Round(44100d / rate)); var high = (int)Math.Round(12d * 44100 / rate);
        var highLow = (int)Math.Round(93d * 44100 / rate); var highHigh = (int)Math.Round(300d * 44100 / rate);
        for (var i = 0; i < priorSpectrum.Length; i++)
        {
            var magnitude = fft[i].Magnitude;
            if (i >= low && i <= high) bass += magnitude;
            if (i >= highLow && i <= highHigh) highEnergy += magnitude;
            if (haveSpectrum)
            {
                var increase = Math.Max(0, magnitude - priorSpectrum[i]); onset += increase;
                if (i >= low && i <= high) bassFlux += increase;
                if (i >= highLow && i <= highHigh) highFlux += increase;
            }
            priorSpectrum[i] = magnitude;
        }
        var rms = Math.Sqrt(squares / Size);
        haveSpectrum = true;
        envelope.FeatureTrack.Append(new(Math.Max(chunkStart, seconds - (Size - 1d) / rate), (float)rms,
            (float)bass, (float)onset, (float)highFlux, (float)bassFlux, (float)highEnergy));
    }
    private static void Transform(Complex[] buffer)
    {
        var count = buffer.Length;
        for (int i = 1, j = 0; i < count; i++)
        {
            var bit = count >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
        for (var length = 2; length <= count; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -Math.Tau / length);
            for (var start = 0; start < count; start += length)
            {
                var phase = Complex.One;
                for (var i = 0; i < length / 2; i++)
                {
                    var even = buffer[start + i]; var odd = buffer[start + i + length / 2] * phase;
                    buffer[start + i] = even + odd; buffer[start + i + length / 2] = even - odd; phase *= step;
                }
            }
        }
    }
}
