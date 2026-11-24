using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClasseControle.Agent;

/// <summary>
/// Écran d'entrée en séance : plein écran, au premier plan. L'élève doit saisir
/// son nom complet et se connecter à la séance du professeur pour accéder au poste.
/// </summary>
internal sealed class LoginOverlay : Window
{
    private readonly TextBox _name;
    private readonly Button _enter;
    private readonly TextBlock _error;
    private readonly TextBlock _title;
    private bool _allowClose;

    public Func<bool>? YieldTo { get; set; }
    /// <summary>Appelé quand l'élève valide son nom.</summary>
    public event Action<string>? Submitted;

    public LoginOverlay(string sessionTitle, string teacher, string suggestedName)
    {
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));

        _title = new TextBlock
        {
            FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        var subtitle = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(teacher) ? "" : $"Professeur : {teacher}",
            FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 28),
        };
        _name = new TextBox
        {
            Watermark = "Prénom et nom",
            Text = suggestedName,
            FontSize = 20,
            Width = 420,
            Padding = new Thickness(12, 10),
        };
        _enter = new Button
        {
            Content = "Entrer en classe  →",
            FontSize = 18,
            Padding = new Thickness(24, 10),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
        };
        _error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73)),
            FontSize = 15, Margin = new Thickness(0, 14, 0, 0), TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
        };

        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "🎓", FontSize = 64, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) },
                _title, subtitle,
                new TextBlock { Text = "Saisissez votre nom pour accéder au poste :", FontSize = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) },
                _name, _enter, _error,
            },
        };

        SetSession(sessionTitle, teacher);
        _enter.Click += (_, _) => Submit();
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        // Bloque les raccourcis système courants dans la fenêtre (Alt+F4 géré par Closing).
        KeyDown += (_, e) => { if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) e.Handled = true; };
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
        Deactivated += (_, _) => { if (!(YieldTo?.Invoke() ?? false)) { Topmost = true; Activate(); } };
        Opened += (_, _) => _name.Focus();
    }

    public void SetSession(string title, string teacher) => _title.Text = $"Bienvenue — {title}";

    private void Submit()
    {
        string name = (_name.Text ?? "").Trim();
        if (name.Length < 3 || !name.Contains(' '))
        {
            ShowError("Veuillez saisir votre prénom ET votre nom.");
            return;
        }
        _enter.IsEnabled = false;
        _error.Text = "Connexion à la séance…";
        _error.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
        Submitted?.Invoke(name);
    }

    public void ShowError(string message)
    {
        _enter.IsEnabled = true;
        _error.Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73));
        _error.Text = message;
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }
}
