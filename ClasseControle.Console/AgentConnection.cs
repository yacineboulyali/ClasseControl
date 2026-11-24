using System.Net.Sockets;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Connexion de contrôle de la console vers un agent élève.
/// Gère la poignée de main, l'envoi des commandes et la réception des images.
/// </summary>
internal sealed class AgentConnection : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public DiscoveryReply Info { get; }
    public bool IsConnected { get; private set; }
    public bool IsLocked { get; set; }
    public bool Selected { get; set; }

    public string Key => $"{Info.IpAddress}:{Info.ControlPort}";
    public string Title => $"{Info.MachineName}  ({Info.UserName})";

    /// <summary>Déclenché à la réception d'une image d'écran.</summary>
    public event Action<ScreenFramePayload>? FrameReceived;
    /// <summary>Déclenché quand l'état de connexion change (bool connecté).</summary>
    public event Action<AgentConnection, bool>? ConnectionChanged;

    public AgentConnection(DiscoveryReply info) => Info = info;

    public async Task<(bool ok, string reason)> ConnectAsync(string roomCode, string teacherName)
    {
        try
        {
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(Info.IpAddress, Info.ControlPort);
            _stream = _client.GetStream();

            await Wire.SendAsync(_stream, MessageType.Hello,
                new HelloPayload { RoomCode = roomCode, TeacherName = teacherName });

            var ack = await Wire.ReadAsync(_stream);
            var ap = ack is null ? null : Wire.Read<HelloAckPayload>(ack);
            if (ap is null || !ap.Accepted)
            {
                Dispose();
                return (false, ap?.Reason ?? "Connexion refusée");
            }

            IsConnected = true;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(_cts.Token));
            ConnectionChanged?.Invoke(this, true);
            return (true, "");
        }
        catch (Exception ex)
        {
            Dispose();
            return (false, ex.Message);
        }
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var env = await Wire.ReadAsync(_stream, ct);
                if (env is null) break;
                if (env.Type == MessageType.ScreenFrame)
                {
                    var f = Wire.Read<ScreenFramePayload>(env);
                    if (f != null) FrameReceived?.Invoke(f);
                }
            }
        }
        catch { }
        finally
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public Task Lock(string message) => Send(MessageType.Lock, new LockPayload { Message = message });
    public Task Unlock() => Send(MessageType.Unlock, new { });
    public Task ShowMessage(string title, string text, int seconds) =>
        Send(MessageType.ShowMessage, new ShowMessagePayload { Title = title, Text = text, DurationSeconds = seconds });
    public Task StartScreen(StartScreenPayload p) => Send(MessageType.StartScreen, p);
    public Task StopScreen() => Send(MessageType.StopScreen, new { });
    public Task SendInput(RemoteInputPayload p) => Send(MessageType.RemoteInput, p);
    public Task LaunchApp(string path, string args) =>
        Send(MessageType.LaunchApp, new LaunchAppPayload { Path = path, Arguments = args });
    public Task CloseApp(string proc) => Send(MessageType.CloseApp, new CloseAppPayload { ProcessName = proc });
    public Task PushFile(string name, byte[] content, bool open) =>
        Send(MessageType.FilePush, new FilePushPayload { FileName = name, Content = content, OpenAfter = open });

    private async Task Send<T>(MessageType type, T payload)
    {
        if (_stream == null || !IsConnected) return;
        await _writeLock.WaitAsync();
        try { await Wire.SendAsync(_stream, type, payload); }
        catch { }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        IsConnected = false;
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
    }
}
