using System.Text;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>Un groupe de travail : ses membres, son document partagé et sa discussion.</summary>
internal sealed class TeamGroup
{
    public required int Number { get; init; }
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Name => $"Équipe {Number}";
    public List<string> MemberKeys { get; } = new();
    public SharedDocument Doc { get; init; } = new();
    public List<GroupChatPayload> Chat { get; } = new();
    /// <summary>Sérialise le traitement des messages du groupe (ordre des envois garanti).</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>
/// Gestion des groupes de travail côté professeur : répartition (manuelle ou
/// aléatoire), ouverture des espaces d'équipe chez les élèves, et relais
/// serveur du document partagé et de la discussion de chaque groupe.
/// </summary>
internal sealed class GroupManager
{
    private readonly Func<IEnumerable<AgentConnection>> _connections;
    private readonly Dictionary<int, TeamGroup> _groups = new();
    private readonly object _gate = new();

    /// <summary>Répartition en préparation : clé du poste → numéro d'équipe (0 = aucune).</summary>
    public Dictionary<string, int> Draft { get; } = new();
    public bool IsOpen { get; private set; }
    /// <summary>Modèle de document (consigne, plan à compléter) donné aux nouvelles équipes.</summary>
    public string Template { get; set; } = "";
    public event Action? Changed;

    public GroupManager(Func<IEnumerable<AgentConnection>> connections) => _connections = connections;

    public IReadOnlyList<TeamGroup> Groups
    {
        get { lock (_gate) return _groups.Values.OrderBy(g => g.Number).ToList(); }
    }

    private AgentConnection? Find(string key) => _connections().FirstOrDefault(c => c.Key == key);

    // ---------- Répartition ----------
    public void Shuffle(IReadOnlyList<AgentConnection> students, int teamSize)
    {
        teamSize = Math.Max(1, teamSize);
        var order = students.OrderBy(_ => Random.Shared.Next()).ToList();
        int teams = Math.Max(1, (int)Math.Ceiling(order.Count / (double)teamSize));
        Draft.Clear();
        // Distribution en tourniquet : les équipes ont des tailles équilibrées (écart ≤ 1).
        for (int i = 0; i < order.Count; i++)
            Draft[order[i].Key] = i % teams + 1;
    }

    // ---------- Ouverture / fermeture ----------
    public async Task OpenAsync()
    {
        var byNumber = Draft.Where(kv => kv.Value > 0).GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

        var removedMembers = new List<(string key, string groupId)>();
        lock (_gate)
        {
            // Les équipes conservent leur document si elles existent déjà.
            foreach (var n in _groups.Keys.Where(n => !byNumber.ContainsKey(n)).ToList())
            {
                removedMembers.AddRange(_groups[n].MemberKeys.Select(k => (k, _groups[n].Id)));
                _groups.Remove(n);
            }
            foreach (var (n, keys) in byNumber)
            {
                if (!_groups.TryGetValue(n, out var g)) _groups[n] = g = new TeamGroup { Number = n, Doc = new SharedDocument(Template) };
                removedMembers.AddRange(g.MemberKeys.Except(keys).Select(k => (k, g.Id)));
                g.MemberKeys.Clear();
                g.MemberKeys.AddRange(keys);
            }
            IsOpen = _groups.Count > 0;
        }

        foreach (var (key, id) in removedMembers)
            if (Find(key) is { IsConnected: true } c)
                await c.CloseGroup(id); // l'élève qui change d'équipe reçoit ensuite la nouvelle

        foreach (var g in Groups)
            foreach (var key in g.MemberKeys)
                if (Find(key) is { IsConnected: true } c) await c.OpenGroup(OpenPayload(g));
        Changed?.Invoke();
    }

    public async Task CloseAsync()
    {
        foreach (var g in Groups)
            foreach (var key in g.MemberKeys)
                if (Find(key) is { IsConnected: true } c) await c.CloseGroup(g.Id);
        lock (_gate) IsOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Un poste se (re)connecte : s'il appartient à une équipe ouverte, on lui rouvre son espace.</summary>
    public async Task OnConnectedAsync(AgentConnection c)
    {
        if (!IsOpen) return;
        var g = Groups.FirstOrDefault(x => x.MemberKeys.Contains(c.Key));
        if (g != null) await c.OpenGroup(OpenPayload(g));
    }

    private GroupOpenPayload OpenPayload(TeamGroup g)
    {
        List<GroupChatPayload> history;
        lock (g.Chat) history = g.Chat.TakeLast(100).ToList();
        return new GroupOpenPayload
        {
            GroupId = g.Id,
            GroupName = g.Name,
            Members = g.MemberKeys.Select(k => Find(k)?.DisplayName ?? k).ToList(),
            Text = g.Doc.Text,
            Revision = g.Doc.Revision,
            History = history,
        };
    }

    private TeamGroup? GroupOf(AgentConnection c, string groupId) =>
        Groups.FirstOrDefault(g => g.Id == groupId && g.MemberKeys.Contains(c.Key));

    // ---------- Relais ----------
    public async Task HandleDocAsync(AgentConnection from, GroupDocPayload p)
    {
        var g = GroupOf(from, p.GroupId);
        if (g == null || !IsOpen) return;
        await g.Gate.WaitAsync();
        try
        {
            var (applied, text, rev) = g.Doc.Submit(p.Revision, p.Ops.Select(o => o.ToSplice()).ToList());
            var ops = applied.Select(SpliceDto.From).ToList();
            foreach (var key in g.MemberKeys)
            {
                if (Find(key) is not { IsConnected: true } c) continue;
                bool author = c == from;
                // L'auteur reçoit l'accusé ; les autres reçoivent les modifications à appliquer.
                await c.SendGroupDoc(new GroupDocPayload
                {
                    GroupId = g.Id, Text = text, Revision = rev,
                    AckSeq = author ? p.Seq : 0,
                    Ops = author ? new List<SpliceDto>() : ops,
                });
            }
        }
        finally { g.Gate.Release(); }
        Changed?.Invoke();
    }

    public async Task HandleChatAsync(AgentConnection from, GroupChatPayload p)
    {
        var g = GroupOf(from, p.GroupId);
        if (g == null || !IsOpen || string.IsNullOrWhiteSpace(p.Text)) return;
        var msg = new GroupChatPayload
        {
            GroupId = g.Id, Author = from.DisplayName, Text = p.Text.Trim()[..Math.Min(p.Text.Trim().Length, 1000)], Time = DateTime.UtcNow,
        };
        await SendChatAsync(g, msg);
    }

    /// <summary>Le professeur écrit dans la discussion d'une équipe.</summary>
    public Task TeacherChatAsync(TeamGroup g, string teacher, string text) =>
        SendChatAsync(g, new GroupChatPayload { GroupId = g.Id, Author = "👩‍🏫 " + teacher, Text = text, Time = DateTime.UtcNow });

    private async Task SendChatAsync(TeamGroup g, GroupChatPayload msg)
    {
        await g.Gate.WaitAsync();
        try
        {
            lock (g.Chat) g.Chat.Add(msg);
            foreach (var key in g.MemberKeys)
                if (Find(key) is { IsConnected: true } c) await c.SendGroupChat(msg);
        }
        finally { g.Gate.Release(); }
        Changed?.Invoke();
    }

    // ---------- Export ----------
    public string Export()
    {
        string dir = Path.Combine(ActionLog.Folder, $"travaux-equipes-{DateTime.Now:yyyy-MM-dd-HHmm}");
        Directory.CreateDirectory(dir);
        foreach (var g in Groups)
        {
            var sb = new StringBuilder();
            sb.AppendLine(g.Name);
            sb.AppendLine("Membres : " + string.Join(", ", g.MemberKeys.Select(k => Find(k)?.DisplayName ?? k)));
            sb.AppendLine(new string('=', 60));
            sb.AppendLine(g.Doc.Text);
            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("Discussion :");
            lock (g.Chat)
                foreach (var c in g.Chat) sb.AppendLine($"[{c.Time.ToLocalTime():HH:mm}] {c.Author} : {c.Text}");
            File.WriteAllText(Path.Combine(dir, $"{g.Name.Replace(' ', '-')}.txt"), sb.ToString(), new UTF8Encoding(true));
        }
        return dir;
    }
}
