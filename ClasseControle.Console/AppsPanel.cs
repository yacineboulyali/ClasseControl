using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Moitié basse de l'écran : pour chaque poste, son nom (PC), l'élève, l'application
/// sur laquelle il travaille (icône + titre de la fenêtre) et ses autres applications
/// ouvertes. Clic droit sur une icône : fermer cette application sur le poste.
/// </summary>
internal sealed class AppsPanel : Border
{
    private readonly Func<IReadOnlyList<AgentConnection>> _connections;
    private readonly Func<AgentConnection, string, Task> _closeApp;
    private readonly Action<AgentConnection> _view;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _summary = new() { Foreground = Palette.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Bitmap?> _bitmaps = new();
    private readonly DispatcherTimer _refresh;
    private bool _dirty = true;

    public event Action? CloseRequested;

    public AppsPanel(Func<IReadOnlyList<AgentConnection>> connections, Func<AgentConnection, string, Task> closeApp, Action<AgentConnection> view)
    {
        _connections = connections;
        _closeApp = closeApp;
        _view = view;
        Background = Brushes.White;
        BorderBrush = Palette.Line;
        BorderThickness = new Thickness(0, 1, 0, 0);

        var close = new Button { Content = Glyph.Make(Glyph.Cancel, 14), Padding = new Thickness(6) };
        close.Classes.Add("zoom");
        ToolTip.SetTip(close, "Masquer le panneau des applications");
        close.Click += (_, _) => CloseRequested?.Invoke();

        var header = new DockPanel { Margin = new Thickness(14, 8, 8, 6) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                Glyph.Make(Glyph.Apps, 18, Palette.Accent),
                new TextBlock { Text = "Applications ouvertes sur les postes", FontWeight = FontWeight.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                _summary,
            },
        });

        var columns = new Grid { ColumnDefinitions = Columns(), Margin = new Thickness(14, 0, 14, 4) };
        AddHeader(columns, "Poste", 0);
        AddHeader(columns, "Travaille sur", 1);
        AddHeader(columns, "Autres applications ouvertes (clic droit : fermer)", 2);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(columns, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(columns);
        root.Children.Add(new ScrollViewer { Content = _rows, Padding = new Thickness(8, 0, 8, 8) });
        Child = root;

        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => { if (_dirty) Rebuild(); });
        _refresh.Start();
    }

    private static ColumnDefinitions Columns() => new("200,380,*");

    private static void AddHeader(Grid g, string text, int col)
    {
        var tb = new TextBlock { Text = text, FontSize = 11.5, Foreground = Palette.Muted, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    /// <summary>Une liste a changé : reconstruction groupée (au plus deux fois par seconde).</summary>
    public void Invalidate() => _dirty = true;

    private Bitmap? IconOf(AgentConnection c, string process)
    {
        string key = c.Key + "|" + process;
        if (_bitmaps.TryGetValue(key, out var b)) return b;
        Bitmap? bmp = null;
        if (c.IconFor(process) is { } png)
            try { using var ms = new MemoryStream(png); bmp = new Bitmap(ms); } catch { }
        if (bmp != null) _bitmaps[key] = bmp; // on ne mémorise pas l'absence : l'icône peut arriver plus tard
        return bmp;
    }

    private Control AppIcon(AgentConnection c, AppInfo a, double size)
    {
        Control content = IconOf(c, a.ProcessName) is { } bmp
            ? new Image { Source = bmp, Width = size, Height = size }
            : new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(5), Background = Palette.AccentLight,
                Child = new TextBlock
                {
                    Text = (a.Name.Length > 0 ? a.Name : a.ProcessName)[..1].ToUpperInvariant(),
                    Foreground = Palette.Accent, FontWeight = FontWeight.Bold, FontSize = size * 0.5,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
        var holder = new Border { Child = content, Padding = new Thickness(3), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent };
        ToolTip.SetTip(holder, $"{a.Name}\n{a.Title}");
        var menu = new MenuFlyout();
        var close = new MenuItem { Header = $"Fermer « {a.Name} » sur {c.Info.MachineName}" };
        close.Click += async (_, _) => await _closeApp(c, a.ProcessName);
        menu.Items.Add(close);
        holder.ContextFlyout = menu;
        holder.PointerEntered += (_, _) => holder.Background = Palette.Hover;
        holder.PointerExited += (_, _) => holder.Background = Brushes.Transparent;
        return holder;
    }

    private void Rebuild()
    {
        _dirty = false;
        _rows.Children.Clear();
        var conns = _connections().OrderBy(c => c.Info.MachineName, StringComparer.OrdinalIgnoreCase).ToList();
        int online = conns.Count(c => c.IsConnected);
        _summary.Text = $"{online} poste(s) connecté(s) — mise à jour toutes les 2 s";

        bool odd = false;
        foreach (var c in conns)
        {
            odd = !odd;
            var row = new Grid { ColumnDefinitions = Columns(), MinHeight = 44 };

            var who = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = c.Info.MachineName, FontWeight = FontWeight.Bold, FontSize = 13 },
                    new TextBlock { Text = c.DisplayName, Foreground = Palette.Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            };
            row.Children.Add(who);

            var active = c.Apps.FirstOrDefault(a => a.IsActive);
            Control activeCell;
            if (!c.IsConnected) activeCell = new TextBlock { Text = "Hors ligne", Foreground = Palette.Danger, VerticalAlignment = VerticalAlignment.Center };
            else if (active == null)
                activeCell = new TextBlock
                {
                    Text = c.Apps.Count == 0 ? "En attente de la liste…" : "Bureau (aucune fenêtre au premier plan)",
                    Foreground = Palette.Muted, FontStyle = FontStyle.Italic, VerticalAlignment = VerticalAlignment.Center,
                };
            else
                activeCell = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        AppIcon(c, active, 28),
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320,
                            Children =
                            {
                                new TextBlock { Text = active.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                                new TextBlock { Text = active.Title, FontSize = 11.5, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis },
                            },
                        },
                    },
                };
            Grid.SetColumn(activeCell, 1);
            row.Children.Add(activeCell);

            var others = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var a in c.Apps.Where(a => !a.IsActive)) others.Children.Add(AppIcon(c, a, 22));
            Grid.SetColumn(others, 2);
            row.Children.Add(others);

            var line = new Border
            {
                Child = row, Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(6),
                Background = odd ? new SolidColorBrush(Color.FromRgb(246, 248, 251)) : Brushes.Transparent,
            };
            ToolTip.SetTip(who, "Double-clic : voir l'écran de ce poste");
            var conn = c;
            line.DoubleTapped += (_, _) => _view(conn);
            _rows.Children.Add(line);
        }
        if (conns.Count == 0)
            _rows.Children.Add(new TextBlock { Text = "Aucun poste.", Foreground = Palette.Muted, Margin = new Thickness(8) });
    }

    /// <summary>Panneau masqué : libère les icônes (elles seront renvoyées à la prochaine ouverture).</summary>
    public void ClearCache()
    {
        foreach (var b in _bitmaps.Values) b?.Dispose();
        _bitmaps.Clear();
    }
}
