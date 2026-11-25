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
    public bool IsHandRaised { get; private set; }
    public bool IsBlanked { get; set; }
    public bool IsInternetBlocked { get; set; }
    public bool IsMuted { get; set; }
    public bool IsInputLocked { get; set; }
    public bool IsNetLocked { get; set; }
    /// <summary>Restrictions appliquées à ce poste (applications et sites interdits).</summary>
    public RestrictionsPayload Restrictions { get; private set; } = new();
    public event Action<AgentConnection, string>? Notice;

    /// <summary>Applications ouvertes sur le poste (dernière liste reçue), l'active en premier.</summary>
    public IReadOnlyList<AppInfo> Apps { get; private set; } = Array.Empty<AppInfo>();
    /// <summary>Icônes PNG reçues, par nom de processus (envoyées une fois par l'agent).</summary>
    public Dictionary<string, byte[]> AppIcons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public event Action<AgentConnection>? AppsChanged;
    /// <summary>Étoiles (récompenses) obtenues pendant la séance.</summary>
    public int Stars { get; set; }
    /// <summary>Nom saisi par l'élève en début de séance (vide s'il ne s'est pas identifié).</summary>
    public string StudentName { get; set; } = "";
    /// <summary>Nom à afficher : celui de l'élève s'il s'est identifié, sinon l'utilisateur Windows.</summary>
    public string DisplayName => StudentName.Length > 0 ? StudentName : Info.UserName;

    public string Key => $"{Info.IpAddress}:{Info.ControlPort}";
    public string Title => $"{Info.MachineName}  ({Info.UserName})";

    /// <summary>Déclenché à la réception d'une image d'écran.</summary>
    public event Action<ScreenFramePayload>? FrameReceived;
    /// <summary>Déclenché quand l'état de connexion change (bool connecté).</summary>
    public event Action<AgentConnection, bool>? ConnectionChanged;
    /// <summary>Déclenché quand l'élève lève ou baisse la main.</summary>
    public event Action<AgentConnection, bool>? HandChanged;
    public event Action<AgentConnection, StudentLoginPayload>? StudentLogin;
    public event Action<AgentConnection, GroupDocPayload>? GroupDocReceived;
    public event Action<AgentConnection, GroupChatPayload>? GroupChatReceived;
    public event Action<AgentConnection, ChatPayload>? ChatReceived;
    public event Action<AgentConnection, SurveyAnswerPayload>? SurveyAnswered;

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

            if (!string.IsNullOrWhiteSpace(ap.StudentName)) StudentName = ap.StudentName;
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
                else if (env.Type == MessageType.StudentLogin)
                {
                    var l = Wire.Read<StudentLoginPayload>(env);
                    if (l != null) StudentLogin?.Invoke(this, l);
                }
                else if (env.Type == MessageType.GroupDoc)
                {
                    var d = Wire.Read<GroupDocPayload>(env);
                    if (d != null) GroupDocReceived?.Invoke(this, d);
                }
                else if (env.Type == MessageType.GroupChat)
                {
                    var c = Wire.Read<GroupChatPayload>(env);
                    if (c != null) GroupChatReceived?.Invoke(this, c);
                }
                else if (env.Type == MessageType.AppsList)
                {
                    var al = Wire.Read<AppsListPayload>(env);
                    if (al != null)
                    {
                        lock (AppIcons)
                            foreach (var a in al.Apps)
                                if (a.Icon is { Length: > 0 }) AppIcons[a.ProcessName] = a.Icon;
                        Apps = al.Apps;
                        AppsChanged?.Invoke(this);
                    }
                }
                else if (env.Type == MessageType.AgentNotice)
                {
                    var n = Wire.Read<AgentNoticePayload>(env);
                    if (n != null) Notice?.Invoke(this, n.Text);
                }
                else if (env.Type == MessageType.Chat)
                {
                    var m = Wire.Read<ChatPayload>(env);
                    if (m != null) ChatReceived?.Invoke(this, m);
                }
                else if (env.Type == MessageType.SurveyAnswer)
                {
                    var a = Wire.Read<SurveyAnswerPayload>(env);
                    if (a != null) SurveyAnswered?.Invoke(this, a);
                }
                else if (env.Type == MessageType.HandRaise)
                {
                    var h = Wire.Read<HandRaisePayload>(env);
                    if (h != null && h.Raised != IsHandRaised)
                    {
                        IsHandRaised = h.Raised;
                        HandChanged?.Invoke(this, h.Raised);
                    }
                }
            }
        }
        catch { }
        finally
        {
            IsConnected = false;
            if (IsHandRaised) { IsHandRaised = false; HandChanged?.Invoke(this, false); }
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public Task Lock(string message, byte[]? image = null) =>
        Send(MessageType.Lock, new LockPayload { Message = message, ImageJpeg = image });
    public Task SetNetLock(bool locked)
    {
        IsNetLocked = locked;
        return Send(MessageType.NetLock, new NetLockPayload { Locked = locked });
    }

    public Task Annotate(AnnotationPayload p) => Send(MessageType.Annotate, p);

    public Task SetInputLock(bool locked)
    {
        IsInputLocked = locked;
        return Send(MessageType.InputLock, new InputLockPayload { Locked = locked });
    }

    public Task SetMute(bool muted)
    {
        IsMuted = muted;
        return Send(MessageType.SetMute, new MutePayload { Muted = muted });
    }

    public Task WatchApps(bool enabled)
    {
        if (!enabled) { Apps = Array.Empty<AppInfo>(); lock (AppIcons) AppIcons.Clear(); }
        return Send(MessageType.AppsWatch, new AppsWatchPayload { Enabled = enabled });
    }

    public byte[]? IconFor(string processName)
    {
        lock (AppIcons) return AppIcons.TryGetValue(processName, out var b) ? b : null;
    }

    public Task SetInternet(bool blocked)
    {
        IsInternetBlocked = blocked;
        return Send(MessageType.InternetBlock, new InternetBlockPayload { Blocked = blocked });
    }
    public Task Blank() => Send(MessageType.Lock, new LockPayload { Blank = true, Message = "" });
    public Task Chat(ChatPayload p) => Send(MessageType.Chat, p);
    public Task StartAudio(AudioStartPayload p) => Send(MessageType.AudioStart, p);
    public Task SendAudio(byte[] pcm) => Send(MessageType.AudioChunk, new AudioChunkPayload { Pcm = pcm });
    public Task StopAudio() => Send(MessageType.AudioStop, new { });
    public Task Reward(int delta)
    {
        Stars = delta == 0 ? 0 : Math.Max(0, Stars + delta);
        return Send(MessageType.Reward, new RewardPayload { Stars = Stars, Delta = delta });
    }
    public Task Desktop(DesktopAction a) => Send(MessageType.StudentDesktop, new StudentDesktopPayload { Action = a });
    public Task StartSurvey(SurveyPayload p) => Send(MessageType.Survey, p);
    public Task CloseSurvey(string id) => Send(MessageType.SurveyClose, new SurveyClosePayload { Id = id });
    public Task StartLesson(SessionStartPayload p) => Send(MessageType.SessionStart, p);
    public Task EndLesson() => Send(MessageType.SessionEnd, new { });
    public Task AckLogin(bool ok, string message) =>
        Send(MessageType.StudentLoginAck, new StudentLoginAckPayload { Accepted = ok, Message = message });
    public Task OpenGroup(GroupOpenPayload p) => Send(MessageType.GroupOpen, p);
    public Task SendGroupDoc(GroupDocPayload p) => Send(MessageType.GroupDoc, p);
    public Task SendGroupChat(GroupChatPayload p) => Send(MessageType.GroupChat, p);
    public Task CloseGroup(string id) => Send(MessageType.GroupClose, new GroupClosePayload { GroupId = id });
    public Task Unlock() => Send(MessageType.Unlock, new { });
    public Task ShowMessage(string title, string text, int seconds) =>
        Send(MessageType.ShowMessage, new ShowMessagePayload { Title = title, Text = text, DurationSeconds = seconds });
    public Task SendInput(RemoteInputPayload p) => Send(MessageType.RemoteInput, p);
    public Task Shutdown(int delay, string msg) => Send(MessageType.Shutdown, new PowerPayload { DelaySeconds = delay, Message = msg });
    public Task Restart(int delay, string msg) => Send(MessageType.Restart, new PowerPayload { DelaySeconds = delay, Message = msg });
    public Task Logoff() => Send(MessageType.Logoff, new PowerPayload());

    // ---------- Arbitrage du flux d'écran ----------
    // Plusieurs consommateurs (miniature + fenêtre de contrôle) partagent une
    // seule connexion : on envoie StartScreen avec le profil le plus exigeant,
    // et StopScreen seulement quand plus personne ne regarde.
    private readonly object _streamLock = new();
    private readonly Dictionary<string, StartScreenPayload> _streamReqs = new();

    public Task AcquireStream(string consumerKey, StartScreenPayload profile)
    {
        lock (_streamLock) _streamReqs[consumerKey] = profile;
        return ApplyStreamProfile();
    }

    public Task ReleaseStream(string consumerKey)
    {
        lock (_streamLock) _streamReqs.Remove(consumerKey);
        return ApplyStreamProfile();
    }

    private Task ApplyStreamProfile()
    {
        StartScreenPayload? combined = null;
        lock (_streamLock)
        {
            if (_streamReqs.Count > 0)
            {
                combined = new StartScreenPayload
                {
                    MaxWidth = _streamReqs.Values.Max(p => p.MaxWidth),
                    Quality = _streamReqs.Values.Max(p => p.Quality),
                    Fps = _streamReqs.Values.Max(p => p.Fps),
                    AllowControl = _streamReqs.Values.Any(p => p.AllowControl),
                };
            }
        }
        return combined != null
            ? Send(MessageType.StartScreen, combined)
            : Send(MessageType.StopScreen, new { });
    }
    public Task LaunchApp(string path, string args) =>
        Send(MessageType.LaunchApp, new LaunchAppPayload { Path = path, Arguments = args });
    public Task CloseApp(string proc) => Send(MessageType.CloseApp, new CloseAppPayload { ProcessName = proc });
    public Task PushFile(string name, byte[] content, bool open) =>
        Send(MessageType.FilePush, new FilePushPayload { FileName = name, Content = content, OpenAfter = open });

    public Task LowerHand() => Send(MessageType.LowerHand, new { });
    public Task SetRestrictions(RestrictionsPayload p)
    {
        Restrictions = p.Clone();
        return Send(MessageType.SetRestrictions, p);
    }
    public Task RemoveGames() => Send(MessageType.RemoveGames, new { });
    public Task UpdateNow() => Send(MessageType.UpdateNow, new { });

    /// <summary>Version de l'agent (annoncée à la découverte).</summary>
    public Version? AgentVersion => AppVersion.Parse(Info.AgentVersion);
    /// <summary>L'agent a une version plus ancienne que la console.</summary>
    public bool IsOutdated => AgentVersion is { } v && v < AppVersion.Running;
    public Task StartBroadcast(BroadcastStartPayload p) => Send(MessageType.BroadcastStart, p);
    public Task StopBroadcast() => Send(MessageType.BroadcastStop, new { });

    // Une image de diffusion n'est envoyée que si la précédente est partie :
    // un poste lent ne ralentit pas les autres, il saute simplement des images.
    private int _broadcastBusy;
    public void TrySendBroadcastFrame(ScreenFramePayload frame)
    {
        if (!IsConnected || Interlocked.CompareExchange(ref _broadcastBusy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await Send(MessageType.BroadcastFrame, frame); }
            finally { Interlocked.Exchange(ref _broadcastBusy, 0); }
        });
    }

    public async Task Send<T>(MessageType type, T payload)
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
