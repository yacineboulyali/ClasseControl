using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClasseControle.Console;

/// <summary>
/// Constitution des équipes (aléatoire selon un nombre d'élèves par équipe, ou
/// manuelle poste par poste), ouverture des espaces d'équipe et suivi en direct
/// du document et de la discussion de chaque équipe.
/// </summary>
internal sealed class GroupsWindow : Window
{
    private readonly GroupManager _groups;
    private readonly Func<IReadOnlyList<AgentConnection>> _students;
    private readonly ConsoleSettings _settings;
    private readonly NumericUpDown _size;
    private readonly StackPanel _assignList = new() { Spacing = 4 };
    private readonly StackPanel _monitor = new() { Spacing = 10 };
    private readonly TextBlock _info = new() { Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _openBtn;
    private readonly Button _closeBtn;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, (TextBox doc, TextBlock chat)> _views = new();
    private bool _dirty = true;

    public GroupsWindow(GroupManager groups, Func<IReadOnlyList<AgentConnection>> students, ConsoleSettings settings)
    {
        _groups = groups;
        _students = students;
        _settings = settings;
        Title = "Équipes — travail de groupe";
        Width = 1100;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _size = new NumericUpDown { Minimum = 2, Maximum = 10, Value = Math.Clamp(settings.TeamSize, 2, 10), Width = 120, FormatString = "0" };
        var shuffle = Ui.Btn("🎲 Répartir aléatoirement", () =>
        {
            int size = (int)(_size.Value ?? 3);
            _settings.TeamSize = size;
            _settings.Save();
            _groups.Shuffle(_students(), size);
            RebuildAssign();
        });
        var clear = Ui.Btn("Effacer", () => { _groups.Draft.Clear(); RebuildAssign(); });

        _openBtn = Ui.Btn("🚀 Ouvrir les espaces d'équipe", async () =>
        {
            await _groups.OpenAsync();
            ActionLog.Add("Espaces d'équipe ouverts", $"{_groups.Groups.Count} équipe(s)",
                string.Join(" | ", _groups.Groups.Select(g => $"{g.Name} : {MemberNames(g)}")));
            _dirty = true;
        });
        _openBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        _openBtn.Foreground = Brushes.White;
        _closeBtn = Ui.Btn("⏹ Fermer les espaces", async () =>
        {
            await _groups.CloseAsync();
            ActionLog.Add("Espaces d'équipe fermés", $"{_groups.Groups.Count} équipe(s)");
        });
        var export = Ui.Btn("💾 Exporter les travaux", () =>
        {
            try
            {
                string dir = _groups.Export();
                ActionLog.Add("Export des travaux d'équipe", dir);
                Ui.OpenFolder(dir);
            }
            catch (Exception ex) { _info.Text = "Export impossible : " + ex.Message; }
        });

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 14, 16, 0),
            Children =
            {
                new TextBlock { Text = "Élèves par équipe :", VerticalAlignment = VerticalAlignment.Center }, _size, shuffle, clear,
            },
        };

        var template = new TextBox
        {
            Text = _groups.Template.Length > 0 ? _groups.Template : "1) Introduction : \n2) Matériel : \n3) Hypothèse : \n4) Conclusion : ",
            AcceptsReturn = true, Height = 92, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        };
        _groups.Template = template.Text ?? "";
        template.TextChanged += (_, _) => _groups.Template = template.Text ?? "";
        var templateBox = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                new TextBlock { Text = "Modèle du document d'équipe (consigne, plan à compléter)", FontWeight = FontWeight.SemiBold },
                Ui.Hint("Copié dans le document de chaque nouvelle équipe à l'ouverture."),
                new Border { Height = 4 },
                template,
            },
        };

        var left = new DockPanel { Margin = new Thickness(16, 10, 8, 0) };
        var leftTitle = new TextBlock { Text = "Répartition (modifiable à la main)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(leftTitle, Dock.Top);
        DockPanel.SetDock(_info, Dock.Bottom);
        DockPanel.SetDock(templateBox, Dock.Bottom);
        left.Children.Add(leftTitle);
        left.Children.Add(_info);
        left.Children.Add(templateBox);
        left.Children.Add(new ScrollViewer { Content = _assignList });

        var right = new DockPanel { Margin = new Thickness(8, 10, 16, 0) };
        var rightTitle = new TextBlock { Text = "Suivi des équipes en direct", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(rightTitle, Dock.Top);
        right.Children.Add(rightTitle);
        right.Children.Add(new ScrollViewer { Content = _monitor });

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("380,*") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var bottom = Ui.ButtonRow(export, _closeBtn, _openBtn);
        bottom.Margin = new Thickness(16, 10, 16, 14);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(body);
        Content = root;

        _groups.Changed += OnChanged;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => RefreshMonitor());
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _groups.Changed -= OnChanged; };
        RebuildAssign();
    }

    private void OnChanged() => _dirty = true;

    private string MemberNames(TeamGroup g) =>
        string.Join(", ", g.MemberKeys.Select(k => _students().FirstOrDefault(s => s.Key == k)?.DisplayName ?? k));

    private void RebuildAssign()
    {
        var students = _students().OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        int maxTeam = Math.Max(_groups.Draft.Values.DefaultIfEmpty(0).Max(),
                               (int)Math.Ceiling(students.Count / (double)Math.Max(2, (int)(_size.Value ?? 3))));
        var choices = new List<string> { "—" };
        for (int i = 1; i <= maxTeam + 1; i++) choices.Add($"Équipe {i}");

        _assignList.Children.Clear();
        foreach (var s in students)
        {
            var combo = new ComboBox { ItemsSource = choices, Width = 130, SelectedIndex = Math.Min(_groups.Draft.GetValueOrDefault(s.Key), choices.Count - 1) };
            string key = s.Key;
            combo.SelectionChanged += (_, _) =>
            {
                _groups.Draft[key] = Math.Max(0, combo.SelectedIndex);
                UpdateInfo();
            };
            var row = new DockPanel();
            DockPanel.SetDock(combo, Dock.Right);
            row.Children.Add(combo);
            row.Children.Add(new TextBlock
            {
                Text = $"{s.DisplayName}  ·  {s.Info.MachineName}", VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _assignList.Children.Add(row);
        }
        if (students.Count == 0)
            _assignList.Children.Add(Ui.Hint("Aucun poste connecté."));
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        var counts = _groups.Draft.Where(kv => kv.Value > 0).GroupBy(kv => kv.Value).OrderBy(g => g.Key)
            .Select(g => $"É{g.Key} : {g.Count()}").ToList();
        int unassigned = _students().Count(s => _groups.Draft.GetValueOrDefault(s.Key) == 0);
        _info.Text = (counts.Count == 0 ? "Aucune équipe définie. " : string.Join("   ", counts) + ". ")
                     + (unassigned > 0 ? $"{unassigned} élève(s) sans équipe." : "")
                     + (_groups.IsOpen ? "\nLes espaces sont ouverts : « Ouvrir » applique les changements." : "");
    }

    private void RefreshMonitor()
    {
        _closeBtn.IsEnabled = _groups.IsOpen;
        if (!_dirty) return;
        _dirty = false;

        var groups = _groups.Groups;
        if (!_groups.IsOpen || groups.Count == 0)
        {
            _views.Clear();
            _monitor.Children.Clear();
            _monitor.Children.Add(Ui.Hint("Les espaces d'équipe ne sont pas ouverts. Répartissez les élèves puis cliquez sur « Ouvrir les espaces d'équipe »."));
            return;
        }

        // (Re)construit les cartes si la liste des équipes a changé, sinon met juste à jour le contenu.
        if (!groups.Select(g => g.Id).SequenceEqual(_views.Keys))
        {
            _views.Clear();
            _monitor.Children.Clear();
            foreach (var g in groups) _monitor.Children.Add(BuildCard(g));
        }
        foreach (var g in groups)
        {
            var (doc, chat) = _views[g.Id];
            if (doc.Text != g.Doc.Text) doc.Text = g.Doc.Text;
            lock (g.Chat)
                chat.Text = g.Chat.Count == 0 ? "(pas encore de message)"
                    : string.Join("\n", g.Chat.TakeLast(4).Select(c => $"[{c.Time.ToLocalTime():HH:mm}] {c.Author} : {c.Text}"));
        }
    }

    private Control BuildCard(TeamGroup g)
    {
        var doc = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130, FontSize = 12 };
        var chat = new TextBlock { FontSize = 12, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        var say = new TextBox { Watermark = "Écrire à l'équipe…", FontSize = 12 };
        var sayBtn = new Button { Content = "Envoyer", FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        async void Say()
        {
            var t = (say.Text ?? "").Trim();
            if (t.Length == 0) return;
            say.Text = "";
            await _groups.TeacherChatAsync(g, _settings.TeacherName, t);
        }
        sayBtn.Click += (_, _) => Say();
        say.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Say(); };
        var sayRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(sayBtn, Dock.Right);
        sayRow.Children.Add(sayBtn);
        sayRow.Children.Add(say);

        _views[g.Id] = (doc, chat);
        return new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = $"👥 {g.Name}", FontWeight = FontWeight.Bold },
                    new TextBlock { Text = MemberNames(g), Foreground = Ui.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                    doc, chat, sayRow,
                },
            },
        };
    }
}
