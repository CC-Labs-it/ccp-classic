using System.Text;

namespace CCP.Classic.App.Media;

public static class ProceduralAudio
{
    // A short falling tone with a soft attack. This is a sound effect, never speech.
    public static byte[] PopWave()
    {
        const int sampleRate = 22050, samples = sampleRate / 10;
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        double phase = 0;
        for (var index = 0; index < samples; index++)
        {
            var time = (double)index / sampleRate;
            phase += 2 * Math.PI * (250 + 950 * Math.Exp(-time * 35)) / sampleRate;
            var envelope = Math.Min(1, time * 1000) * Math.Exp(-time * 70);
            writer.Write((short)(Math.Sin(phase) * envelope * 0.2 * short.MaxValue));
        }
        writer.Flush();
        return bytes.ToArray();
    }
}
