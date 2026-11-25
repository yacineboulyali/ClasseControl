using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.Versioning;
using ClasseControle.Common;

namespace ClasseControle.Agent;

[SupportedOSPlatform("windows")]
public sealed class App : Application, IAgentHost
{
    private AgentSettings _settings = new();
    private AgentServer? _server;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private LockOverlay? _lock;
    private BroadcastWindow? _broadcast;
    private NativeMenuItem? _handItem;
    private NativeMenuItem? _teamItem;
    private LoginOverlay? _login;
    private TeamWindow? _team;
    private ChatWindow? _chat;
    private InputLockBanner? _inputBanner;
    private SurveyPopup? _survey;
    private int _stars;

    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _settings = AgentSettings.Load();
            SetupTray();

            _server = new AgentServer(_settings, this);
            _server.Start();

            // Mise à jour automatique : si la tâche SYSTEM a installé une nouvelle version
            // de ClasseControle.exe, l'agent redémarre dessus (vérification chaque minute).
            DispatcherTimer.Run(() =>
            {
                if (!ClasseControle.Common.AppVersion.NewerOnDisk()) return true;
                RestartOnNewVersion(desktop);
                return false;
            }, TimeSpan.FromMinutes(1));
            SetStatus("En attente de connexion…");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray()
    {
        _statusItem = new NativeMenuItem("Démarrage…") { IsEnabled = false };
        var quit = new NativeMenuItem("Quitter l'agent");
        quit.Click += (_, _) => TryQuit();

        _handItem = new NativeMenuItem("✋ Lever la main (demander de l'aide)");
        _handItem.Click += (_, _) => _server?.SetHand(!(_server?.HandRaised ?? false));

        var menu = new NativeMenu();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_handItem);
        _teamItem = new NativeMenuItem("👥 Espace d'équipe") { IsEnabled = false };
        _teamItem.Click += (_, _) => { if (_team != null) { _team.Show(); _team.Activate(); } };
        menu.Items.Add(_teamItem);
        var chatItem = new NativeMenuItem("💬 Écrire au professeur");
        chatItem.Click += (_, _) => OpenChat();
        menu.Items.Add(chatItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        WindowIcon? icon = null;
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://ClasseControle.Agent/app-icon.png"));
            icon = new WindowIcon(s);
        }
        catch { }

        _tray = new TrayIcon
        {
            ToolTipText = "Classe_controle — Agent",
            Menu = menu,
            IsVisible = true,
        };
        if (icon != null) _tray.Icon = icon;
    }

    private void RestartOnNewVersion(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            InputBlocker.ReleaseAll();
            _server?.Stop();
            if (_tray != null) _tray.IsVisible = false;
            ClasseControle.Common.AppRuntime.ReleaseSingleInstance?.Invoke();
            string? exe = Environment.ProcessPath;
            if (exe != null)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--eleve") { UseShellExecute = false });
        }
        catch { }
        desktop.Shutdown();
    }

    private void TryQuit()
    {
        // Protection simple : il faut saisir le code de salle pour fermer l'agent.
        var dlg = new PasswordDialog(_settings.RoomCode);
        dlg.Closed += (_, _) =>
        {
            if (dlg.Success && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            {
                InputBlocker.ReleaseAll();
                _server?.Stop();
                _tray!.IsVisible = false;
                d.Shutdown();
            }
        };
        dlg.Show();
        dlg.Activate();
    }

    // ---------- IAgentHost (appelé depuis les threads réseau) ----------
    // Priorité des fenêtres plein écran : verrouillage > entrée en séance > diffusion.
    // Chacune ne reprend le premier plan que si aucune fenêtre plus prioritaire n'est ouverte.
    private void RaiseTop()
    {
        _broadcast?.Activate();
        _login?.Activate();
        _lock?.Activate();
    }

    public void ShowLock(string message, byte[]? image, bool blank) => Dispatcher.UIThread.Post(() =>
    {
        if (_lock == null)
        {
            _lock = new LockOverlay(message, image, blank);
            _lock.Closed += (_, _) => _lock = null;
            _lock.Show();
        }
        else _lock.Update(message, image, blank);
        InputBlocker.Set("verrouillage", true); // Alt+Tab, touche Windows… n'échappent plus au verrouillage
        RaiseTop();
    });

    public void HideLock() => Dispatcher.UIThread.Post(() =>
    {
        try { _lock?.Close(); } catch { }
        _lock = null;
        InputBlocker.Set("verrouillage", false);
        RaiseTop();
    });

    public void ShowMessage(string title, string text, int durationSeconds) =>
        Dispatcher.UIThread.Post(() => new MessagePopup(title, text, durationSeconds).Show());

    public void SetStatus(string status) => Dispatcher.UIThread.Post(() =>
    {
        if (_statusItem != null) _statusItem.Header = $"Classe_controle — {status}";
        if (_tray != null) _tray.ToolTipText = $"Classe_controle — {status}";
    });

    public void ShowBroadcast(string title, bool exclusive) => Dispatcher.UIThread.Post(() =>
    {
        if (_broadcast == null)
        {
            _broadcast = new BroadcastWindow(title, exclusive);
            _broadcast.YieldTo = () => _lock != null || _login != null;
            _broadcast.Closed += (_, _) => _broadcast = null;
            _broadcast.Show();
        }
        else _broadcast.SetMode(title, exclusive);
        InputBlocker.Set("diffusion", exclusive);
        RaiseTop();
    });

    public void ShowBroadcastFrame(byte[] jpeg) => Dispatcher.UIThread.Post(() => _broadcast?.ShowFrame(jpeg));

    public void HideBroadcast() => Dispatcher.UIThread.Post(() =>
    {
        try { _broadcast?.ForceClose(); } catch { }
        _broadcast = null;
        InputBlocker.Set("diffusion", false);
    });

    public void SetHandRaised(bool raised) => Dispatcher.UIThread.Post(() =>
    {
        if (_handItem != null)
            _handItem.Header = raised ? "✋ Baisser la main" : "✋ Lever la main (demander de l'aide)";
        if (_tray != null)
            _tray.ToolTipText = raised ? "Classe_controle — main levée ✋" : "Classe_controle — Agent";
    });

    // ---------- Séance ----------
    public void ShowLogin(SessionStartPayload session, string suggestedName) => Dispatcher.UIThread.Post(() =>
    {
        if (_login == null)
        {
            _login = new LoginOverlay(session.Title, session.TeacherName, suggestedName);
            _login.YieldTo = () => _lock != null;
            _login.Submitted += name => _server?.SubmitLogin(name);
            _login.Closed += (_, _) => _login = null;
            _login.Show();
        }
        else _login.SetSession(session.Title, session.TeacherName);
        RaiseTop();
    });

    public void LoginFailed(string message) => Dispatcher.UIThread.Post(() => _login?.ShowError(message));

    public void HideLogin() => Dispatcher.UIThread.Post(() =>
    {
        try { _login?.ForceClose(); } catch { }
        _login = null;
        RaiseTop();
    });

    // ---------- Espace d'équipe ----------
    public void OpenTeam(GroupOpenPayload group) => Dispatcher.UIThread.Post(() =>
    {
        _team ??= new TeamWindow(doc => _server?.SendGroupDoc(doc),
                                 text => { if (_team != null) _server?.SendGroupChat(_team.GroupId, text); });
        _team.Open(group);
        _team.Show();
        _team.Activate();
        if (_teamItem != null) _teamItem.IsEnabled = true;
        RaiseTop();
    });

    public void TeamDoc(GroupDocPayload doc) => Dispatcher.UIThread.Post(() => _team?.ReceiveDoc(doc));

    public void TeamChat(GroupChatPayload message) => Dispatcher.UIThread.Post(() => _team?.ReceiveChat(message));

    public void CloseTeam(string groupId) => Dispatcher.UIThread.Post(() =>
    {
        if (_team == null || (groupId.Length > 0 && _team.GroupId != groupId)) return;
        _team.ForceClose();
        _team = null;
        if (_teamItem != null) _teamItem.IsEnabled = false;
    });

    public void TeamDisconnected() => Dispatcher.UIThread.Post(() => _team?.Disconnected());

    // ---------- Discussion / annonces / récompenses / sondages ----------
    private void OpenChat()
    {
        _chat ??= new ChatWindow(text => _server?.SendChat(text));
        _chat.Show();
        _chat.Activate();
    }

    public void SetInputLock(bool locked) => Dispatcher.UIThread.Post(() =>
    {
        InputBlocker.Set("prof", locked);
        if (locked)
        {
            _inputBanner ??= new InputLockBanner();
            _inputBanner.Show();
        }
        else
        {
            try { _inputBanner?.Close(); } catch { }
            _inputBanner = null;
        }
        if (_tray != null) _tray.ToolTipText = locked ? "Classe_controle — clavier et souris bloqués" : "Classe_controle — Agent";
    });

    public void ChatReceived(ChatPayload message) => Dispatcher.UIThread.Post(() =>
    {
        OpenChat();
        _chat!.Add(message);
    });

    public void AnnouncementChanged(bool live, string speaker) => Dispatcher.UIThread.Post(() =>
    {
        if (_tray != null)
            _tray.ToolTipText = live ? $"Classe_controle — 🔊 annonce de {speaker}" : "Classe_controle — Agent";
        if (live) new MessagePopup("🔊 Annonce", $"{speaker} parle à la classe. Montez le son si besoin.", 4).Show();
    });

    public void RewardChanged(RewardPayload reward) => Dispatcher.UIThread.Post(() =>
    {
        _stars = reward.Stars;
        if (_tray != null) _tray.ToolTipText = $"Classe_controle — ⭐ {_stars} étoile(s)";
        if (reward.Delta > 0)
            new MessagePopup("⭐ Bravo !", $"Le professeur vous a donné une étoile.\nTotal : {new string('⭐', Math.Min(_stars, 10))} ({_stars})", 5).Show();
    });

    public void ShowSurvey(SurveyPayload survey) => Dispatcher.UIThread.Post(() =>
    {
        try { _survey?.ForceClose(); } catch { }
        _survey = new SurveyPopup(survey, choice => _server?.SendSurveyAnswer(survey.Id, choice));
        _survey.Closed += (_, _) => _survey = null;
        _survey.Show();
        _survey.Activate();
        RaiseTop();
    });

    public void CloseSurvey(string surveyId) => Dispatcher.UIThread.Post(() =>
    {
        if (_survey != null && (surveyId.Length == 0 || _survey.SurveyId == surveyId))
        {
            _survey.ForceClose();
            _survey = null;
        }
    });
}
