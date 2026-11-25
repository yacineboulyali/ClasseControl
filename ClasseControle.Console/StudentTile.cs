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
/// Vignette d'un poste élève : bandeau (nom, heure, étoiles, séance), miniature
/// de l'écran en direct, pied (élève · utilisateur · poste) et indicateurs
/// (sélection, main levée, verrouillé, hors ligne).
/// </summary>
internal sealed class StudentTile
{
    public AgentConnection Conn { get; }
    public Border Root { get; }
    public bool IsSelected { get; private set; }

    private readonly TextBlock _headerName = new() { Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _headerTime = new() { Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 230)), FontSize = 11 };
    private readonly TextBlock _stars = new() { Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 40)), FontSize = 12 };
    private readonly TextBlock _lesson = new() { Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 230)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Image _thumb = new() { Stretch = Stretch.Uniform };
    private readonly Border _thumbBox;
    private readonly Border _overlay;
    private readonly ContentControl _overlayGlyph = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _overlayText = new() { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _handBadge;
    private readonly Border _netBadge;
    private readonly Border _muteBadge;
    private readonly Border _inputBadge;
    private readonly Image _activeIcon = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private Avalonia.Media.Imaging.Bitmap? _activeIconBmp;
    private string _activeIconFor = "";
    private Point? _pressAt;
    private readonly TextBlock _footStudent = new() { FontSize = 11.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _footUser = new() { FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _footMachine = new() { FontSize = 11, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _signal = new() { FontSize = 10, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _card;
    private Bitmap? _bitmap;
    private DateTime _lastFrame = DateTime.MinValue;

    public string Status { get; private set; } = "Connexion…";
    public string LessonTitle { get; set; } = "";

    /// <summary>Clic : sélection. Double-clic : voir l'écran. Clic droit : menu des actions.</summary>
    public event Action<StudentTile, bool>? Clicked;      // bool = Ctrl/Maj enfoncé
    public event Action<StudentTile>? DoubleClicked;
    public event Action<StudentTile, Control>? ContextRequested;
    /// <summary>Le professeur fait glisser la vignette (pour la déplacer).</summary>
    public event Action<StudentTile, PointerEventArgs>? DragRequested;

    public StudentTile(AgentConnection conn, double width)
    {
        Conn = conn;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            Height = 22,
            Margin = new Thickness(8, 0),
        };
        header.Children.Add(_headerName);
        _headerTime.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(_headerTime, 1);
        header.Children.Add(_headerTime);
        Grid.SetColumn(_stars, 2);
        header.Children.Add(_stars);
        Grid.SetColumn(_lesson, 3);
        header.Children.Add(_lesson);
        foreach (var c in header.Children.OfType<TextBlock>()) c.VerticalAlignment = VerticalAlignment.Center;

        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 10, 20, 40)),
            IsVisible = false,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Children = { _overlayGlyph, _overlayText },
            },
        };
        _handBadge = new Border
        {
            Background = Palette.Hand,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    Glyph.Make(Glyph.Hand, 12, Brushes.White),
                    new TextBlock { Text = "Aide", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Bold },
                },
            },
        };
        _netBadge = new Border
        {
            Background = Palette.Danger,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(7, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    Glyph.Make(Glyph.WebOff, 13, Brushes.White),
                    new TextBlock { Text = "Internet coupé", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Bold },
                },
            },
        };
        _muteBadge = new Border
        {
            Background = Palette.Navy,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(7, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    Glyph.Make(Glyph.SoundOff, 13, Brushes.White),
                    new TextBlock { Text = "Son coupé", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Bold },
                },
            },
        };
        _inputBadge = new Border
        {
            Background = Palette.Accent,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(7, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    Glyph.Make(Glyph.InputLock, 13, Brushes.White),
                    new TextBlock { Text = "Clavier/souris bloqués", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Bold },
                },
            },
        };
        _thumbBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 38, 52)),
            Child = new Grid { Children = { _thumb, _overlay, _handBadge, _netBadge, _muteBadge, _inputBadge } },
        };

        var cardContent = new DockPanel();
        var headerBar = new Border { Background = Palette.Navy, Child = header };
        DockPanel.SetDock(headerBar, Dock.Top);
        cardContent.Children.Add(headerBar);
        cardContent.Children.Add(_thumbBox);

        _card = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Palette.Line,
            Background = Brushes.White,
            ClipToBounds = true,
            Child = cardContent,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 6, Color = Color.FromArgb(40, 0, 0, 0) }),
        };

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,*"), Margin = new Thickness(2, 4, 2, 0) };
        foot.Children.Add(_signal);
        var studentCell = new StackPanel { Orientation = Orientation.Horizontal, Children = { _activeIcon, _footStudent } };
        Grid.SetColumn(studentCell, 1);
        foot.Children.Add(studentCell);
        Grid.SetColumn(_footUser, 2);
        foot.Children.Add(_footUser);
        Grid.SetColumn(_footMachine, 3);
        foot.Children.Add(_footMachine);

        var outer = new StackPanel { Children = { _card, foot } };
        Root = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Child = outer,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        Root.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(Root);
            if (p.Properties.IsRightButtonPressed)
            {
                ContextRequested?.Invoke(this, Root);
                e.Handled = true;
                return;
            }
            if (e.ClickCount >= 2) DoubleClicked?.Invoke(this);
            else
            {
                _pressAt = e.GetPosition(Root);
                Clicked?.Invoke(this, e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
            e.Handled = true;
        };
        // Glisser : au-delà de quelques pixels avec le bouton gauche enfoncé, on déplace la vignette.
        Root.PointerMoved += (_, e) =>
        {
            if (_pressAt is not { } start || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(Root);
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 10) return;
            _pressAt = null;
            DragRequested?.Invoke(this, e);
        };
        Root.PointerReleased += (_, _) => _pressAt = null;

        conn.FrameReceived += OnFrame;
        SetWidth(width);
        Refresh();

        // Horloge du bandeau + indicateur de signal (fraîcheur des images).
        DispatcherTimer.Run(() =>
        {
            _headerTime.Text = DateTime.Now.ToString("HH:mm");
            UpdateSignal();
            return true;
        }, TimeSpan.FromSeconds(1));
    }

    public void SetWidth(double width)
    {
        _card.Width = width;
        _thumbBox.Height = Math.Round(width * 0.6);
        ((StackPanel)Root.Child!).Width = width;
    }

    private void OnFrame(ScreenFramePayload f) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            using var ms = new MemoryStream(f.Jpeg);
            var bmp = new Bitmap(ms);
            _thumb.Source = bmp;
            _bitmap?.Dispose();
            _bitmap = bmp;
            _lastFrame = DateTime.UtcNow;
        }
        catch { }
    });

    private void UpdateSignal()
    {
        // Barres vertes si les images arrivent, orange si elles ralentissent, grises sinon.
        double age = (DateTime.UtcNow - _lastFrame).TotalSeconds;
        _signal.Text = "▂▄▆";
        _signal.Foreground = !Conn.IsConnected ? Palette.Danger
            : age < 3 ? Palette.Ok
            : age < 10 ? Palette.Hand
            : Palette.Muted;
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        Refresh();
    }

    public void SetStatus(string status)
    {
        Status = status;
        Refresh();
    }

    /// <summary>Met à jour tout l'affichage à partir de l'état de la connexion.</summary>
    public void Refresh()
    {
        _headerName.Text = Conn.DisplayName;
        _headerTime.Text = DateTime.Now.ToString("HH:mm");
        _stars.Text = Conn.Stars switch
        {
            0 => "",
            <= 5 => new string('★', Conn.Stars),
            _ => "★ ×" + Conn.Stars,
        };
        _lesson.Text = LessonTitle;
        _footStudent.Text = Conn.StudentName.Length > 0 ? Conn.StudentName : "—";
        _footUser.Text = Conn.Info.UserName;
        _footMachine.Text = Conn.Info.MachineName;
        ToolTip.SetTip(Root, $"{Conn.Info.MachineName} — {Conn.Info.IpAddress}\n{Status}");

        _handBadge.IsVisible = Conn.IsHandRaised;
        _netBadge.IsVisible = Conn.IsConnected && Conn.IsInternetBlocked;
        _muteBadge.IsVisible = Conn.IsConnected && Conn.IsMuted;
        _inputBadge.IsVisible = Conn.IsConnected && Conn.IsInputLocked;
        UpdateActiveApp();
        if (!Conn.IsConnected)
        {
            ShowOverlay(Glyph.Cancel, Status.Length > 0 ? Status : "Hors ligne");
            _thumb.Opacity = 0.35;
        }
        else
        {
            _thumb.Opacity = 1;
            if (Conn.IsBlanked) ShowOverlay(Glyph.Blank, "Écran noir");
            else if (Conn.IsLocked) ShowOverlay(Glyph.Lock, "Verrouillé");
            else _overlay.IsVisible = false;
        }

        _card.BorderBrush = Conn.IsHandRaised ? Palette.Hand : IsSelected ? Palette.Accent : Palette.Line;
        _card.BorderThickness = new Thickness(Conn.IsHandRaised || IsSelected ? 3 : 2);
        Root.Background = IsSelected ? Palette.AccentLight : Brushes.Transparent;
        UpdateSignal();
    }

    private void ShowOverlay(string glyph, string text)
    {
        _overlayGlyph.Content = Glyph.Make(glyph, 34, Brushes.White);
        _overlayText.Text = text;
        _overlay.IsVisible = true;
    }

    /// <summary>Icône de l'application au premier plan chez l'élève (si la liste des applications est suivie).</summary>
    public void UpdateActiveApp()
    {
        var active = Conn.IsConnected ? Conn.Apps.FirstOrDefault(a => a.IsActive) : null;
        string key = active?.ProcessName ?? "";
        if (key != _activeIconFor)
        {
            _activeIconFor = key;
            _activeIconBmp?.Dispose();
            _activeIconBmp = null;
            if (active != null && Conn.IconFor(key) is { } png)
                try { using var ms = new MemoryStream(png); _activeIconBmp = new Avalonia.Media.Imaging.Bitmap(ms); } catch { }
            _activeIcon.Source = _activeIconBmp;
        }
        _activeIcon.IsVisible = _activeIconBmp != null;
        ToolTip.SetTip(_activeIcon, active == null ? null : $"Travaille sur : {active.Name}\n{active.Title}");
    }

    /// <summary>Repère d'insertion pendant un glisser-déposer : -1 avant, +1 après, 0 aucun.</summary>
    public void SetDropHint(int side)
    {
        Root.BorderBrush = side == 0 ? null : Palette.Accent;
        Root.BorderThickness = side < 0 ? new Thickness(4, 0, 0, 0) : side > 0 ? new Thickness(0, 0, 4, 0) : new Thickness(0);
    }
}
