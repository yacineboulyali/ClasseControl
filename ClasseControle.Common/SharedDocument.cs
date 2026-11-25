namespace ClasseControle.Common;

/// <summary>
/// Document partagé d'un groupe, côté professeur (qui fait office de serveur).
/// Les élèves envoient leurs modifications réelles (position, suppression, insertion)
/// écrites sur une révision donnée ; si d'autres modifications ont été acceptées entre-temps,
/// elles sont transformées pour s'appliquer au bon endroit, puis appliquées.
/// Chaque modification acceptée crée une révision. Thread-safe.
/// </summary>
public sealed class SharedDocument
{
    private const int HistoryLimit = 2000;
    private readonly object _gate = new();
    private readonly List<TextSplice> _history = new(); // _history[i] produit la révision _firstRev + i
    private int _firstRev = 1;

    public string Text { get; private set; }
    public int Revision { get; private set; }

    public SharedDocument(string initial = "") => Text = initial;

    /// <summary>
    /// Intègre une suite de modifications d'un élève, écrites sur <paramref name="baseRevision"/>.
    /// Renvoie les modifications telles qu'appliquées (à transmettre aux autres membres).
    /// </summary>
    public (List<TextSplice> applied, string text, int revision) Submit(int baseRevision, IReadOnlyList<TextSplice> ops)
    {
        lock (_gate)
        {
            var list = ops.ToList();
            // Transforme la suite contre l'historique postérieur à sa base (l'historique passe devant).
            int from = Math.Max(baseRevision, _firstRev - 1);
            for (int r = from + 1; r <= Revision; r++)
            {
                var h = _history[r - _firstRev];
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    list[i] = c.TransformAgainst(h, thisFirst: false);
                    h = h.TransformAgainst(c, thisFirst: true);
                }
            }
            foreach (var op in list)
            {
                Text = op.ApplyTo(Text);
                Revision++;
                _history.Add(op);
            }
            while (_history.Count > HistoryLimit) { _history.RemoveAt(0); _firstRev++; }
            return (list, Text, Revision);
        }
    }
}

/// <summary>
/// Côté élève : copie locale du document partagé. Les frappes sont appliquées tout de suite
/// et mises en attente ; une seule série est « en vol » à la fois. Les modifications des autres
/// sont transformées contre les frappes en attente, pour que rien ne soit perdu ni déplacé.
/// Si la copie locale s'écarte du texte du professeur alors que rien n'est en attente, elle
/// s'aligne sur lui (filet de sécurité).
/// </summary>
public sealed class SharedDocumentClient
{
    private List<TextSplice> _outstanding = new(); // envoyées, sans accusé
    private List<TextSplice> _buffer = new();      // frappées depuis
    private int _seq, _outSeq;

    public string GroupId { get; private set; } = "";
    public string Text { get; private set; } = "";
    public int Revision { get; private set; }
    public bool HasPending => _outstanding.Count > 0 || _buffer.Count > 0;

    public void Reset(string groupId, string text, int revision)
    {
        GroupId = groupId; Text = text; Revision = revision;
        _outstanding = new(); _buffer = new(); _outSeq = 0;
    }

    /// <summary>Frappe locale (déjà visible dans la zone de texte).</summary>
    public void LocalEdit(TextSplice op)
    {
        if (op.IsEmpty) return;
        Text = op.ApplyTo(Text);
        // Fusionne les frappes consécutives pour limiter le nombre d'opérations.
        if (_buffer.Count > 0)
        {
            var last = _buffer[^1];
            if (op.Del == 0 && last.Del == 0 && op.Pos == last.Pos + last.Ins.Length)
            { _buffer[^1] = last with { Ins = last.Ins + op.Ins }; return; }
        }
        _buffer.Add(op);
    }

    /// <summary>Message à envoyer, ou null si une série est déjà en vol ou si rien n'a changé.</summary>
    public GroupDocPayload? TakeOutgoing()
    {
        if (_outstanding.Count > 0 || _buffer.Count == 0) return null;
        _outstanding = _buffer;
        _buffer = new();
        _outSeq = ++_seq;
        return new GroupDocPayload
        {
            GroupId = GroupId, Revision = Revision, Seq = _outSeq,
            Ops = _outstanding.Select(SpliceDto.From).ToList(),
        };
    }

    /// <summary>
    /// Message du professeur. Renvoie les modifications appliquées à la copie locale
    /// (pour déplacer le curseur), vide s'il n'y en a pas.
    /// </summary>
    public List<TextSplice> Receive(GroupDocPayload p)
    {
        var applied = new List<TextSplice>();
        if (p.GroupId != GroupId) return applied;

        if (p.AckSeq != 0 && p.AckSeq == _outSeq)
        {
            _outstanding = new();
            _outSeq = 0;
            Revision = p.Revision;
        }
        else
        {
            foreach (var dto in p.Ops)
            {
                var h = dto.ToSplice();
                for (int i = 0; i < _outstanding.Count; i++)
                {
                    var c = _outstanding[i];
                    _outstanding[i] = c.TransformAgainst(h, thisFirst: false);
                    h = h.TransformAgainst(c, thisFirst: true);
                }
                for (int i = 0; i < _buffer.Count; i++)
                {
                    var c = _buffer[i];
                    _buffer[i] = c.TransformAgainst(h, thisFirst: false);
                    h = h.TransformAgainst(c, thisFirst: true);
                }
                Text = h.ApplyTo(Text);
                applied.Add(h);
            }
            Revision = p.Revision;
        }

        // Filet de sécurité : sans frappe en attente, la copie doit être identique au texte du professeur.
        if (!HasPending && p.Text != null && Text != p.Text)
        {
            applied.Add(TextSplice.Diff(Text, p.Text));
            Text = p.Text;
        }
        return applied;
    }
}
