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
    void ShowLock(string message, byte[]? image, bool blank);
    void HideLock();
    void ShowMessage(string title, string text, int durationSeconds);
    void SetStatus(string status);
    void ShowBroadcast(string title, bool exclusive);
    void ShowBroadcastFrame(byte[] jpeg);
    void HideBroadcast();
    /// <summary>État de la main levée changé (par l'élève ou baissée par le prof).</summary>
    void SetHandRaised(bool raised);

    // --- Séance : identification obligatoire de l'élève ---
    void ShowLogin(SessionStartPayload session, string suggestedName);
    void LoginFailed(string message);
    void HideLogin();

    // --- Espace d'équipe ---
    void OpenTeam(GroupOpenPayload group);
    void TeamDoc(GroupDocPayload doc);
    void TeamChat(GroupChatPayload message);
    void CloseTeam(string groupId);
    void TeamDisconnected();

    // --- Communication / retour ---
    void ChatReceived(ChatPayload message);
    /// <summary>Blocage clavier/souris demandé par le professeur (écran visible).</summary>
    void SetInputLock(bool locked);
    void AnnouncementChanged(bool live, string speaker);
    void RewardChanged(RewardPayload reward);
    void ShowSurvey(SurveyPayload survey);
    void CloseSurvey(string surveyId);
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

    // Séance en cours (démarrée par une console) et identité de l'élève.
    private SessionStartPayload? _lesson;
    private Session? _lessonOwner;
    private string? _loggedLessonId;
    private string _studentName = "";
    private CancellationTokenSource? _lessonTimeout;

    // Groupe de travail (espace d'équipe).
    private Session? _groupOwner;

    private readonly AudioPlayer _audio = new();
    private Session? _inputLockOwner;
    private CancellationTokenSource? _inputLockTimeout;
    private Session? _surveyOwner;
    private Session? _audioOwner;

    /// <summary>Nom saisi par l'élève pour la séance (vide si non identifié).</summary>
    public string StudentName => _studentName;

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

    private static async Task SendTo<T>(Session? s, MessageType type, T payload)
    {
        if (s == null) return;
        try { await SendLocked(s.Stream, s.WriteLock, type, payload, s.Ct); } catch { }
    }

    /// <summary>L'élève a saisi son nom sur l'écran d'entrée en séance.</summary>
    public void SubmitLogin(string fullName)
    {
        var lesson = _lesson;
        var owner = _lessonOwner;
        if (lesson == null || owner == null)
        {
            _host.LoginFailed("Le professeur n'est pas connecté. Patientez quelques instants puis réessayez.");
            return;
        }
        _studentName = fullName;
        _ = SendTo(owner, MessageType.StudentLogin, new StudentLoginPayload { SessionId = lesson.SessionId, FullName = fullName });
    }

    /// <summary>L'élève écrit au professeur (toutes les consoles connectées le reçoivent).</summary>
    public void SendChat(string text) => _ = PushToAll(MessageType.Chat, new ChatPayload
    {
        Author = _studentName.Length > 0 ? _studentName : Environment.UserName,
        Text = text,
        Time = DateTime.UtcNow,
    });

    public void SendSurveyAnswer(string id, int choice) =>
        _ = SendTo(_surveyOwner, MessageType.SurveyAnswer, new SurveyAnswerPayload { Id = id, Choice = choice });

    public void SendGroupDoc(GroupDocPayload doc) => _ = SendTo(_groupOwner, MessageType.GroupDoc, doc);

    public void SendGroupChat(string groupId, string text) =>
        _ = SendTo(_groupOwner, MessageType.GroupChat, new GroupChatPayload { GroupId = groupId, Text = text });

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
        InternetBlocker.RecoverAtStartup();
        _ = Task.Run(() => DiscoveryLoop(_cts.Token));
        _ = Task.Run(() => ControlLoop(_cts.Token));
    }

    public void Stop()
    {
        // On ne laisse jamais un poste sans Internet quand l'agent s'arrête.
        try { if (InternetBlocker.IsBlocked) InternetBlocker.Unblock(); } catch { }
        try { if (AudioMuter.IsMuted) AudioMuter.SetMuted(false); } catch { }
        _cts.Cancel();
        _restrictions.Dispose();
        _audio.Dispose();
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
        CancellationTokenSource? appsCts = null;

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
                StudentName = _studentName,
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
                        _host.ShowLock(lk.Message, lk.ImageJpeg, lk.Blank);
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

                    case MessageType.SessionStart:
                        var st = Wire.Read<SessionStartPayload>(env);
                        if (st == null) break;
                        _lesson = st;
                        _lessonOwner = session;
                        _lessonTimeout?.Cancel();
                        if (_loggedLessonId == st.SessionId && _studentName.Length > 0)
                            // Déjà identifié pour cette séance (reconnexion) : on se réannonce.
                            await SendTo(session, MessageType.StudentLogin, new StudentLoginPayload { SessionId = st.SessionId, FullName = _studentName });
                        else
                            _host.ShowLogin(st, _studentName);
                        break;

                    case MessageType.StudentLoginAck:
                        var la2 = Wire.Read<StudentLoginAckPayload>(env) ?? new StudentLoginAckPayload();
                        if (la2.Accepted && _lesson != null)
                        {
                            _loggedLessonId = _lesson.SessionId;
                            _host.HideLogin();
                            _host.SetStatus($"Séance « {_lesson.Title} » — {_studentName}");
                        }
                        else if (!la2.Accepted) _host.LoginFailed(la2.Message);
                        break;

                    case MessageType.SessionEnd:
                        _lesson = null;
                        _lessonOwner = null;
                        _loggedLessonId = null;
                        _host.HideLogin();
                        break;

                    case MessageType.GroupOpen:
                        var go = Wire.Read<GroupOpenPayload>(env);
                        if (go == null) break;
                        _groupOwner = session;
                        _host.OpenTeam(go);
                        break;

                    case MessageType.GroupDoc:
                        var gd = Wire.Read<GroupDocPayload>(env);
                        if (gd != null) _host.TeamDoc(gd);
                        break;

                    case MessageType.GroupChat:
                        var gc = Wire.Read<GroupChatPayload>(env);
                        if (gc != null) _host.TeamChat(gc);
                        break;

                    case MessageType.GroupClose:
                        var gcl = Wire.Read<GroupClosePayload>(env) ?? new GroupClosePayload();
                        if (session == _groupOwner) _groupOwner = null;
                        _host.CloseTeam(gcl.GroupId);
                        break;

                    case MessageType.Chat:
                        var ch = Wire.Read<ChatPayload>(env);
                        if (ch != null) _host.ChatReceived(ch);
                        break;

                    case MessageType.AudioStart:
                        var au = Wire.Read<AudioStartPayload>(env) ?? new AudioStartPayload();
                        _audioOwner = session;
                        try { _audio.Start(au.SampleRate, au.Channels); _host.AnnouncementChanged(true, au.Speaker); } catch { }
                        break;

                    case MessageType.AudioChunk:
                        var ac = Wire.Read<AudioChunkPayload>(env);
                        if (ac != null && session == _audioOwner) _audio.Feed(ac.Pcm);
                        break;

                    case MessageType.AudioStop:
                        if (session == _audioOwner) { _audio.Stop(); _audioOwner = null; _host.AnnouncementChanged(false, ""); }
                        break;

                    case MessageType.Reward:
                        var rw = Wire.Read<RewardPayload>(env);
                        if (rw != null) _host.RewardChanged(rw);
                        break;

                    case MessageType.StudentDesktop:
                        var sd = Wire.Read<StudentDesktopPayload>(env);
                        if (sd != null) DesktopAction(sd.Action);
                        break;

                    case MessageType.InternetBlock:
                        var ib = Wire.Read<InternetBlockPayload>(env) ?? new InternetBlockPayload();
                        try
                        {
                            if (ib.Blocked)
                            {
                                InternetBlocker.Block();
                                _host.ShowMessage("Internet coupé", "Le professeur a coupé l'accès à Internet. Le réseau de la salle reste disponible.", 6);
                            }
                            else if (InternetBlocker.IsBlocked)
                            {
                                InternetBlocker.Unblock();
                                _host.ShowMessage("Internet rétabli", "L'accès à Internet est de nouveau disponible.", 4);
                            }
                        }
                        catch { }
                        break;

                    case MessageType.RemoveGames:
                        var replyTo = session;
                        _ = Task.Run(async () =>
                        {
                            string report = RemovePreinstalledGames();
                            await SendTo(replyTo, MessageType.AgentNotice, new AgentNoticePayload { Text = report });
                        });
                        break;

                    case MessageType.InputLock:
                        var il = Wire.Read<InputLockPayload>(env) ?? new InputLockPayload();
                        _inputLockTimeout?.Cancel();
                        _inputLockOwner = il.Locked ? session : null;
                        _host.SetInputLock(il.Locked);
                        break;

                    case MessageType.SetMute:
                        var mu = Wire.Read<MutePayload>(env) ?? new MutePayload();
                        if (mu.Muted != AudioMuter.IsMuted)
                        {
                            AudioMuter.SetMuted(mu.Muted);
                            _host.ShowMessage(mu.Muted ? "Son coupé" : "Son rétabli",
                                mu.Muted ? "Le professeur a coupé le son des applications." : "Le son des applications est rétabli.", 4);
                        }
                        break;

                    case MessageType.AppsWatch:
                        var aw = Wire.Read<AppsWatchPayload>(env) ?? new AppsWatchPayload();
                        appsCts?.Cancel();
                        appsCts = null;
                        if (aw.Enabled)
                        {
                            appsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            _ = Task.Run(() => AppsLoop(stream, writeLock, appsCts.Token));
                        }
                        break;

                    case MessageType.Survey:
                        var sv = Wire.Read<SurveyPayload>(env);
                        if (sv == null) break;
                        _surveyOwner = session;
                        _host.ShowSurvey(sv);
                        break;

                    case MessageType.SurveyClose:
                        var sc = Wire.Read<SurveyClosePayload>(env) ?? new SurveyClosePayload();
                        _host.CloseSurvey(sc.Id);
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
            appsCts?.Cancel();
            if (session != null) lock (_sessions) _sessions.Remove(session);
            // Si le professeur se déconnecte pendant une diffusion, on rend la main à l'élève.
            if (broadcasting) _host.HideBroadcast();
            if (session != null && session == _inputLockOwner)
            {
                // Console perdue : on débloque l'élève au bout de 60 s si elle ne revient pas.
                _inputLockOwner = null;
                var cts = new CancellationTokenSource();
                _inputLockTimeout = cts;
                _ = Task.Delay(TimeSpan.FromSeconds(60), cts.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled && _inputLockOwner == null) _host.SetInputLock(false);
                }, TaskScheduler.Default);
            }
            if (session != null && session == _audioOwner)
            {
                _audio.Stop();
                _audioOwner = null;
                _host.AnnouncementChanged(false, "");
            }
            if (session != null && session == _groupOwner)
            {
                _groupOwner = null;
                _host.TeamDisconnected();
            }
            if (session != null && session == _lessonOwner)
            {
                // Le professeur a perdu la connexion : on lui laisse 2 minutes pour
                // revenir avant de libérer le poste (évite de bloquer l'élève).
                _lessonOwner = null;
                var cts = new CancellationTokenSource();
                _lessonTimeout = cts;
                _ = Task.Delay(TimeSpan.FromMinutes(2), cts.Token).ContinueWith(t =>
                {
                    if (t.IsCanceled || _lessonOwner != null) return;
                    _lesson = null;
                    _host.HideLogin();
                }, TaskScheduler.Default);
            }
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

    /// <summary>
    /// Envoie la liste des applications ouvertes toutes les 2 s si elle a changé (et au moins
    /// toutes les 15 s). Chaque icône n'est envoyée qu'une fois par abonnement.
    /// </summary>
    private static async Task AppsLoop(NetworkStream stream, SemaphoreSlim writeLock, CancellationToken ct)
    {
        var iconsSent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string last = "";
        var lastSend = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var apps = AppWatcher.Snapshot();
                string sig = string.Join("\n", apps.Select(a => $"{a.ProcessName}|{a.Title}|{a.IsActive}"));
                if (sig != last || DateTime.UtcNow - lastSend > TimeSpan.FromSeconds(15))
                {
                    var payload = new AppsListPayload();
                    foreach (var a in apps)
                    {
                        payload.Apps.Add(new AppInfo
                        {
                            ProcessName = a.ProcessName, Name = a.Name, Title = a.Title, IsActive = a.IsActive,
                            Icon = a.Icon != null && iconsSent.Add(a.ProcessName) ? a.Icon : null,
                        });
                    }
                    await SendLocked(stream, writeLock, MessageType.AppsList, payload, ct);
                    last = sig;
                    lastSend = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }
            catch { }
            await Task.Delay(2000, ct).ContinueWith(_ => { }, TaskScheduler.Default);
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

    /// <summary>
    /// Désinstalle, pour l'utilisateur de la session, les jeux préinstallés de Windows
    /// (Solitaire, Candy Crush…). Ne demande pas de droits administrateur.
    /// </summary>
    private string RemovePreinstalledGames()
    {
        string machine = _settings.DisplayName;
        try
        {
            string patterns = string.Join(",", RestrictionCatalog.UwpGamePackagePatterns.Select(p => "'" + p.Replace("'", "''") + "'"));
            string script =
                "$ErrorActionPreference='SilentlyContinue';" +
                $"foreach($pat in @({patterns})){{" +
                "  Get-AppxPackage -Name $pat | ForEach-Object {" +
                "    try { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop; Write-Output $_.Name } catch {} } }";
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return $"{machine} : impossible de lancer PowerShell.";
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(180_000);
            var removed = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
            return removed.Count == 0
                ? $"{machine} : aucun jeu préinstallé trouvé (déjà désinstallés)."
                : $"{machine} : {removed.Count} jeu(x) désinstallé(s) — {string.Join(", ", removed.Select(Pretty))}";
        }
        catch (Exception ex) { return $"{machine} : échec de la désinstallation ({ex.Message})."; }

        // « Microsoft.MicrosoftSolitaireCollection » → « MicrosoftSolitaireCollection »
        static string Pretty(string pkg) => pkg.Contains('.') ? pkg[(pkg.LastIndexOf('.') + 1)..] : pkg;
    }

    // Processus jamais fermés par « Fermer toutes les applications ».
    private static readonly HashSet<string> KeepAlive = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ClasseControle.Agent", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "SearchApp", "TextInputHost", "ApplicationFrameHost", "SystemSettings", "LockApp", "dwm", "ctfmon",
    };

    private static void DesktopAction(DesktopAction action)
    {
        try
        {
            if (action == ClasseControle.Common.DesktopAction.MinimizeAll)
            {
                const ushort VK_LWIN = 0x5B, VK_M = 0x4D;
                InputInjector.Chord(VK_LWIN, VK_M);
                return;
            }
            // Fermer toutes les applications fenêtrées de la session de l'élève :
            // fermeture « polie » (comme le bouton ✕), puis forcée après 5 s.
            int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
            var targets = System.Diagnostics.Process.GetProcesses()
                .Where(p => { try { return p.SessionId == session && p.MainWindowHandle != IntPtr.Zero && !KeepAlive.Contains(p.ProcessName); } catch { return false; } })
                .ToList();
            foreach (var p in targets) { try { p.CloseMainWindow(); } catch { } }
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                foreach (var p in targets) { try { if (!p.HasExited) p.Kill(); } catch { } finally { p.Dispose(); } }
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
