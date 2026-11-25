using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClasseControle.Common;
using ClasseControle.Shared;

namespace ClasseControle.Console;

/// <summary>
/// Écran d'un élève en direct, avec deux modes optionnels :
///  • prise de contrôle (souris + clavier renvoyés à l'agent) ;
///  • annotation : le professeur dessine par-dessus l'écran (stylo, surligneur, formes,
///    flèches) et l'élève voit les mêmes traits sur son propre écran, en direct.
/// </summary>
internal sealed class RemoteViewWindow : Window
{
    private static readonly (string name, string hex)[] Colors =
    {
        ("Rouge", "#E53935"), ("Jaune", "#FDD835"), ("Vert", "#43A047"), ("Bleu", "#1E88E5"), ("Noir", "#212121"), ("Blanc", "#FFFFFF"),
    };
    private static readonly IBrush BarBg = new SolidColorBrush(Color.FromRgb(22, 27, 34));
    private static readonly IBrush Selected = new SolidColorBrush(Color.FromRgb(56, 98, 160));

    private readonly AgentConnection _conn;
    private readonly Image _image;
    private readonly Canvas _ink = new() { IsHitTestVisible = false };
    private readonly AnnotationRenderer _renderer;
    private readonly CheckBox _controlToggle;
    private readonly ToggleButton _annotateToggle;
    private readonly Border _annotateBar;
    private readonly Dictionary<AnnotationTool, Button> _toolButtons = new();
    private readonly Dictionary<string, Button> _colorButtons = new();
    private readonly Dictionary<double, Button> _widthButtons = new();
    private Bitmap? _current;
    private int _srcW = 1, _srcH = 1;

    // Annotation en cours
    private AnnotationTool _tool = AnnotationTool.Pen;
    private string _color = "#E53935";
    private double _width = 0.004;
    private AnnotationStroke? _drawing;
    private int _nextId = 1;
    private DateTime _lastSend = DateTime.MinValue;

    /// <param name="control">Ouvre directement en prise de contrôle (souris + clavier).</param>
    /// <param name="annotate">Ouvre directement en mode annotation.</param>
    public RemoteViewWindow(AgentConnection conn, bool control = false, bool annotate = false)
    {
        _conn = conn;
        Width = 1100;
        Height = 720;
        Background = Brushes.Black;
        _renderer = new AnnotationRenderer(_ink);

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = true,
        };

        _controlToggle = new CheckBox
        {
            Content = new TextBlock { Text = "Prendre le contrôle (souris + clavier)", Foreground = Brushes.White },
            Margin = new Thickness(12, 0, 0, 0),
            IsChecked = control && !annotate,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _controlToggle.IsCheckedChanged += (_, _) =>
        {
            if (Control && Annotating) _annotateToggle!.IsChecked = false;
            UpdateTitle();
        };

        _annotateToggle = new ToggleButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { Glyph.Make(Glyph.Pen, 16, Brushes.White), new TextBlock { Text = "Annoter l'écran", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } },
            },
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(40, 48, 60)),
        };
        _annotateToggle.IsCheckedChanged += async (_, _) => await SetAnnotatingAsync(_annotateToggle.IsChecked == true);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = BarBg,
            Height = 42,
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(21, 41, 77)),
                    Padding = new Thickness(14, 0),
                    Child = new TextBlock
                    {
                        Text = $"Vous visualisez : {conn.DisplayName} — {conn.Info.MachineName}",
                        Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    },
                },
                _controlToggle,
                _annotateToggle,
            },
        };

        _annotateBar = BuildAnnotateBar();

        var stage = new Grid { Children = { _image, _ink } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        grid.Children.Add(toolbar);
        Grid.SetRow(_annotateBar, 1);
        grid.Children.Add(_annotateBar);
        Grid.SetRow(stage, 2);
        grid.Children.Add(stage);
        Content = grid;

        _conn.FrameReceived += OnFrame;
        stage.SizeChanged += (_, _) => UpdateInkArea();

        // Souris : contrôle à distance OU dessin selon le mode.
        _image.PointerMoved += (_, e) => { if (Annotating) DrawMove(e); else SendMouse(InputKind.MouseMove, e); };
        _image.PointerPressed += (_, e) => { _image.Focus(); if (Annotating) DrawStart(e); else SendMouseButton(e, down: true); };
        _image.PointerReleased += (_, e) => { if (Annotating) DrawEnd(e); else SendMouseButton(e, down: false); };
        _image.PointerWheelChanged += (_, e) =>
        {
            if (!Control) return;
            _ = _conn.SendInput(new RemoteInputPayload { Kind = InputKind.MouseWheel, Wheel = (int)(e.Delta.Y * 120) });
        };

        KeyDown += (_, e) =>
        {
            if (Annotating && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _ = UndoAsync(); e.Handled = true; return; }
            SendKey(e, down: true);
        };
        KeyUp += (_, e) => SendKey(e, down: false);

        Opened += async (_, _) =>
        {
            await _conn.AcquireStream("view", new StartScreenPayload { Quality = 55, MaxWidth = 1600, Fps = 8, AllowControl = true });
            if (annotate) _annotateToggle.IsChecked = true;
        };
        Closed += async (_, _) =>
        {
            _conn.FrameReceived -= OnFrame;
            if (Annotating) { try { await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.End }); } catch { } }
            try { await _conn.ReleaseStream("view"); } catch { }
            _current?.Dispose();
        };
        UpdateTitle();
    }

    private bool Control => _controlToggle.IsChecked == true;
    private bool Annotating => _annotateToggle.IsChecked == true;

    private void UpdateTitle() =>
        Title = (Annotating ? "Annotation — " : Control ? "Contrôle — " : "Écran — ") + _conn.Title;

    // =====================================================================
    //  Annotation
    // =====================================================================
    private Border BuildAnnotateBar()
    {
        Button Tool(AnnotationTool tool, string glyph, string tip)
        {
            var b = BarButton(Glyph.Make(glyph, 18, Brushes.White), tip);
            b.Click += (_, _) => { _tool = tool; RefreshBar(); };
            _toolButtons[tool] = b;
            return b;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 4) };
        row.Children.Add(Tool(AnnotationTool.Pen, Glyph.Pen, "Stylo"));
        row.Children.Add(Tool(AnnotationTool.Highlighter, Glyph.Highlighter, "Surligneur"));
        row.Children.Add(Tool(AnnotationTool.Rectangle, Glyph.RectTool, "Rectangle"));
        row.Children.Add(Tool(AnnotationTool.Ellipse, Glyph.EllipseTool, "Cercle / ellipse"));
        row.Children.Add(Tool(AnnotationTool.Arrow, Glyph.ArrowTool, "Flèche"));
        row.Children.Add(new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 4) });
        foreach (var (name, hex) in Colors)
        {
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.Parse(hex)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var b = BarButton(swatch, name);
            string c = hex;
            b.Click += (_, _) => { _color = c; RefreshBar(); };
            _colorButtons[hex] = b;
            row.Children.Add(b);
        }
        row.Children.Add(new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 4) });
        foreach (var (label, w) in new[] { ("Fin", 0.0025), ("Moyen", 0.004), ("Épais", 0.008) })
        {
            var b = BarButton(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 12 }, "Épaisseur : " + label.ToLowerInvariant());
            double width = w;
            b.Click += (_, _) => { _width = width; RefreshBar(); };
            _widthButtons[w] = b;
            row.Children.Add(b);
        }
        row.Children.Add(new Border { Width = 1, Background = Brushes.Gray, Margin = new Thickness(8, 4) });
        var undo = BarButton(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Glyph.Make(Glyph.Undo, 16, Brushes.White), new TextBlock { Text = "Annuler", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } } }, "Annuler le dernier trait (Ctrl+Z)");
        undo.Click += async (_, _) => await UndoAsync();
        var clear = BarButton(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Glyph.Make(Glyph.Delete, 16, Brushes.White), new TextBlock { Text = "Tout effacer", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } } }, "Effacer toutes les annotations");
        clear.Click += async (_, _) =>
        {
            _renderer.Clear();
            await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.Clear });
        };
        row.Children.Add(undo);
        row.Children.Add(clear);
        row.Children.Add(new TextBlock
        {
            Text = "L'élève voit vos traits sur son écran ; son clavier et sa souris sont en pause pendant l'annotation.",
            Foreground = new SolidColorBrush(Color.FromRgb(160, 170, 185)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        });
        RefreshBar();
        return new Border { Background = new SolidColorBrush(Color.FromRgb(30, 36, 46)), Child = row, IsVisible = false };
    }

    private static Button BarButton(Control content, string tip)
    {
        var b = new Button { Content = content, Padding = new Thickness(8, 5), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5) };
        ToolTip.SetTip(b, tip);
        return b;
    }

    private void RefreshBar()
    {
        foreach (var (t, b) in _toolButtons) b.Background = t == _tool ? Selected : Brushes.Transparent;
        foreach (var (c, b) in _colorButtons) b.Background = c == _color ? Selected : Brushes.Transparent;
        foreach (var (w, b) in _widthButtons) b.Background = Math.Abs(w - _width) < 1e-6 ? Selected : Brushes.Transparent;
    }

    private async Task SetAnnotatingAsync(bool on)
    {
        if (on) _controlToggle.IsChecked = false;
        _annotateBar.IsVisible = on;
        _image.Cursor = on ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
        UpdateTitle();
        if (on)
        {
            await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.Begin });
            // Si des traits existent déjà (réouverture), on les renvoie.
            foreach (var s in _renderer.Strokes) await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.Stroke, Stroke = s });
            ActionLog.Add("Annotation de l'écran", _conn.Info.MachineName);
        }
        else
        {
            _renderer.Clear();
            await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.End });
        }
    }

    /// <summary>Zone de l'image où s'affiche réellement l'écran de l'élève (bandes noires exclues).</summary>
    private Rect DisplayRect()
    {
        double cw = _image.Bounds.Width, ch = _image.Bounds.Height;
        if (cw <= 0 || ch <= 0 || _srcW <= 0 || _srcH <= 0) return default;
        double scale = Math.Min(cw / _srcW, ch / _srcH);
        double w = _srcW * scale, h = _srcH * scale;
        return new Rect((cw - w) / 2, (ch - h) / 2, w, h);
    }

    private void UpdateInkArea() => _renderer.SetArea(DisplayRect());

    private void DrawStart(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_image).Properties.IsLeftButtonPressed) return;
        if (!TryNormalize(e.GetPosition(_image), out double x, out double y, clamp: false)) return;
        _drawing = new AnnotationStroke { Id = _nextId++, Tool = _tool, Color = _color, Width = _width, Points = { x, y } };
        if (_tool != AnnotationTool.Pen && _tool != AnnotationTool.Highlighter) { _drawing.Points.Add(x); _drawing.Points.Add(y); }
        _renderer.Upsert(_drawing);
        e.Pointer.Capture(_image);
    }

    private void DrawMove(PointerEventArgs e)
    {
        if (_drawing == null) return;
        TryNormalize(e.GetPosition(_image), out double x, out double y, clamp: true);
        if (_drawing.Tool is AnnotationTool.Pen or AnnotationTool.Highlighter)
        {
            double lx = _drawing.Points[^2], ly = _drawing.Points[^1];
            if (Math.Abs(x - lx) + Math.Abs(y - ly) < 0.0015) return; // évite les points inutiles
            _drawing.Points.Add(x);
            _drawing.Points.Add(y);
        }
        else
        {
            _drawing.Points[^2] = x;
            _drawing.Points[^1] = y;
        }
        _renderer.Upsert(_drawing);
        // Envoi progressif (25 fois par seconde au plus) : l'élève voit le trait se dessiner.
        if (DateTime.UtcNow - _lastSend > TimeSpan.FromMilliseconds(40)) SendDrawing();
    }

    private void DrawEnd(PointerReleasedEventArgs e)
    {
        if (_drawing == null) return;
        SendDrawing();
        _drawing = null;
        e.Pointer.Capture(null);
    }

    private void SendDrawing()
    {
        if (_drawing == null) return;
        _lastSend = DateTime.UtcNow;
        var copy = new AnnotationStroke { Id = _drawing.Id, Tool = _drawing.Tool, Color = _drawing.Color, Width = _drawing.Width, Points = _drawing.Points.ToList() };
        _ = _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.Stroke, Stroke = copy });
    }

    private async Task UndoAsync()
    {
        if (_renderer.Count == 0) return;
        int id = _renderer.Strokes[^1].Id;
        _renderer.Remove(id);
        await _conn.Annotate(new AnnotationPayload { Action = AnnotationAction.Undo, StrokeId = id });
    }

    // =====================================================================
    //  Image et contrôle à distance
    // =====================================================================
    private void OnFrame(ScreenFramePayload f) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            int w = f.ScreenWidth > 0 ? f.ScreenWidth : f.Width, h = f.ScreenHeight > 0 ? f.ScreenHeight : f.Height;
            bool sizeChanged = w != _srcW || h != _srcH;
            _srcW = w;
            _srcH = h;
            using var ms = new MemoryStream(f.Jpeg);
            var bmp = new Bitmap(ms);
            _image.Source = bmp;
            _current?.Dispose();
            _current = bmp;
            if (sizeChanged) UpdateInkArea();
        }
        catch { }
    });

    /// <summary>Convertit une position pointeur en coordonnées normalisées 0..1,
    /// en tenant compte du letterboxing (Stretch=Uniform).</summary>
    private bool TryNormalize(Point p, out double nx, out double ny, bool clamp = false)
    {
        nx = ny = 0;
        var r = DisplayRect();
        if (r.Width <= 0 || r.Height <= 0) return false;
        double x = p.X - r.X, y = p.Y - r.Y;
        if (!clamp && (x < 0 || y < 0 || x > r.Width || y > r.Height)) return false;
        nx = Math.Clamp(x / r.Width, 0, 1);
        ny = Math.Clamp(y / r.Height, 0, 1);
        return true;
    }

    private void SendMouse(InputKind kind, PointerEventArgs e)
    {
        if (!Control) return;
        if (!TryNormalize(e.GetPosition(_image), out double nx, out double ny)) return;
        _ = _conn.SendInput(new RemoteInputPayload { Kind = kind, X = nx, Y = ny });
    }

    private void SendMouseButton(PointerEventArgs e, bool down)
    {
        if (!Control) return;
        var pt = e.GetCurrentPoint(_image);
        int button = pt.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 1,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 2,
            _ => 0,
        };
        if (!TryNormalize(e.GetPosition(_image), out double nx, out double ny)) return;
        // On déplace d'abord puis on clique, pour positionner le curseur distant.
        _ = _conn.SendInput(new RemoteInputPayload { Kind = InputKind.MouseMove, X = nx, Y = ny });
        _ = _conn.SendInput(new RemoteInputPayload
        {
            Kind = down ? InputKind.MouseDown : InputKind.MouseUp,
            X = nx, Y = ny, Button = button,
        });
    }

    private void SendKey(KeyEventArgs e, bool down)
    {
        if (!Control) return;
        int vk = VkMap.ToVk(e.Key);
        if (vk == 0) return;
        e.Handled = true;
        _ = _conn.SendInput(new RemoteInputPayload
        {
            Kind = down ? InputKind.KeyDown : InputKind.KeyUp,
            KeyCode = vk,
        });
    }
}
