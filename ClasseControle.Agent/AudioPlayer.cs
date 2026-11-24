using System.Runtime.Versioning;
using NAudio.Wave;

namespace ClasseControle.Agent;

/// <summary>Lecture des annonces audio du professeur (flux PCM reçu en continu).</summary>
[SupportedOSPlatform("windows")]
internal sealed class AudioPlayer : IDisposable
{
    private readonly object _gate = new();
    private WaveOutEvent? _out;
    private BufferedWaveProvider? _buffer;

    public void Start(int sampleRate, int channels)
    {
        lock (_gate)
        {
            StopLocked();
            _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, Math.Clamp(channels, 1, 2)))
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true, // en cas de retard réseau on saute plutôt que d'accumuler
            };
            _out = new WaveOutEvent { DesiredLatency = 200 };
            _out.Init(_buffer);
            _out.Play();
        }
    }

    public void Feed(byte[] pcm)
    {
        lock (_gate) _buffer?.AddSamples(pcm, 0, pcm.Length);
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    private void StopLocked()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        _buffer = null;
    }

    public void Dispose() => Stop();
}
