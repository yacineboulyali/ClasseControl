using System.Runtime.Versioning;
using NAudio.Wave;

namespace ClasseControle.Agent;

/// <summary>
/// Son de notification doux (deux notes, « ding-dong » descendant), généré en mémoire :
/// aucun fichier audio à embarquer. Joué par l'agent, donc audible même quand le
/// professeur a coupé le son des autres applications.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Chime
{
    private const int Rate = 44100;
    private static readonly Lazy<byte[]> Pcm = new(Build);

    private static byte[] Build()
    {
        // Deux notes (Mi6 puis Do6) avec harmonique douce et décroissance exponentielle.
        var notes = new (double freq, double start, double length)[] { (1318.5, 0.00, 0.55), (1046.5, 0.22, 0.85) };
        int total = (int)(Rate * 1.1);
        var mix = new double[total];
        foreach (var (f, start, len) in notes)
        {
            int s0 = (int)(start * Rate), n = (int)(len * Rate);
            for (int i = 0; i < n && s0 + i < total; i++)
            {
                double t = (double)i / Rate;
                double env = Math.Exp(-t * 5.5) * Math.Min(1, i / (Rate * 0.004)); // attaque douce, pas de clic
                mix[s0 + i] += env * (Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(2 * Math.PI * 2 * f * t));
            }
        }
        double peak = mix.Max(Math.Abs);
        var bytes = new byte[total * 2];
        for (int i = 0; i < total; i++)
        {
            short v = (short)(mix[i] / peak * 0.6 * short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xFF);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }
        return bytes;
    }

    public static void Play()
    {
        try
        {
            var stream = new RawSourceWaveStream(new MemoryStream(Pcm.Value), new WaveFormat(Rate, 16, 1));
            var output = new WaveOutEvent();
            output.Init(stream);
            output.PlaybackStopped += (_, _) => { output.Dispose(); stream.Dispose(); };
            output.Play();
        }
        catch { }
    }
}
