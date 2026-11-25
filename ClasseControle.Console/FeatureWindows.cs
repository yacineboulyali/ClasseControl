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
    public static readonly IBrush Muted = Palette.Muted;
    public static readonly IBrush Panel = new SolidColorBrush(Color.FromRgb(246, 248, 250));
    public static readonly IBrush Line = Palette.Line;

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

/// <summary>
/// Restrictions d'un poste, d'une sélection ou de toute la classe : catégories prêtes
/// (jeux préinstallés de Windows, plateformes de jeux, messageries, réseaux sociaux, vidéo,
/// jeux en ligne), listes libres, et mode d'accès au web.
/// </summary>
internal sealed class RestrictionsDialog : Window
{
    public RestrictionsPayload? Result { get; private set; }

    private readonly Dictionary<string, CheckBox> _appCats = new();
    private readonly Dictionary<string, CheckBox> _siteCats = new();
    private readonly TextBox _apps, _sites, _allowed;
    private readonly ComboBox _web;

    public RestrictionsDialog(RestrictionsPayload current, string target)
    {
        Title = "Restrictions — " + target;
        Width = 940;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var tokens = new HashSet<string>(current.BlockedApps.Concat(current.BlockedSites), StringComparer.OrdinalIgnoreCase);
        _apps = MultiLine(current.BlockedApps.Where(a => !a.StartsWith('@')), "ex :\nchrome\nvlc", 90);
        _sites = MultiLine(current.BlockedSites.Where(a => !a.StartsWith('@')), "ex :\nnetflix\nspotify", 70);
        _allowed = MultiLine(current.AllowedSites, "ex :\nwikipedia\nmoodle\nofppt", 70);
        _web = new ComboBox
        {
            ItemsSource = new[] { "Sans restriction", "Sites interdits (catégories et liste ci-dessous)", "Sites approuvés uniquement", "Bloquer tout le web (navigateurs fermés)" },
            SelectedIndex = current.Enabled ? (int)current.Web : 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(Section("Applications interdites", "Fermées automatiquement dès leur ouverture, à chaque fois."));
        foreach (var c in RestrictionCatalog.AppCategories) left.Children.Add(CategoryBox(c, tokens.Contains(c.Token), _appCats));
        left.Children.Add(new TextBlock { Text = "Autres applications (nom du processus, une par ligne) :", Margin = new Thickness(0, 6, 0, 0) });
        left.Children.Add(_apps);
        left.Children.Add(Ui.Hint("Le nom du processus se voit dans le Gestionnaire des tâches (onglet Détails), sans « .exe ». " +
                                  "Astuce : le panneau « Applications ouvertes » de la console les affiche aussi."));

        var right = new StackPanel { Spacing = 6 };
        right.Children.Add(Section("Accès au web", "Le blocage se fait sur le titre de l'onglet actif du navigateur."));
        right.Children.Add(_web);
        foreach (var c in RestrictionCatalog.SiteCategories) right.Children.Add(CategoryBox(c, tokens.Contains(c.Token), _siteCats));
        right.Children.Add(new TextBlock { Text = "Autres sites interdits (mot-clé, un par ligne) :", Margin = new Thickness(0, 6, 0, 0) });
        right.Children.Add(_sites);
        right.Children.Add(new TextBlock { Text = "Sites approuvés (mode « approuvés uniquement ») :" });
        right.Children.Add(_allowed);

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        cols.Children.Add(left);
        Grid.SetColumn(right, 2);
        cols.Children.Add(right);

        Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Children =
            {
                new TextBlock { Text = "S'applique à : " + target, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) },
                cols,
                Ui.ButtonRow(
                    Ui.Btn("Annuler", Close),
                    Ui.Btn("Tout lever", () => { Result = new RestrictionsPayload { Enabled = false, Web = WebMode.Unrestricted }; Close(); }),
                    Ui.Btn("⛔ Appliquer", () => { Result = Build(); Close(); })),
            },
        };
    }

    private RestrictionsPayload Build()
    {
        var apps = _appCats.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).Concat(Lines(_apps)).ToList();
        var sites = _siteCats.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).Concat(Lines(_sites)).ToList();
        var web = (WebMode)Math.Max(0, _web.SelectedIndex);
        var r = new RestrictionsPayload { BlockedApps = apps, BlockedSites = sites, AllowedSites = Lines(_allowed), Web = web };
        r.Enabled = apps.Count > 0 || web != WebMode.Unrestricted;
        return r;
    }

    private static Control Section(string title, string hint) => new StackPanel
    {
        Children = { new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 15 }, Ui.Hint(hint) },
    };

    private static Control CategoryBox(RestrictionCatalog.Category c, bool on, Dictionary<string, CheckBox> into)
    {
        var box = new CheckBox
        {
            IsChecked = on,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = c.Label, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = c.Description, FontSize = 11.5, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
        into[c.Token] = box;
        return box;
    }

    private static TextBox MultiLine(IEnumerable<string> items, string watermark, double height) => new()
    {
        AcceptsReturn = true,
        Height = height,
        TextWrapping = TextWrapping.NoWrap,
        Watermark = watermark,
        Text = string.Join(Environment.NewLine, items),
    };

    private static List<string> Lines(TextBox box) =>
        (box.Text ?? "").Split('\n', ',', ';').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            Foreground = bold ? Ui.Muted : Palette.IconFg,
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
