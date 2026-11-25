using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClasseControle.Common;
using NAudio.Wave;

namespace ClasseControle.Console;

/// <summary>
/// Annonce audio : le micro du professeur est diffusé en direct sur les postes
/// (PCM 16 kHz mono, ~32 Ko/s par poste — supportable par un réseau de salle).
/// </summary>
internal sealed class Announcer : IDisposable
{
    private WaveInEvent? _in;
    private List<AgentConnection> _targets = new();

    public bool IsLive => _in != null;
    public int TargetCount => _targets.Count;

    public async Task StartAsync(IEnumerable<AgentConnection> targets, string speaker)
    {
        if (IsLive) return;
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("Aucun microphone détecté sur ce poste.");
        _targets = targets.ToList();
        var format = new WaveFormat(16000, 16, 1);
        foreach (var t in _targets) await t.StartAudio(new AudioStartPayload { SampleRate = 16000, Channels = 1, Speaker = speaker });

        var input = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 100, NumberOfBuffers = 3 };
        input.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded <= 0) return;
            var pcm = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
            foreach (var t in _targets) _ = t.SendAudio(pcm);
        };
        input.StartRecording();
        _in = input;
    }

    public async Task StopAsync()
    {
        var input = _in;
        _in = null;
        if (input != null)
        {
            try { input.StopRecording(); } catch { }
            input.Dispose();
        }
        foreach (var t in _targets) { try { await t.StopAudio(); } catch { } }
        _targets.Clear();
    }

    public void Dispose() => _ = StopAsync();
}

/// <summary>Discussion du professeur avec un ou plusieurs élèves.</summary>
internal sealed class ConsoleChatWindow : Window
{
    private readonly List<AgentConnection> _members;
    private readonly Func<string> _teacher;
    private readonly List<string> _lines = new();
    private readonly ListBox _list;
    private readonly TextBox _input;

    public IReadOnlyList<AgentConnection> Members => _members;

    public ConsoleChatWindow(IEnumerable<AgentConnection> members, Func<string> teacher)
    {
        _members = members.ToList();
        _teacher = teacher;
        Title = _members.Count == 1 ? $"Discussion — {_members[0].DisplayName} ({_members[0].Info.MachineName})"
                                    : $"Discussion — {_members.Count} élèves";
        Width = 520;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var who = new TextBlock
        {
            Text = "Participants : " + string.Join(", ", _members.Select(m => m.DisplayName)),
            Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 10, 12, 4),
        };
        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = s ?? "", TextWrapping = TextWrapping.Wrap }),
            Margin = new Thickness(8, 0),
        };
        _input = new TextBox { Watermark = "Votre message…" };
        var send = new Button { Content = "Envoyer", Margin = new Thickness(6, 0, 0, 0) };
        send.Click += async (_, _) => await PostAsync();
        _input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await PostAsync(); } };

        var row = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(send, Dock.Right);
        row.Children.Add(send);
        row.Children.Add(_input);
        var root = new DockPanel();
        DockPanel.SetDock(who, Dock.Top);
        DockPanel.SetDock(row, Dock.Bottom);
        root.Children.Add(who);
        root.Children.Add(row);
        root.Children.Add(_list);
        Content = root;

        foreach (var m in _members) m.ChatReceived += OnChat;
        Closed += (_, _) => { foreach (var m in _members) m.ChatReceived -= OnChat; };
        Opened += (_, _) => _input.Focus();
    }

    private void OnChat(AgentConnection from, ChatPayload p) => Dispatcher.UIThread.Post(() =>
    {
        Add($"[{p.Time.ToLocalTime():HH:mm}] {from.DisplayName} : {p.Text}");
        Activate();
    });

    /// <summary>Message reçu avant l'ouverture de la fenêtre.</summary>
    public void AddIncoming(AgentConnection from, ChatPayload p) =>
        Add($"[{p.Time.ToLocalTime():HH:mm}] {from.DisplayName} : {p.Text}");

    private void Add(string line)
    {
        _lines.Add(line);
        _list.ItemsSource = _lines.ToList();
        _list.ScrollIntoView(_lines.Count - 1);
    }

    private async Task PostAsync()
    {
        string t = (_input.Text ?? "").Trim();
        if (t.Length == 0) return;
        _input.Text = "";
        var msg = new ChatPayload { Author = "Professeur — " + _teacher(), Text = t, Time = DateTime.UtcNow };
        foreach (var m in _members.Where(m => m.IsConnected)) await m.Chat(msg);
        Add($"[{DateTime.Now:HH:mm}] Moi : {t}");
        ActionLog.Add("Discussion", string.Join(", ", _members.Select(m => m.Info.MachineName)), t);
    }
}

/// <summary>Sondage rapide : composition, envoi, résultats en direct.</summary>
internal sealed class SurveyWindow : Window
{
    private static readonly (string label, string[] options)[] Presets =
    {
        ("Oui / Non", new[] { "Oui", "Non" }),
        ("Compris ?", new[] { "👍 J'ai compris", "🤔 Pas sûr", "👎 Pas compris" }),
        ("A B C D", new[] { "A", "B", "C", "D" }),
        ("Humeur", new[] { "😀", "🙂", "😐", "🙁" }),
        ("Vrai / Faux", new[] { "Vrai", "Faux" }),
    };

    private readonly List<AgentConnection> _targets;
    private readonly TextBox _question = new() { Watermark = "Votre question…", FontSize = 15 };
    private readonly TextBox _options = new() { AcceptsReturn = true, Height = 110, Watermark = "Une réponse par ligne" };
    private readonly StackPanel _compose;
    private readonly StackPanel _results = new() { Spacing = 8, IsVisible = false };
    private readonly Dictionary<string, int> _answers = new(); // clé poste → choix
    private SurveyPayload? _survey;

    public SurveyWindow(IEnumerable<AgentConnection> targets)
    {
        _targets = targets.ToList();
        Title = "Sondage";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var presets = new WrapPanel();
        foreach (var (label, opts) in Presets)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), FontSize = 12 };
            b.Click += (_, _) => _options.Text = string.Join(Environment.NewLine, opts);
            presets.Children.Add(b);
        }
        _options.Text = "Oui" + Environment.NewLine + "Non";

        var send = Ui.Btn("📊 Envoyer le sondage", async () => await StartAsync());
        _compose = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"Question posée à {_targets.Count} poste(s) :" },
                _question,
                new TextBlock { Text = "Réponses possibles :", Margin = new Thickness(0, 8, 0, 0) },
                presets,
                _options,
                Ui.ButtonRow(Ui.Btn("Annuler", Close), send),
            },
        };

        Content = new StackPanel { Margin = new Thickness(20), Children = { _compose, _results } };
        Closed += async (_, _) => await FinishAsync();
    }

    private async Task StartAsync()
    {
        var opts = (_options.Text ?? "").Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).Take(8).ToList();
        string q = (_question.Text ?? "").Trim();
        if (q.Length == 0 || opts.Count < 2) { _question.Watermark = "Saisissez une question et au moins 2 réponses"; return; }

        _survey = new SurveyPayload { Id = Guid.NewGuid().ToString("N")[..8], Question = q, Options = opts };
        foreach (var t in _targets)
        {
            t.SurveyAnswered += OnAnswer;
            await t.StartSurvey(_survey);
        }
        ActionLog.Add("Sondage", $"{_targets.Count} poste(s)", $"{q} [{string.Join(" / ", opts)}]");
        _compose.IsVisible = false;
        _results.IsVisible = true;
        Render();
    }

    private void OnAnswer(AgentConnection from, SurveyAnswerPayload a) => Dispatcher.UIThread.Post(() =>
    {
        if (_survey == null || a.Id != _survey.Id || a.Choice < 0 || a.Choice >= _survey.Options.Count) return;
        _answers[from.Key] = a.Choice;
        Render();
    });

    private void Render()
    {
        if (_survey == null) return;
        _results.Children.Clear();
        _results.Children.Add(new TextBlock { Text = _survey.Question, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _results.Children.Add(Ui.Hint($"{_answers.Count} réponse(s) sur {_targets.Count} poste(s)"));

        int total = Math.Max(1, _answers.Count);
        for (int i = 0; i < _survey.Options.Count; i++)
        {
            int n = _answers.Values.Count(v => v == i);
            var who = _targets.Where(t => _answers.TryGetValue(t.Key, out var c) && c == i).Select(t => t.DisplayName).ToList();
            var bar = new Border
            {
                Background = Palette.Accent, Height = 22, CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 360.0 * n / total),
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*,60") };
            row.Children.Add(new TextBlock { Text = _survey.Options[i], VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var count = new TextBlock { Text = $"{n} ({Math.Round(100.0 * n / total)} %)", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            ToolTip.SetTip(row, who.Count > 0 ? string.Join(", ", who) : "Personne");
            _results.Children.Add(row);
        }
        var missing = _targets.Where(t => !_answers.ContainsKey(t.Key)).Select(t => t.DisplayName).ToList();
        if (missing.Count > 0) _results.Children.Add(Ui.Hint("Pas encore répondu : " + string.Join(", ", missing)));
        _results.Children.Add(Ui.ButtonRow(Ui.Btn("⏹ Terminer le sondage", Close)));
    }

    private async Task FinishAsync()
    {
        if (_survey == null) return;
        var s = _survey;
        _survey = null;
        foreach (var t in _targets)
        {
            t.SurveyAnswered -= OnAnswer;
            try { await t.CloseSurvey(s.Id); } catch { }
        }
        var summary = string.Join(" | ", s.Options.Select((o, i) => $"{o} : {_answers.Values.Count(v => v == i)}"));
        ActionLog.Add("Résultats du sondage", $"{_answers.Count}/{_targets.Count} réponse(s)", $"{s.Question} → {summary}");
    }
}

/// <summary>Favoris du lancement rapide (« Nom = programme ou adresse »).</summary>
internal sealed class QuickLaunchDialog : Window
{
    private readonly TextBox _box;
    public List<QuickItem>? Result { get; private set; }

    public QuickLaunchDialog(IEnumerable<QuickItem> items)
    {
        Title = "Lancement rapide — favoris";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _box = new TextBox
        {
            AcceptsReturn = true, Height = 220, FontFamily = new FontFamily("Consolas, monospace"),
            Text = string.Join(Environment.NewLine, items.Select(i => $"{i.Name} = {i.Target}")),
        };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Une ligne par favori :  Nom = programme, fichier ou adresse web" },
                Ui.Hint("Exemples :  Word = winword    ·    Calculatrice = calc    ·    Moodle = https://moodle.exemple.ma"),
                _box,
                Ui.ButtonRow(Ui.Btn("Annuler", Close), Ui.Btn("💾 Enregistrer", () =>
                {
                    Result = (_box.Text ?? "").Split('\n')
                        .Select(l => l.Split('=', 2))
                        .Where(p => p.Length == 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0)
                        .Select(p => new QuickItem { Name = p[0].Trim(), Target = p[1].Trim() })
                        .ToList();
                    Close();
                })),
            },
        };
    }
}
