using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClasseControle.App;

/// <summary>Application minimale qui affiche la fenêtre de choix du mode au premier démarrage.</summary>
public sealed class ChooserApp : Application
{
    public static AppMode Choice { get; set; } = AppMode.None;

    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.MainWindow = new ModeChooserWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// « Comment utiliser ce poste ? » : Poste du professeur ou Poste de l'élève.
/// Pour l'élève, le code de salle est demandé et enregistré dans agent.ini.
/// </summary>
internal sealed class ModeChooserWindow : Window
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(31, 78, 140));
    private static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(208, 215, 222));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(87, 96, 106));
    private static readonly IBrush Selected = new SolidColorBrush(Color.FromRgb(225, 237, 250));

    private readonly Border _teacherCard, _studentCard;
    private readonly StackPanel _studentOptions;
    private readonly TextBox _room = new() { Text = "CLASSE", Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _remember = new() { Content = "Se souvenir de ce choix sur ce poste", IsChecked = true };
    private readonly Button _start;
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private AppMode _mode = AppMode.None;

    public ModeChooserWindow()
    {
        Title = "Classe_controle — Choix du mode";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ClasseControle/app-icon.png"))); } catch { }

        _teacherCard = Card("Poste du professeur", "Voir et contrôler les écrans de la salle, verrouiller, diffuser, sondages, équipes…",
            "M3.5 4.5H20.5A1.5 1.5 0 0 1 22 6V15.5A1.5 1.5 0 0 1 20.5 17H3.5A1.5 1.5 0 0 1 2 15.5V6A1.5 1.5 0 0 1 3.5 4.5ZM8 20.5H16M12 17V20.5M7 9H11M7 12H14",
            AppMode.Teacher);
        _studentCard = Card("Poste de l'élève", "Fonctionne discrètement près de l'horloge et reçoit les consignes du professeur.",
            "M2 9L12 4.5L22 9L12 13.5ZM6 11V15.8C6 17.6 8.7 19 12 19C15.3 19 18 17.6 18 15.8V11M22 9V14.5",
            AppMode.Student);

        _studentOptions = new StackPanel
        {
            IsVisible = false, Spacing = 4, Margin = new Thickness(0, 14, 0, 0),
            Children =
            {
                new TextBlock { Text = "Code de salle (le même que sur la console du professeur) :" },
                _room,
            },
        };

        _start = new Button
        {
            Content = "Démarrer", IsEnabled = false, Padding = new Thickness(22, 8),
            Background = Accent, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
        };
        _start.Click += (_, _) => Confirm();
        var quit = new Button { Content = "Quitter", Padding = new Thickness(16, 8) };
        quit.Click += (_, _) => Close();

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        cards.Children.Add(_teacherCard);
        Grid.SetColumn(_studentCard, 2);
        cards.Children.Add(_studentCard);

        Content = new StackPanel
        {
            Margin = new Thickness(28, 24),
            Children =
            {
                new TextBlock { Text = "Bienvenue dans Classe_controle", FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Comment ce poste sera-t-il utilisé ?", FontSize = 14, Foreground = Muted, Margin = new Thickness(0, 4, 0, 18) },
                cards,
                _studentOptions,
                new Border { Height = 14 },
                _remember,
                _error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { quit, _start },
                },
            },
        };
    }

    private Border Card(string title, string text, string icon, AppMode mode)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(icon), Stroke = Accent, StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        var card = new Border
        {
            BorderBrush = Line, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.White,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Viewbox { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left,
                                  Child = new Canvas { Width = 24, Height = 24, Children = { path } } },
                    new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
        card.PointerPressed += (_, e) =>
        {
            Select(mode);
            if (e.ClickCount >= 2) Confirm();
        };
        return card;
    }

    private void Select(AppMode mode)
    {
        _mode = mode;
        foreach (var (card, m) in new[] { (_teacherCard, AppMode.Teacher), (_studentCard, AppMode.Student) })
        {
            card.BorderBrush = m == mode ? Accent : Line;
            card.Background = m == mode ? Selected : Brushes.White;
        }
        _studentOptions.IsVisible = mode == AppMode.Student;
        _start.IsEnabled = true;
        _error.Text = "";
    }

    private void Confirm()
    {
        if (_mode == AppMode.None) return;
        if (_mode == AppMode.Student)
        {
            string room = (_room.Text ?? "").Trim();
            if (room.Length == 0) { _error.Text = "Le code de salle est obligatoire."; return; }
            if (!TryWriteAgentIni(room))
                _error.Text = "Impossible d'enregistrer le code de salle à côté de l'application (dossier protégé). " +
                              "Utilisez l'installateur, ou copiez l'application dans un dossier où vous pouvez écrire.";
            if (!string.IsNullOrEmpty(_error.Text)) return;
        }
        if (_remember.IsChecked == true) ModeStore.SaveUserChoice(_mode);
        ChooserApp.Choice = _mode;
        Close();
    }

    /// <summary>Écrit agent.ini (lu par l'agent élève) à côté de l'exécutable.</summary>
    private static bool TryWriteAgentIni(string room)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "agent.ini");
            string name = Environment.MachineName;
            if (File.Exists(path))
                foreach (var l in File.ReadAllLines(path))
                    if (l.Trim().StartsWith("DisplayName=", StringComparison.OrdinalIgnoreCase) && l.Trim().Length > 12)
                        name = l.Trim()[12..];
            File.WriteAllText(path, $"# Configuration de l'agent Classe_controle\r\nRoomCode={room}\r\nDisplayName={name}\r\n");
            return true;
        }
        catch { return false; }
    }
}
