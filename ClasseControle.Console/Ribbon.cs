using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClasseControle.Console;

/// <summary>
/// Icônes : police « Segoe Fluent Icons » (Windows 11) ou « Segoe MDL2 Assets »
/// (Windows 10), présentes sur tous les postes Windows — aucune image à embarquer.
/// </summary>
internal static class Glyph
{
    public static readonly FontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Codes vérifiés dans la liste officielle « Segoe MDL2 Assets » (nom Microsoft en commentaire).
    public const string
        Home = "",        // Home
        Search = "",      // Search
        Education = "",   // Education
        List = "",        // BulletedList
        View = "",        // View
        Folder = "",      // Folder
        Lock = "",        // Lock
        Unlock = "",      // Unlock
        Blank = "",       // QuietHours (lune)
        Monitor = "",     // TVMonitor
        Projector = "",   // Projector
        Exhibit = "",     // TVMonitorSelected
        Launch = "",      // Send (avion en papier)
        Desktop = "",     // TaskView
        Globe = "",       // Globe
        Chat = "",        // ChatBubbles
        Mic = "",         // Megaphone
        Message = "",     // Message
        Help = "",        // Help
        Hand = "",        // Help (demande d'aide)
        Refresh = "",     // Refresh
        ZoomIn = "",      // ZoomIn
        ZoomOut = "",     // ZoomOut
        People = "",      // People
        Shuffle = "",     // Shuffle
        Play = "",        // Play
        Stop = "",        // Stop
        Save = "",        // Save
        SelectAll = "",   // SelectAll
        ClearSel = "",    // ClearSelection
        Switch = "",      // Switch
        Contact = "",     // ContactInfo
        Survey = "",      // AreaChart
        Star = "",        // FavoriteStar
        StarFill = "",    // FavoriteStarFill
        History = "",     // History
        Power = "",       // PowerButton
        SignOut = "",     // SwitchUser
        Blocked = "",     // Blocked2
        Cancel = "",      // Cancel
        Settings = "",    // Setting
        Delete = "",      // Delete
        Admin = "",       // Admin
        Sort = "";        // Sort

    public static TextBlock Make(string glyph, double size, IBrush? fg = null) => new()
    {
        Text = glyph,
        FontFamily = Font,
        FontSize = size,
        Foreground = fg ?? Palette.IconFg,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>Couleurs de la console (thème clair).</summary>
internal static class Palette
{
    public static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(31, 78, 140));
    public static readonly IBrush AccentLight = new SolidColorBrush(Color.FromRgb(225, 237, 250));
    public static readonly IBrush Navy = new SolidColorBrush(Color.FromRgb(21, 41, 77));
    public static readonly IBrush IconFg = new SolidColorBrush(Color.FromRgb(36, 41, 47));
    public static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(87, 96, 106));
    public static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(208, 215, 222));
    public static readonly IBrush RibbonBg = Brushes.White;
    public static readonly IBrush TabsBg = new SolidColorBrush(Color.FromRgb(243, 246, 250));
    public static readonly IBrush Hand = new SolidColorBrush(Color.FromRgb(230, 140, 20));
    public static readonly IBrush Danger = new SolidColorBrush(Color.FromRgb(200, 35, 35));
    public static readonly IBrush Ok = new SolidColorBrush(Color.FromRgb(26, 127, 55));
    public static readonly IBrush Hover = new SolidColorBrush(Color.FromRgb(232, 240, 250));

    public static readonly IBrush Workspace = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(240, 247, 253), 0),
            new GradientStop(Color.FromRgb(214, 232, 247), 1),
        },
    };
}

/// <summary>Construction d'un ruban (onglets + groupes de gros boutons icône/libellé).</summary>
internal static class Ribbon
{
    /// <summary>Gros bouton du ruban : icône au-dessus, libellé dessous (2 lignes max).</summary>
    public static Button Big(string glyph, string label, Action? onClick = null, FlyoutBase? menu = null)
    {
        var text = new TextBlock
        {
            Text = label + (menu != null ? " ▾" : ""),
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 74,
            Foreground = Palette.IconFg,
        };
        var b = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            MinWidth = 58,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel
            {
                Spacing = 3,
                Children = { Glyph.Make(glyph, 22), text },
            },
        };
        if (menu != null) b.Flyout = menu;
        if (onClick != null) b.Click += (_, _) => onClick();
        ToolTip.SetTip(b, label);
        return b;
    }

    /// <summary>Petit bouton (icône + texte sur une ligne), pour les colonnes de 2-3 commandes.</summary>
    public static Button Small(string glyph, string label, Action onClick)
    {
        var b = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Glyph.Make(glyph, 13), new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Groupe du ruban : commandes en ligne et légende centrée en bas.</summary>
    public static Control Group(string caption, params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var i in items) row.Children.Add(i);
        var g = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(4, 4, 4, 2) };
        g.Children.Add(row);
        var cap = new TextBlock
        {
            Text = caption, FontSize = 11, Foreground = Palette.Muted,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(cap, 1);
        g.Children.Add(cap);
        return new Border
        {
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(2, 0, 4, 0),
            Child = g,
        };
    }

    public static StackPanel Column(params Control[] items)
    {
        var c = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        foreach (var i in items) c.Children.Add(i);
        return c;
    }

    public static MenuFlyout Menu(params (string label, Action action)[] items)
    {
        var f = new MenuFlyout();
        foreach (var (label, action) in items)
        {
            if (label == "-") { f.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => action();
            f.Items.Add(mi);
        }
        return f;
    }

    /// <summary>Contenu d'un onglet : groupes alignés horizontalement, défilables si la fenêtre est étroite.</summary>
    public static Control TabContent(params Control[] groups)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 92 };
        foreach (var g in groups) row.Children.Add(g);
        return new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }
}
