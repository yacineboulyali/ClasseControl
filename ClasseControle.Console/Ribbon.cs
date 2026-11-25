using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClasseControle.Console;

/// <summary>
/// Jeu d'icônes vectorielles de la console, dessinées pour l'application :
/// style « flat » au trait, monochrome, grille 24×24 (trait 1,7 px, extrémités
/// arrondies). Identiques sur tous les postes, sans police ni image à installer.
/// Chaque icône est un tracé SVG ; « tracé|remplissage » ajoute une partie pleine.
/// Les couleurs viennent des styles (survol, actif…) sauf si une couleur est imposée.
/// </summary>
internal static class Glyph
{
    private const string Circle9 = "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12Z";
    private const string Screen = "M3.5 4.5H20.5A1.5 1.5 0 0 1 22 6V15.5A1.5 1.5 0 0 1 20.5 17H3.5A1.5 1.5 0 0 1 2 15.5V6A1.5 1.5 0 0 1 3.5 4.5ZM8 20.5H16M12 17V20.5";
    private const string Lens = "M17 10.5A6.5 6.5 0 1 1 4 10.5A6.5 6.5 0 1 1 17 10.5ZM15.3 15.3L20.5 20.5";

    public const string
        Home = "M3 11L12 3.5L21 11M5.5 9.5V19.5A0.5 0.5 0 0 0 6 20H10V14.5H14V20H18A0.5 0.5 0 0 0 18.5 19.5V9.5",
        Grid = "M4.5 4H9.5A0.5 0.5 0 0 1 10 4.5V9.5A0.5 0.5 0 0 1 9.5 10H4.5A0.5 0.5 0 0 1 4 9.5V4.5A0.5 0.5 0 0 1 4.5 4ZM14.5 4H19.5A0.5 0.5 0 0 1 20 4.5V9.5A0.5 0.5 0 0 1 19.5 10H14.5A0.5 0.5 0 0 1 14 9.5V4.5A0.5 0.5 0 0 1 14.5 4ZM4.5 14H9.5A0.5 0.5 0 0 1 10 14.5V19.5A0.5 0.5 0 0 1 9.5 20H4.5A0.5 0.5 0 0 1 4 19.5V14.5A0.5 0.5 0 0 1 4.5 14ZM14.5 14H19.5A0.5 0.5 0 0 1 20 14.5V19.5A0.5 0.5 0 0 1 19.5 20H14.5A0.5 0.5 0 0 1 14 19.5V14.5A0.5 0.5 0 0 1 14.5 14Z",
        Search = Lens,
        Education = "M2 9L12 4.5L22 9L12 13.5ZM6 11V15.8C6 17.6 8.7 19 12 19C15.3 19 18 17.6 18 15.8V11M22 9V14.5",
        List = "M9 4.5H6.5A1.5 1.5 0 0 0 5 6V19.5A1.5 1.5 0 0 0 6.5 21H17.5A1.5 1.5 0 0 0 19 19.5V6A1.5 1.5 0 0 0 17.5 4.5H15M9.5 3H14.5A0.5 0.5 0 0 1 15 3.5V5.5A0.5 0.5 0 0 1 14.5 6H9.5A0.5 0.5 0 0 1 9 5.5V3.5A0.5 0.5 0 0 1 9.5 3ZM8.5 11L9.5 12L11.5 10M13.5 11H16M8.5 16L9.5 17L11.5 15M13.5 16H16",
        View = "M2 12C4.5 7.2 8 5 12 5C16 5 19.5 7.2 22 12C19.5 16.8 16 19 12 19C8 19 4.5 16.8 2 12ZM15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12Z",
        Folder = "M3 7A1.5 1.5 0 0 1 4.5 5.5H9.5L11.5 7.5H19.5A1.5 1.5 0 0 1 21 9V18A1.5 1.5 0 0 1 19.5 19.5H4.5A1.5 1.5 0 0 1 3 18ZM12 17V11.5M9.5 14L12 11.5L14.5 14",
        Lock = "M6.5 11H17.5A1.5 1.5 0 0 1 19 12.5V19.5A1.5 1.5 0 0 1 17.5 21H6.5A1.5 1.5 0 0 1 5 19.5V12.5A1.5 1.5 0 0 1 6.5 11ZM8 11V7.5A4 4 0 0 1 16 7.5V11M12 15V17",
        Unlock = "M6.5 11H17.5A1.5 1.5 0 0 1 19 12.5V19.5A1.5 1.5 0 0 1 17.5 21H6.5A1.5 1.5 0 0 1 5 19.5V12.5A1.5 1.5 0 0 1 6.5 11ZM8 11V7.5A4 4 0 0 1 15.6 5.8M12 15V17",
        Blank = Screen + "|M4.5 7H19.5V14.5H4.5Z",
        Monitor = Screen,
        Projector = Screen + "|M10 7.8L15 10.75L10 13.7Z",
        Exhibit = Screen + "M13.8 9A1.8 1.8 0 1 1 10.2 9A1.8 1.8 0 1 1 13.8 9ZM8.8 14.5C9.3 12.8 10.5 12 12 12C13.5 12 14.7 12.8 15.2 14.5",
        Launch = "M21 3L3 10.5L10 13L12.5 20L21 3ZM10 13L21 3",
        Desktop = "M8 3.5H20A1 1 0 0 1 21 4.5V13A1 1 0 0 1 20 14H8A1 1 0 0 1 7 13V4.5A1 1 0 0 1 8 3.5ZM7 7H21M4 8V19A1 1 0 0 0 5 20H17",
        Globe = Circle9 + "M3 12H21M12 3C14.4 5.5 15.5 8.5 15.5 12C15.5 15.5 14.4 18.5 12 21C9.6 18.5 8.5 15.5 8.5 12C8.5 8.5 9.6 5.5 12 3Z",
        Chat = "M4.5 4.5H14.5A1.5 1.5 0 0 1 16 6V11.5A1.5 1.5 0 0 1 14.5 13H9L5.5 16V13H4.5A1.5 1.5 0 0 1 3 11.5V6A1.5 1.5 0 0 1 4.5 4.5ZM16 8.5H19.5A1.5 1.5 0 0 1 21 10V15.5A1.5 1.5 0 0 1 19.5 17H18.5V20L15 17H11.5A1.5 1.5 0 0 1 10 15.5V13",
        Mic = "M3 10V14A1 1 0 0 0 4 15H7L16 19.5V4.5L7 9H4A1 1 0 0 0 3 10ZM7 15L8.5 20.5H11L9.8 15.6M19 9.2C20 9.9 20.6 10.9 20.6 12C20.6 13.1 20 14.1 19 14.8",
        Message = "M4.5 4H19.5A1.5 1.5 0 0 1 21 5.5V15.5A1.5 1.5 0 0 1 19.5 17H10L5.5 20.5V17H4.5A1.5 1.5 0 0 1 3 15.5V5.5A1.5 1.5 0 0 1 4.5 4ZM7.5 9H16.5M7.5 12.5H13.5",
        Help = Circle9 + "M9.5 9.5C9.5 8 10.6 7 12 7C13.4 7 14.5 8 14.5 9.3C14.5 11.4 12 11.4 12 13.6|M13 16.9A1 1 0 1 1 11 16.9A1 1 0 1 1 13 16.9Z",
        Hand = "M8 13.5V6.8A1.3 1.3 0 0 1 10.6 6.8V11.5M10.6 11V4.8A1.3 1.3 0 0 1 13.2 4.8V11M13.2 11V5.8A1.3 1.3 0 0 1 15.8 5.8V11.5M15.8 10A1.3 1.3 0 0 1 18.4 10V14.5C18.4 18.2 15.8 20.8 12.3 20.8C9.8 20.8 8.2 19.8 6.7 17.8L4.4 14.4A1.3 1.3 0 0 1 6.5 12.8L8 14.6",
        Refresh = "M19.5 10.5A7.8 7.8 0 0 0 5.4 7.2M4.5 3.8V7.8H8.5M4.5 13.5A7.8 7.8 0 0 0 18.6 16.8M19.5 20.2V16.2H15.5",
        ZoomIn = Lens + "M10.5 7.5V13.5M7.5 10.5H13.5",
        ZoomOut = Lens + "M7.5 10.5H13.5",
        Fit = "M4 9V4.5A0.5 0.5 0 0 1 4.5 4H9M15 4H19.5A0.5 0.5 0 0 1 20 4.5V9M20 15V19.5A0.5 0.5 0 0 1 19.5 20H15M9 20H4.5A0.5 0.5 0 0 1 4 19.5V15",
        Plus = "M12 5V19M5 12H19",
        Minus = "M5 12H19",
        People = "M15.5 7.5A3.5 3.5 0 1 1 8.5 7.5A3.5 3.5 0 1 1 15.5 7.5ZM5.5 20C5.5 16.6 8.4 14 12 14C15.6 14 18.5 16.6 18.5 20M17.6 4.6A2.6 2.6 0 0 1 18.2 9.9M19.5 14.2C21 14.9 22 16.5 22 18.4M6.4 4.6A2.6 2.6 0 0 0 5.8 9.9M4.5 14.2C3 14.9 2 16.5 2 18.4",
        Shuffle = "M3 7H6.5C10.5 7 12.5 17 16.5 17H21M18 14L21 17L18 20M3 17H6.5C8 17 9.1 15.8 10 14.3M13.9 9.7C14.8 8.2 15.4 7 17 7H21M18 4L21 7L18 10",
        Play = "M8 5.2V18.8A0.6 0.6 0 0 0 8.9 19.3L19.2 12.5A0.6 0.6 0 0 0 19.2 11.5L8.9 4.7A0.6 0.6 0 0 0 8 5.2Z",
        Stop = "M7 6H17A1 1 0 0 1 18 7V17A1 1 0 0 1 17 18H7A1 1 0 0 1 6 17V7A1 1 0 0 1 7 6Z",
        Save = "M12 4V15M7.5 10.5L12 15L16.5 10.5M4 16.5V19A1 1 0 0 0 5 20H19A1 1 0 0 0 20 19V16.5",
        SelectAll = "M5 4H19A1 1 0 0 1 20 5V19A1 1 0 0 1 19 20H5A1 1 0 0 1 4 19V5A1 1 0 0 1 5 4ZM8 12L11 15L16.5 9",
        ClearSel = "M5 4H19A1 1 0 0 1 20 5V19A1 1 0 0 1 19 20H5A1 1 0 0 1 4 19V5A1 1 0 0 1 5 4ZM9 9L15 15M15 9L9 15",
        Switch = "M7 4L3 8L7 12M3 8H17M17 12L21 16L17 20M21 16H7",
        Contact = "M4 5H20A1 1 0 0 1 21 6V18A1 1 0 0 1 20 19H4A1 1 0 0 1 3 18V6A1 1 0 0 1 4 5ZM11 10A2 2 0 1 1 7 10A2 2 0 1 1 11 10ZM5.5 16.2C6 14.8 7.3 14 9 14C10.7 14 12 14.8 12.5 16.2M15 9.5H18.5M15 13H18.5",
        Survey = "M6 12H8.5A0.5 0.5 0 0 1 9 12.5V20H5.5V12.5A0.5 0.5 0 0 1 6 12ZM11 6H13.5A0.5 0.5 0 0 1 14 6.5V20H10.5V6.5A0.5 0.5 0 0 1 11 6ZM16 14H18.5A0.5 0.5 0 0 1 19 14.5V20H15.5V14.5A0.5 0.5 0 0 1 16 14ZM3 20.5H21",
        Star = "M12 3.5L14.6 8.9L20.5 9.6L16.2 13.7L17.3 19.6L12 16.8L6.7 19.6L7.8 13.7L3.5 9.6L9.4 8.9Z",
        StarFill = "M12 3.5L14.6 8.9L20.5 9.6L16.2 13.7L17.3 19.6L12 16.8L6.7 19.6L7.8 13.7L3.5 9.6L9.4 8.9Z|M12 3.5L14.6 8.9L20.5 9.6L16.2 13.7L17.3 19.6L12 16.8L6.7 19.6L7.8 13.7L3.5 9.6L9.4 8.9Z",
        History = "M3.5 12A8.5 8.5 0 1 0 6 6L3.5 8.5M3.5 4.5V8.5H7.5M12 7.5V12L15 14",
        Power = "M12 3V11.5M7 6.3A8 8 0 1 0 17 6.3",
        SignOut = "M10 4H5.5A1.5 1.5 0 0 0 4 5.5V18.5A1.5 1.5 0 0 0 5.5 20H10M15 8L19 12L15 16M19 12H9",
        Blocked = Circle9 + "M5.6 5.6L18.4 18.4",
        Cancel = "M6 6L18 18M18 6L6 18",
        Settings = "M4 7H13M17 7H20M15 5V9M4 17H7M11 17H20M9 15V19M4 12H16M20 12H20.01",
        Delete = "M4 7H20M9 7V4.5H15V7M6 7L7 20H17L18 7M10 11V16M14 11V16",
        Admin = "M12 3L19.5 6V11.5C19.5 16 16.5 19.5 12 21C7.5 19.5 4.5 16 4.5 11.5V6ZM9 12L11 14L15 10",
        Sort = "M4 6H13M4 12H10M4 18H7M17 5V19M14 16L17 19L20 16",
        WebOff = "M19.4 15.6A9 9 0 0 1 8.4 20.3M5.2 18A9 9 0 0 1 15.6 3.6M3 12H8.5M15.5 12H21M12 3C13.4 4.4 14.4 6 15 7.8M9.2 15.6C9.6 17.6 10.6 19.4 12 21M8.5 12C8.5 10.6 8.7 9.3 9 8.1M3.5 3.5L20.5 20.5",
        SoundOff = "M3 9.5V14.5A1 1 0 0 0 4 15.5H7L12 19.5V4.5L7 8.5H4A1 1 0 0 0 3 9.5ZM16 9L21 14M21 9L16 14",
        Sound = "M3 9.5V14.5A1 1 0 0 0 4 15.5H7L12 19.5V4.5L7 8.5H4A1 1 0 0 0 3 9.5ZM15.5 9A4 4 0 0 1 15.5 15M18.5 6.5A7.5 7.5 0 0 1 18.5 17.5",
        Control = "M5 3.5L5 17L9 13.2L11.6 19.2L14 18.2L11.5 12.3H17Z",
        Apps = "M4.5 4H9.5A0.5 0.5 0 0 1 10 4.5V9.5A0.5 0.5 0 0 1 9.5 10H4.5A0.5 0.5 0 0 1 4 9.5V4.5A0.5 0.5 0 0 1 4.5 4ZM14 7H20M4.5 14H9.5A0.5 0.5 0 0 1 10 14.5V19.5A0.5 0.5 0 0 1 9.5 20H4.5A0.5 0.5 0 0 1 4 19.5V14.5A0.5 0.5 0 0 1 4.5 14ZM14 17H20M14 4.5H17M14 14.5H17",
        InputLock = "M3 7H15A1 1 0 0 1 16 8V14A1 1 0 0 1 15 15H3A1 1 0 0 1 2 14V8A1 1 0 0 1 3 7ZM5 10H5.01M8 10H8.01M11 10H11.01M6 12.5H12M17.5 15H22.5V20.5H17.5ZM18.5 15V13.5A1.5 1.5 0 0 1 21.5 13.5V15",
        InputUnlock = "M3 7H15A1 1 0 0 1 16 8V14A1 1 0 0 1 15 15H3A1 1 0 0 1 2 14V8A1 1 0 0 1 3 7ZM5 10H5.01M8 10H8.01M11 10H11.01M6 12.5H12M17.5 15H22.5V20.5H17.5ZM18.5 15V13.5A1.5 1.5 0 0 1 21.3 12.7",
        Pen = "M16.5 3.5L20.5 7.5L8 20H4V16ZM14 6L18 10",
        Highlighter = "M14.5 4L20 9.5L11.5 18H6.5V13ZM6.5 18L4.5 20.5H9M3 21.5H21",
        RectTool = "M5 6H19A1 1 0 0 1 20 7V17A1 1 0 0 1 19 18H5A1 1 0 0 1 4 17V7A1 1 0 0 1 5 6Z",
        EllipseTool = "M21 12A9 6.5 0 1 1 3 12A9 6.5 0 1 1 21 12Z",
        ArrowTool = "M5 19L19 5M10 5H19V14",
        Undo = "M9 14L4 9L9 4M4 9H14.5A5.5 5.5 0 0 1 14.5 20H11",
        WifiLock = "M2 8.5A14 14 0 0 1 19 6M5 12A9.5 9.5 0 0 1 14.5 10M8.5 15.5A4.5 4.5 0 0 1 11.5 14.5M10.5 19H10.51M16 16H22V21H16ZM17 16V14.5A2 2 0 0 1 21 14.5V16",
        Wifi = "M4.5 19.5H7V16H4.5ZM10.5 19.5H13V12H10.5ZM16.5 19.5H19V8H16.5Z";

    /// <summary>Icône vectorielle à la taille demandée. Sans couleur imposée, elle suit les styles (survol, actif).</summary>
    public static Control Make(string data, double size, IBrush? color = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var parts = data.Split('|');
        var stroke = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(parts[0]),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        stroke.Classes.Add("icon");
        if (color != null) stroke.Stroke = color;
        canvas.Children.Add(stroke);
        if (parts.Length > 1)
        {
            var fill = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(parts[1]) };
            fill.Classes.Add("iconfill");
            if (color != null) fill.Fill = color;
            canvas.Children.Add(fill);
        }
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
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
        };
        text.Classes.Add("rlabel");
        var b = new Button
        {
            Padding = new Thickness(6, 5, 6, 4),
            MinWidth = 58,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { Glyph.Make(glyph, 24), text },
            },
        };
        b.Classes.Add("ribbon");
        if (menu != null) b.Flyout = menu;
        if (onClick != null) b.Click += (_, _) => onClick();
        ToolTip.SetTip(b, label);
        return b;
    }

    /// <summary>Petit bouton (icône + texte sur une ligne), pour les colonnes de 2-3 commandes.</summary>
    public static Button Small(string glyph, string label, Action onClick)
    {
        var lbl = new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        lbl.Classes.Add("rlabel");
        var b = new Button
        {
            Padding = new Thickness(6, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Glyph.Make(glyph, 16), lbl },
            },
        };
        b.Classes.Add("ribbon");
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
