using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Espace d'équipe de l'élève : un document partagé que tous les membres du
/// groupe éditent en même temps, et une discussion de groupe.
///
/// Synchronisation : le professeur fait foi. L'élève envoie son texte complet
/// (avec la révision sur laquelle il l'a écrit) après une courte pause de frappe ;
/// une seule modification est « en vol » à la fois. Les textes reçus des autres
/// sont fusionnés avec ce que l'élève est en train de taper (rien n'est perdu).
/// </summary>
internal sealed class TeamWindow : Window
{
    private readonly TextBox _doc;
    private readonly ListBox _chat;
    private readonly TextBox _chatInput;
    private readonly TextBlock _header;
    private readonly TextBlock _members;
    private readonly TextBlock _state;
    private readonly DispatcherTimer _debounce;
    private readonly List<string> _chatLines = new();

    private readonly Action<GroupDocPayload> _sendDoc;
    private readonly Action<string> _sendChat;

    private string _groupId = "";
    private int _rev;
    private string _synced = "";     // texte faisant foi à la révision _rev
    private string _lastSent = "";   // texte envoyé, en attente d'accusé
    private int _seq;
    private int _inflight;           // numéro de la modification en vol (0 = aucune)
    private bool _applying;
    private bool _allowClose;

    public string GroupId => _groupId;

    public TeamWindow(Action<GroupDocPayload> sendDoc, Action<string> sendChat)
    {
        _sendDoc = sendDoc;
        _sendChat = sendChat;
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));

        _header = new TextBlock { FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        _members = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)), TextWrapping = TextWrapping.Wrap };
        _state = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)), HorizontalAlignment = HorizontalAlignment.Right };

        _doc = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Watermark = "Document partagé de l'équipe : tout le groupe peut écrire ici en même temps…",
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        _doc.TextChanged += (_, _) =>
        {
            if (_applying) return;
            _state.Text = "✏️ modification…";
            _debounce!.Stop();
            _debounce.Start();
        };

        _chat = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = s ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 13 }),
        };
        _chatInput = new TextBox { Watermark = "Message à l'équipe…" };
        var send = new Button { Content = "Envoyer", Margin = new Thickness(6, 0, 0, 0) };
        send.Click += (_, _) => PostChat();
        _chatInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { PostChat(); e.Handled = true; } };

        var chatInputRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(send, Dock.Right);
        chatInputRow.Children.Add(send);
        chatInputRow.Children.Add(_chatInput);

        var chatPanel = new DockPanel();
        var chatTitle = new TextBlock { Text = "💬 Discussion", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(chatTitle, Dock.Top);
        DockPanel.SetDock(chatInputRow, Dock.Bottom);
        chatPanel.Children.Add(chatTitle);
        chatPanel.Children.Add(chatInputRow);
        chatPanel.Children.Add(_chat);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,12,*"), Margin = new Thickness(16, 8, 16, 16) };
        body.Children.Add(_doc);
        Grid.SetColumn(chatPanel, 2);
        body.Children.Add(chatPanel);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 14, 16, 0) };
        top.Children.Add(new StackPanel { Children = { _header, _members } });
        Grid.SetColumn(_state, 1);
        top.Children.Add(_state);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); FlushEdit(); };

        // L'élève peut réduire la fenêtre ; la fermeture ne fait que la masquer.
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void Open(GroupOpenPayload p)
    {
        _groupId = p.GroupId;
        Title = $"Espace d'équipe — {p.GroupName}";
        _header.Text = $"👥 {p.GroupName}";
        _members.Text = "Membres : " + string.Join(", ", p.Members);
        _rev = p.Revision;
        _synced = p.Text;
        _inflight = 0;
        SetLocal(p.Text);
        _chatLines.Clear();
        foreach (var c in p.History) _chatLines.Add(Format(c));
        _chat.ItemsSource = _chatLines.ToList();
        _state.Text = "✔ à jour";
        _doc.IsReadOnly = false;
    }

    /// <summary>Le professeur s'est déconnecté : le document passe en lecture seule.</summary>
    public void Disconnected()
    {
        _inflight = 0;
        _doc.IsReadOnly = true;
        _state.Text = "⚠ professeur déconnecté — lecture seule";
    }

    private void FlushEdit()
    {
        if (_inflight != 0 || _doc.IsReadOnly) return;
        string local = _doc.Text ?? "";
        if (local == _synced) { _state.Text = "✔ à jour"; return; }
        _inflight = ++_seq;
        _lastSent = local;
        _state.Text = "⏳ envoi…";
        _sendDoc(new GroupDocPayload { GroupId = _groupId, Text = local, Revision = _rev, Seq = _inflight });
    }

    public void ReceiveDoc(GroupDocPayload p)
    {
        if (p.GroupId != _groupId) return;
        if (p.AckSeq == 0 && p.Revision <= _rev) return; // message en retard : déjà intégré
        string local = _doc.Text ?? "";
        string merged;

        if (p.AckSeq != 0 && p.AckSeq == _inflight)
        {
            // Notre modification est intégrée : on rejoue ce qui a été tapé depuis l'envoi.
            merged = Rebase(local, _lastSent, p.Text);
            _inflight = 0;
        }
        else if (_inflight != 0)
        {
            // Une modification est en vol : l'accusé qui suit contiendra déjà ce texte.
            return;
        }
        else
        {
            merged = Rebase(local, _synced, p.Text);
        }

        _rev = p.Revision;
        _synced = p.Text;
        SetLocal(merged);
        if (merged != _synced) { _debounce.Stop(); _debounce.Start(); }
        else _state.Text = "✔ à jour";
    }

    /// <summary>Réapplique sur <paramref name="incoming"/> les changements locaux faits depuis <paramref name="baseText"/>.</summary>
    private static string Rebase(string local, string baseText, string incoming)
    {
        var mine = TextSplice.Diff(baseText, local);
        if (mine.IsEmpty) return incoming;
        var theirs = TextSplice.Diff(baseText, incoming);
        return mine.TransformAgainst(theirs).ApplyTo(incoming);
    }

    private void SetLocal(string text)
    {
        string current = _doc.Text ?? "";
        if (current == text) return;
        int caret = _doc.CaretIndex;
        var change = TextSplice.Diff(current, text);
        _applying = true;
        try
        {
            _doc.Text = text;
            _doc.CaretIndex = Math.Clamp(change.MapCaret(caret), 0, text.Length);
        }
        finally { _applying = false; }
    }

    public void ReceiveChat(GroupChatPayload c)
    {
        if (c.GroupId != _groupId) return;
        _chatLines.Add(Format(c));
        _chat.ItemsSource = _chatLines.ToList();
        _chat.ScrollIntoView(_chatLines.Count - 1);
        if (!IsVisible) { Show(); }
    }

    private void PostChat()
    {
        string t = (_chatInput.Text ?? "").Trim();
        if (t.Length == 0 || _doc.IsReadOnly) return;
        _chatInput.Text = "";
        _sendChat(t);
    }

    private static string Format(GroupChatPayload c) => $"[{c.Time.ToLocalTime():HH:mm}] {c.Author} : {c.Text}";

    public void ForceClose()
    {
        _allowClose = true;
        _debounce.Stop();
        Close();
    }
}
