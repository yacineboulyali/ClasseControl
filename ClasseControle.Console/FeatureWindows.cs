using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Console;

internal static class Ui
{
    public static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(139, 148, 158));
    public static readonly IBrush Panel = new SolidColorBrush(Color.FromRgb(22, 27, 34));
    public static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(48, 54, 61));

    public static Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(16, 6, 16, 6) };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel ButtonRow(params Control[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    public static TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
    };

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>Options avant de diffuser l'écran du professeur.</summary>
internal sealed class BroadcastDialog : Window
{
    public BroadcastStartPayload? Result { get; private set; }

    public BroadcastDialog(int targetCount, string what = "Votre écran")
    {
        Title = "Diffuser mon écran";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var exclusive = new CheckBox { Content = "Plein écran et bloquer clavier/souris des élèves", IsChecked = true };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = $"{what} va être affiché sur {targetCount} poste(s).", TextWrapping = TextWrapping.Wrap },
                new Border { Height = 12 },
                exclusive,
                Ui.Hint("Décoché : l'écran s'ouvre dans une fenêtre que l'élève peut déplacer, pour suivre en parallèle."),
                Ui.Hint("Astuce : réduisez la console pendant la diffusion pour montrer votre application."),
                Ui.ButtonRow(
                    Ui.Btn("Annuler", Close),
                    Ui.Btn("📺 Diffuser", () =>
                    {
                        Result = new BroadcastStartPayload { Exclusive = exclusive.IsChecked == true };
                        Close();
                    })),
            },
        };
    }
}

/// <summary>Édition de la liste des applications et sites interdits.</summary>
internal sealed class RestrictionsDialog : Window
{
    public RestrictionsPayload? Result { get; private set; }

    private static readonly string[] SitePreset = { "youtube", "facebook", "instagram", "tiktok", "netflix", "twitch", "snapchat" };
    private static readonly string[] GamePreset = { "steam", "epicgameslauncher", "discord", "minecraft", "robloxplayerbeta" };

    public RestrictionsDialog(RestrictionsPayload current, int targetCount)
    {
        Title = "Restrictions — applications et sites";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var apps = MultiLine(current.BlockedApps, "un nom de processus par ligne, ex :\nchrome\ndiscord");
        var sites = MultiLine(current.BlockedSites, "un mot-clé par ligne, ex :\nyoutube\nfacebook");

        var addSites = new Button { Content = "+ Réseaux sociaux / vidéo", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
        addSites.Click += (_, _) => Append(sites, SitePreset);
        var addGames = new Button { Content = "+ Jeux / messageries", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
        addGames.Click += (_, _) => Append(apps, GamePreset);

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var left = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Applications interdites", FontWeight = FontWeight.SemiBold },
                Ui.Hint("Fermées automatiquement dès leur ouverture."),
                new Border { Height = 6 }, apps, new Border { Height = 6 }, addGames,
            },
        };
        var right = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Sites interdits", FontWeight = FontWeight.SemiBold },
                Ui.Hint("L'onglet est fermé si son titre contient le mot-clé."),
                new Border { Height = 6 }, sites, new Border { Height = 6 }, addSites,
            },
        };
        Grid.SetColumn(right, 2);
        cols.Children.Add(left);
        cols.Children.Add(right);

        RestrictionsPayload Build(bool enabled) => new()
        {
            Enabled = enabled,
            BlockedApps = Lines(apps),
            BlockedSites = Lines(sites),
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = $"S'applique à {targetCount} poste(s), et aux postes qui se connectent ensuite.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
                },
                cols,
                Ui.Hint("Le blocage des sites repose sur le titre de l'onglet actif (Chrome, Edge, Firefox…). " +
                        "Il ne remplace pas un filtrage réseau mais suffit pour encadrer une séance."),
                Ui.ButtonRow(
                    Ui.Btn("Annuler", Close),
                    Ui.Btn("🔓 Tout lever", () => { Result = Build(false); Close(); }),
                    Ui.Btn("⛔ Appliquer", () => { Result = Build(true); Close(); })),
            },
        };
    }

    private static TextBox MultiLine(IEnumerable<string> items, string watermark) => new()
    {
        AcceptsReturn = true,
        Height = 160,
        TextWrapping = TextWrapping.NoWrap,
        Watermark = watermark,
        Text = string.Join(Environment.NewLine, items),
    };

    private static List<string> Lines(TextBox box) =>
        (box.Text ?? "").Split('\n', ',', ';').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void Append(TextBox box, IEnumerable<string> extra)
    {
        var all = Lines(box).Concat(extra).Distinct(StringComparer.OrdinalIgnoreCase);
        box.Text = string.Join(Environment.NewLine, all);
    }
}

/// <summary>Journal des actions de la séance.</summary>
internal sealed class JournalWindow : Window
{
    public JournalWindow()
    {
        Title = "Journal des actions";
        Width = 760;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var list = new ListBox
        {
            ItemsSource = ActionLog.Entries,
            ItemTemplate = new FuncDataTemplate<LogEntry>((e, _) =>
                new TextBlock { Text = e?.Display ?? "", FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12 }),
        };

        var bottom = new DockPanel { Margin = new Thickness(12) };
        var info = Ui.Hint("Enregistré automatiquement dans : " + ActionLog.TodayFile);
        info.VerticalAlignment = VerticalAlignment.Center;
        var open = Ui.Btn("📂 Ouvrir le dossier", () => Ui.OpenFolder(ActionLog.Folder));
        DockPanel.SetDock(open, Dock.Right);
        bottom.Children.Add(open);
        bottom.Children.Add(info);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(list);
        Content = root;
    }
}

/// <summary>Liste de présence de la séance, exportable en CSV.</summary>
internal sealed class AttendanceWindow : Window
{
    private static readonly string[] Headers = { "Élève", "Poste", "Utilisateur", "Adresse IP", "Arrivée", "Départ", "Statut", "✋" };
    private readonly Grid _table = new() { Margin = new Thickness(16, 8, 16, 8) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(16, 14, 16, 0), FontWeight = FontWeight.SemiBold };
    private readonly Func<(string teacher, string room)> _context;
    private readonly DispatcherTimer _timer;

    public AttendanceWindow(Func<(string teacher, string room)> context)
    {
        _context = context;
        Title = "Liste de présence";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var export = Ui.Btn("💾 Exporter (CSV / Excel)…", async () => await ExportAsync());
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(12),
            Children = { export },
        };

        var root = new DockPanel();
        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(_summary);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = _table });
        Content = root;

        Refresh();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var rows = Attendance.Rows.OrderBy(r => r.Machine, StringComparer.OrdinalIgnoreCase).ToList();
        _summary.Text = $"Séance commencée à {Attendance.SessionStart:HH:mm} — " +
                        $"{rows.Count(r => r.Present)} présent(s) / {rows.Count} poste(s) vu(s)";

        _table.Children.Clear();
        _table.RowDefinitions.Clear();
        _table.ColumnDefinitions = new ColumnDefinitions("2.2*,1.6*,1.5*,1.4*,*,*,1.3*,0.5*");

        _table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < Headers.Length; c++)
            Cell(Headers[c], 0, c, bold: true);

        int i = 1;
        foreach (var r in rows)
        {
            _table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(r.StudentName.Length > 0 ? r.StudentName : "—", i, 0);
            Cell(r.Machine, i, 1);
            Cell(r.User, i, 2);
            Cell(r.Ip, i, 3);
            Cell(r.FirstText, i, 4);
            Cell(r.Present ? "—" : r.LeftText, i, 5);
            Cell(r.StatusText, i, 6);
            Cell(r.Hands > 0 ? r.Hands.ToString() : "", i, 7);
            i++;
        }
        if (rows.Count == 0)
        {
            _table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var empty = Ui.Hint("Aucun poste pour l'instant : lancez « Rechercher les postes ».");
            Grid.SetRow(empty, 1);
            Grid.SetColumnSpan(empty, Headers.Length);
            empty.Margin = new Thickness(0, 12, 0, 0);
            _table.Children.Add(empty);
        }
    }

    private void Cell(string text, int row, int col, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = bold ? Ui.Muted : Brushes.White,
            Margin = new Thickness(0, 5, 8, 5),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        _table.Children.Add(tb);
    }

    private async Task ExportAsync()
    {
        var (teacher, room) = _context();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter la liste de présence",
            SuggestedFileName = $"presence-{Attendance.SessionStart:yyyy-MM-dd-HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV (Excel)") { Patterns = new[] { "*.csv" } } },
        });
        if (file == null) return;
        try
        {
            await using var s = await file.OpenWriteAsync();
            // BOM UTF-8 : Excel affiche correctement les accents.
            await using var w = new StreamWriter(s, new System.Text.UTF8Encoding(true));
            await w.WriteAsync(Attendance.ExportCsv(teacher, room));
            ActionLog.Add("Export présence", file.Name);
        }
        catch (Exception ex)
        {
            _summary.Text = "Export impossible : " + ex.Message;
        }
    }
}
