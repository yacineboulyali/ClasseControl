using System.Security.Cryptography;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Diffuse l'écran du professeur vers une liste de postes.
/// Capture → JPEG une seule fois par image, puis envoi à chaque poste sans
/// l'attendre (un poste lent saute des images). Les images identiques à la
/// précédente ne sont pas renvoyées, sauf une image « clé » toutes les 2 s.
/// </summary>
internal sealed class ScreenBroadcaster
{
    private const int Fps = 4;
    private const int MaxWidth = 1366;
    private const int Quality = 55;

    private readonly ScreenCapture _capture = new();
    private readonly List<AgentConnection> _targets = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private BroadcastStartPayload _options = new();

    public bool IsRunning => _cts != null || _relaySource != null;

    /// <summary>Élève dont l'écran est relayé à la classe (null = écran du professeur).</summary>
    public AgentConnection? RelaySource => _relaySource;
    private AgentConnection? _relaySource;

    private static readonly StartScreenPayload RelayProfile =
        new() { MaxWidth = MaxWidth, Quality = Quality, Fps = Fps, AllowControl = false };

    public IReadOnlyList<AgentConnection> Targets
    {
        get { lock (_gate) return _targets.ToList(); }
    }

    public async Task StartAsync(IEnumerable<AgentConnection> targets, BroadcastStartPayload options)
    {
        if (IsRunning) return;
        _options = options;
        lock (_gate) { _targets.Clear(); _targets.AddRange(targets); }
        foreach (var t in Targets) await t.StartBroadcast(options);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => Loop(ct));
    }

    /// <summary>
    /// Diffuse l'écran d'un élève vers les autres postes : la console reçoit son
    /// flux d'écran et le relaie tel quel (aucune connexion directe entre élèves).
    /// </summary>
    public async Task StartRelayAsync(AgentConnection source, IEnumerable<AgentConnection> targets, BroadcastStartPayload options)
    {
        if (IsRunning) return;
        _options = options;
        lock (_gate) { _targets.Clear(); _targets.AddRange(targets.Where(t => t != source)); }
        _relaySource = source;
        source.FrameReceived += OnRelayFrame;
        foreach (var t in Targets) await t.StartBroadcast(options);
        await source.AcquireStream("relay", RelayProfile);
    }

    private void OnRelayFrame(ScreenFramePayload frame)
    {
        foreach (var t in Targets) t.TrySendBroadcastFrame(frame);
    }

    /// <summary>Ajoute un poste (re)connecté pendant une diffusion en cours.</summary>
    public async Task AddAsync(AgentConnection c)
    {
        if (!IsRunning || c == _relaySource) return;
        lock (_gate) { if (!_targets.Contains(c)) _targets.Add(c); }
        await c.StartBroadcast(_options);
    }

    public async Task StopAsync()
    {
        if (_relaySource is { } src)
        {
            _relaySource = null;
            src.FrameReceived -= OnRelayFrame;
            try { await src.ReleaseStream("relay"); } catch { }
        }
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        foreach (var t in Targets) { try { await t.StopBroadcast(); } catch { } }
        lock (_gate) _targets.Clear();
    }

    private async Task Loop(CancellationToken ct)
    {
        byte[]? lastHash = null;
        var lastKey = DateTime.MinValue;
        int delay = 1000 / Fps;
        while (!ct.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                var (jpeg, w, h, sw, sh) = _capture.CaptureJpeg(MaxWidth, Quality);
                byte[] hash = SHA1.HashData(jpeg);
                bool same = lastHash != null && hash.AsSpan().SequenceEqual(lastHash);
                if (!same || DateTime.UtcNow - lastKey > TimeSpan.FromSeconds(2))
                {
                    lastHash = hash;
                    lastKey = DateTime.UtcNow;
                    var frame = new ScreenFramePayload { Width = w, Height = h, ScreenWidth = sw, ScreenHeight = sh, Jpeg = jpeg };
                    foreach (var t in Targets) t.TrySendBroadcastFrame(frame);
                }
            }
            catch { /* capture impossible (écran verrouillé, UAC…) : on réessaie */ }

            int wait = delay - (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (wait > 0) await Task.Delay(wait, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
