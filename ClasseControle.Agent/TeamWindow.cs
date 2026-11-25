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
/// Synchronisation (transformation opérationnelle) : chaque frappe est convertie en
/// modification (position, suppression, insertion) et envoyée au professeur, qui fait
/// foi ; les modifications des autres sont transformées contre les frappes en attente,
/// et le curseur de l'élève suit. Voir <see cref="SharedDocumentClient"/>.
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

    private readonly SharedDocumentClient _sync = new();
    private string _shown = "";      // texte de la zone tel que connu de _sync
    private bool _applying;
    private bool _captureQueued;
    private bool _allowClose;

    public string GroupId => _sync.GroupId;

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
            if (_applying || _captureQueued) return;
            // La zone de texte déplace le curseur juste après cet événement : on lit la
            // modification un instant plus tard, avec la bonne position de curseur.
            _captureQueued = true;
            Dispatcher.UIThread.Post(CaptureEdit, DispatcherPriority.Background);
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
        Title = $"Espace d'équipe — {p.GroupName}";
        _header.Text = $"👥 {p.GroupName}";
        _members.Text = "Membres : " + string.Join(", ", p.Members);
        _sync.Reset(p.GroupId, p.Text, p.Revision);
        _applying = true;
        try { _doc.Text = p.Text; _shown = p.Text; }
        finally { _applying = false; }
        _chatLines.Clear();
        foreach (var c in p.History) _chatLines.Add(Format(c));
        _chat.ItemsSource = _chatLines.ToList();
        _state.Text = "✔ à jour";
        _doc.IsReadOnly = false;
    }

    /// <summary>Le professeur s'est déconnecté : le document passe en lecture seule.</summary>
    public void Disconnected()
    {
        _doc.IsReadOnly = true;
        _state.Text = "⚠ professeur déconnecté — lecture seule";
    }

    /// <summary>Convertit la dernière saisie en modification et la met en attente d'envoi.</summary>
    private void CaptureEdit()
    {
        _captureQueued = false;
        string now = _doc.Text ?? "";
        if (now == _shown) return;
        _sync.LocalEdit(TextSplice.Diff(_shown, now, _doc.CaretIndex));
        _shown = now;
        _state.Text = "✏️ modification…";
        _debounce.Stop();
        _debounce.Start();
    }

    private void FlushEdit()
    {
        if (_doc.IsReadOnly) return;
        CaptureEdit();
        var outgoing = _sync.TakeOutgoing();
        if (outgoing != null) { _state.Text = "⏳ envoi…"; _sendDoc(outgoing); }
        else if (!_sync.HasPending) _state.Text = "✔ à jour";
    }

    public void ReceiveDoc(GroupDocPayload p)
    {
        if (p.GroupId != _sync.GroupId) return;
        CaptureEdit(); // la copie de synchronisation doit inclure la dernière frappe
        var applied = _sync.Receive(p);
        if (applied.Count > 0)
        {
            int caret = _doc.CaretIndex;
            foreach (var op in applied) caret = op.MapCaret(caret);
            _applying = true;
            try
            {
                _doc.Text = _sync.Text;
                _doc.CaretIndex = Math.Clamp(caret, 0, _sync.Text.Length);
            }
            finally { _applying = false; }
            _shown = _sync.Text;
        }
        // Accusé reçu : on envoie tout de suite ce qui a été tapé entre-temps.
        if (p.AckSeq != 0) FlushEdit();
        else if (!_sync.HasPending) _state.Text = "✔ à jour";
    }

    public void ReceiveChat(GroupChatPayload c)
    {
        if (c.GroupId != _sync.GroupId) return;
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
