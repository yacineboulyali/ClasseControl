using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>Actions UI que le serveur demande à l'application agent (thread UI).</summary>
internal interface IAgentHost
{
    void ShowLock(string message);
    void HideLock();
    void ShowMessage(string title, string text, int durationSeconds);
    void SetStatus(string status);
    void ShowBroadcast(string title, bool exclusive);
    void ShowBroadcastFrame(byte[] jpeg);
    void HideBroadcast();
    /// <summary>État de la main levée changé (par l'élève ou baissée par le prof).</summary>
    void SetHandRaised(bool raised);
}

/// <summary>
/// Serveur de l'agent élève : répond à la découverte UDP et traite les
/// connexions de contrôle TCP du professeur.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AgentServer
{
    public const string DiscoveryMagic = "CLASSECTRL_DISCOVER_V1";

    private readonly AgentSettings _settings;
    private readonly IAgentHost _host;
    private readonly CancellationTokenSource _cts = new();
    private readonly ScreenCapture _capture = new();
    private readonly RestrictionEnforcer _restrictions;

    /// <summary>Une connexion professeur active (pour pousser des événements élève → prof).</summary>
    private sealed record Session(NetworkStream Stream, SemaphoreSlim WriteLock, CancellationToken Ct);
    private readonly List<Session> _sessions = new();
    private volatile bool _handRaised;

    public AgentServer(AgentSettings settings, IAgentHost host)
    {
        _settings = settings;
        _host = host;
        _restrictions = new RestrictionEnforcer(host);
    }

    public bool HandRaised => _handRaised;

    /// <summary>L'élève lève ou baisse la main : notifie toutes les consoles connectées.</summary>
    public void SetHand(bool raised)
    {
        _handRaised = raised;
        _host.SetHandRaised(raised);
        _ = PushToAll(MessageType.HandRaise, new HandRaisePayload { Raised = raised });
    }

    private async Task PushToAll<T>(MessageType type, T payload)
    {
        Session[] targets;
        lock (_sessions) targets = _sessions.ToArray();
        foreach (var s in targets)
        {
            try { await SendLocked(s.Stream, s.WriteLock, type, payload, s.Ct); } catch { }
        }
    }

    public void Start()
    {
        _ = Task.Run(() => DiscoveryLoop(_cts.Token));
        _ = Task.Run(() => ControlLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        _restrictions.Dispose();
    }

    // ---------- Découverte UDP ----------
    private async Task DiscoveryLoop(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, Net.DiscoveryPort));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                string msg = Encoding.UTF8.GetString(result.Buffer);
                // Format attendu : "CLASSECTRL_DISCOVER_V1|<roomcode>"
                var parts = msg.Split('|');
                if (parts.Length < 2 || parts[0] != DiscoveryMagic) continue;
                if (!string.Equals(parts[1].Trim(), _settings.RoomCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var reply = new DiscoveryReply
                {
                    MachineName = _settings.DisplayName,
                    UserName = Environment.UserName,
                    IpAddress = LocalIp(result.RemoteEndPoint.Address),
                    ControlPort = Net.ControlPort,
                    AgentVersion = AgentInfo.Version,
                };
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(reply);
                await udp.SendAsync(data, data.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(500, ct).ContinueWith(_ => { }); }
        }
    }

    private static string LocalIp(IPAddress remote)
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(remote, 9);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "?"; }
    }

    // ---------- Contrôle TCP ----------
    private async Task ControlLoop(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, Net.ControlPort);
        listener.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClient(client, ct));
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleClient(TcpClient client, CancellationToken outerCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var ct = linked.Token;
        var writeLock = new SemaphoreSlim(1, 1);
        CancellationTokenSource? streamCts = null;
        Session? session = null;
        bool broadcasting = false;

        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            // --- Handshake ---
            var hello = await Wire.ReadAsync(stream, ct);
            if (hello is null || hello.Type != MessageType.Hello) return;
            var hp = Wire.Read<HelloPayload>(hello) ?? new HelloPayload();
            bool ok = string.Equals(hp.RoomCode?.Trim(), _settings.RoomCode, StringComparison.OrdinalIgnoreCase);

            await SendLocked(stream, writeLock, MessageType.HelloAck, new HelloAckPayload
            {
                Accepted = ok,
                Reason = ok ? "" : "Code de salle incorrect",
                MachineName = _settings.DisplayName,
                UserName = Environment.UserName,
            }, ct);
            if (!ok) return;

            _host.SetStatus($"Connecté — {hp.TeacherName}");

            session = new Session(stream, writeLock, ct);
            lock (_sessions) _sessions.Add(session);
            if (_handRaised)
                await SendLocked(stream, writeLock, MessageType.HandRaise, new HandRaisePayload { Raised = true }, ct);

            // --- Boucle de commandes ---
            while (!ct.IsCancellationRequested)
            {
                var env = await Wire.ReadAsync(stream, ct);
                if (env is null) break;

                switch (env.Type)
                {
                    case MessageType.Lock:
                        var lk = Wire.Read<LockPayload>(env) ?? new LockPayload();
                        _host.ShowLock(lk.Message);
                        break;

                    case MessageType.Unlock:
                        _host.HideLock();
                        break;

                    case MessageType.ShowMessage:
                        var sm = Wire.Read<ShowMessagePayload>(env) ?? new ShowMessagePayload();
                        _host.ShowMessage(sm.Title, sm.Text, sm.DurationSeconds);
                        break;

                    case MessageType.StartScreen:
                        var ss = Wire.Read<StartScreenPayload>(env) ?? new StartScreenPayload();
                        streamCts?.Cancel();
                        streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        _ = Task.Run(() => StreamLoop(stream, writeLock, ss, streamCts.Token));
                        break;

                    case MessageType.StopScreen:
                        streamCts?.Cancel();
                        streamCts = null;
                        break;

                    case MessageType.RemoteInput:
                        var ri = Wire.Read<RemoteInputPayload>(env);
                        if (ri != null) InputInjector.Apply(ri);
                        break;

                    case MessageType.FilePush:
                        var fp = Wire.Read<FilePushPayload>(env);
                        if (fp != null) ReceiveFile(fp);
                        break;

                    case MessageType.LaunchApp:
                        var la = Wire.Read<LaunchAppPayload>(env);
                        if (la != null) LaunchApp(la);
                        break;

                    case MessageType.CloseApp:
                        var ca = Wire.Read<CloseAppPayload>(env);
                        if (ca != null) CloseApp(ca);
                        break;

                    case MessageType.Shutdown:
                        Power("/s", Wire.Read<PowerPayload>(env));
                        break;

                    case MessageType.Restart:
                        Power("/r", Wire.Read<PowerPayload>(env));
                        break;

                    case MessageType.Logoff:
                        Power("/l", Wire.Read<PowerPayload>(env));
                        break;

                    case MessageType.BroadcastStart:
                        var bs = Wire.Read<BroadcastStartPayload>(env) ?? new BroadcastStartPayload();
                        broadcasting = true;
                        _host.ShowBroadcast(bs.Title, bs.Exclusive);
                        break;

                    case MessageType.BroadcastFrame:
                        var bf = Wire.Read<ScreenFramePayload>(env);
                        if (bf != null && broadcasting) _host.ShowBroadcastFrame(bf.Jpeg);
                        break;

                    case MessageType.BroadcastStop:
                        broadcasting = false;
                        _host.HideBroadcast();
                        break;

                    case MessageType.LowerHand:
                        _handRaised = false;
                        _host.SetHandRaised(false);
                        await PushToAll(MessageType.HandRaise, new HandRaisePayload { Raised = false });
                        break;

                    case MessageType.SetRestrictions:
                        var rp = Wire.Read<RestrictionsPayload>(env);
                        if (rp != null) _restrictions.Apply(rp);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { /* déconnexion */ }
        finally
        {
            streamCts?.Cancel();
            if (session != null) lock (_sessions) _sessions.Remove(session);
            // Si le professeur se déconnecte pendant une diffusion, on rend la main à l'élève.
            if (broadcasting) _host.HideBroadcast();
            try { client.Close(); } catch { }
            _host.SetStatus("En attente de connexion…");
        }
    }

    private async Task StreamLoop(NetworkStream stream, SemaphoreSlim writeLock,
                                  StartScreenPayload ss, CancellationToken ct)
    {
        int fps = Math.Clamp(ss.Fps, 1, 15);
        int delay = 1000 / fps;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (jpeg, w, h, sw, sh) = _capture.CaptureJpeg(ss.MaxWidth, ss.Quality);
                await SendLocked(stream, writeLock, MessageType.ScreenFrame, new ScreenFramePayload
                {
                    Width = w, Height = h, ScreenWidth = sw, ScreenHeight = sh, Jpeg = jpeg,
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
            await Task.Delay(delay, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private static async Task SendLocked<T>(NetworkStream stream, SemaphoreSlim writeLock,
                                            MessageType type, T payload, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try { await Wire.SendAsync(stream, type, payload, ct); }
        finally { writeLock.Release(); }
    }

    // ---------- Exécution des commandes fichiers / apps ----------
    private static void ReceiveFile(FilePushPayload fp)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Classe_controle");
            Directory.CreateDirectory(dir);
            string safe = Path.GetFileName(fp.FileName);
            if (string.IsNullOrWhiteSpace(safe)) safe = "fichier_recu";
            string path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, fp.Content);
            if (fp.OpenAfter)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    private static void LaunchApp(LaunchAppPayload la)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = la.Path,
                Arguments = la.Arguments,
                UseShellExecute = true, // permet aussi d'ouvrir une URL
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static void Power(string flag, PowerPayload? p)
    {
        try
        {
            p ??= new PowerPayload();
            // /l (déconnexion) n'accepte ni /t ni /c
            string args = flag == "/l"
                ? "/l"
                : $"{flag} /t {Math.Clamp(p.DelaySeconds, 0, 600)} /f" +
                  (string.IsNullOrWhiteSpace(p.Message) ? "" : $" /c \"{p.Message.Replace("\"", "'")}\"");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { }
    }

    private static void CloseApp(CloseAppPayload ca)
    {
        try
        {
            string name = ca.ProcessName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }
}
