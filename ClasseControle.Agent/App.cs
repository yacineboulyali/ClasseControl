using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.Versioning;

namespace ClasseControle.Agent;

[SupportedOSPlatform("windows")]
public sealed class App : Application, IAgentHost
{
    private AgentSettings _settings = new();
    private AgentServer? _server;
    private TrayIcon? _tray;
    private NativeMenuItem? _statusItem;
    private LockOverlay? _lock;

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
            SetStatus("En attente de connexion…");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray()
    {
        _statusItem = new NativeMenuItem("Démarrage…") { IsEnabled = false };
        var quit = new NativeMenuItem("Quitter l'agent");
        quit.Click += (_, _) => TryQuit();

        var menu = new NativeMenu();
        menu.Items.Add(_statusItem);
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

    private void TryQuit()
    {
        // Protection simple : il faut saisir le code de salle pour fermer l'agent.
        var dlg = new PasswordDialog(_settings.RoomCode);
        dlg.Closed += (_, _) =>
        {
            if (dlg.Success && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            {
                _server?.Stop();
                _tray!.IsVisible = false;
                d.Shutdown();
            }
        };
        dlg.Show();
        dlg.Activate();
    }

    // ---------- IAgentHost (appelé depuis les threads réseau) ----------
    public void ShowLock(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (_lock == null)
        {
            _lock = new LockOverlay(message);
            _lock.Closed += (_, _) => _lock = null;
            _lock.Show();
        }
        else _lock.UpdateMessage(message);
        _lock.Activate();
    });

    public void HideLock() => Dispatcher.UIThread.Post(() =>
    {
        try { _lock?.Close(); } catch { }
        _lock = null;
    });

    public void ShowMessage(string title, string text, int durationSeconds) =>
        Dispatcher.UIThread.Post(() => new MessagePopup(title, text, durationSeconds).Show());

    public void SetStatus(string status) => Dispatcher.UIThread.Post(() =>
    {
        if (_statusItem != null) _statusItem.Header = $"Classe_controle — {status}";
        if (_tray != null) _tray.ToolTipText = $"Classe_controle — {status}";
    });
}
