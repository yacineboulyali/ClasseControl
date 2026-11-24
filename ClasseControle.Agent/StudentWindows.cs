using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>Discussion privée entre l'élève et le professeur.</summary>
internal sealed class ChatWindow : Window
{
    private readonly List<string> _lines = new();
    private readonly ListBox _list;
    private readonly TextBox _input;
    private readonly Action<string> _send;
    private bool _allowClose;

    public ChatWindow(Action<string> send)
    {
        _send = send;
        Title = "Discussion avec le professeur";
        Width = 420;
        Height = 480;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = s ?? "", TextWrapping = TextWrapping.Wrap }),
        };
        _input = new TextBox { Watermark = "Votre message…" };
        var btn = new Button { Content = "Envoyer", Margin = new Thickness(6, 0, 0, 0) };
        btn.Click += (_, _) => Post();
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Post(); e.Handled = true; } };

        var row = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(btn, Dock.Right);
        row.Children.Add(btn);
        row.Children.Add(_input);
        var root = new DockPanel();
        DockPanel.SetDock(row, Dock.Bottom);
        root.Children.Add(row);
        root.Children.Add(_list);
        Content = root;

        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; Hide(); } };
        Opened += (_, _) => _input.Focus();
    }

    public void Add(ChatPayload c)
    {
        _lines.Add($"[{c.Time.ToLocalTime():HH:mm}] {c.Author} : {c.Text}");
        _list.ItemsSource = _lines.ToList();
        _list.ScrollIntoView(_lines.Count - 1);
    }

    private void Post()
    {
        string t = (_input.Text ?? "").Trim();
        if (t.Length == 0) return;
        _input.Text = "";
        _send(t);
    }

    public void ForceClose() { _allowClose = true; Close(); }
}

/// <summary>Sondage rapide : une question, un bouton par réponse.</summary>
internal sealed class SurveyPopup : Window
{
    public string SurveyId { get; }
    private bool _allowClose;

    public SurveyPopup(SurveyPayload s, Action<int> answer)
    {
        SurveyId = s.Id;
        Title = "Sondage du professeur";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        for (int i = 0; i < s.Options.Count; i++)
        {
            int idx = i;
            var b = new Button
            {
                Content = s.Options[i], FontSize = 16, Padding = new Thickness(18, 10), Margin = new Thickness(6),
                MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            b.Click += (_, _) => { answer(idx); ForceClose(); };
            buttons.Children.Add(b);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = "📊 Sondage", FontSize = 14, Foreground = Brushes.Gray },
                new TextBlock { Text = s.Question, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
                buttons,
            },
        };
        // L'élève doit répondre : la fenêtre ne se ferme pas d'elle-même.
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
    }

    public void ForceClose() { _allowClose = true; Close(); }
}
