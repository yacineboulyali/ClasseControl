namespace ClasseControle.Common;

/// <summary>
/// Document partagé d'un groupe, côté professeur (qui fait office de serveur).
/// Chaque modification reçue d'un élève est exprimée par rapport à une révision
/// de base ; si d'autres modifications ont été acceptées entre-temps, elle est
/// transformée pour ne pas les écraser, puis appliquée. Thread-safe.
/// </summary>
public sealed class SharedDocument
{
    private const int HistoryLimit = 500;
    private readonly object _gate = new();
    // Révision r : texte obtenu après application de _ops[r - _firstRev].
    private readonly List<(string text, TextSplice op)> _history = new();
    private int _firstRev = 1;

    public string Text { get; private set; } = "";
    public int Revision { get; private set; }

    private readonly string _initial;

    public SharedDocument(string initial = "")
    {
        Text = _initial = initial;
    }

    /// <summary>Intègre le texte complet proposé par un élève sur la révision <paramref name="baseRevision"/>.</summary>
    public (string text, int revision) Submit(int baseRevision, string proposed)
    {
        lock (_gate)
        {
            string? baseText = TextAt(baseRevision);
            TextSplice op;
            if (baseText == null)
            {
                // Révision trop ancienne ou inconnue : dernier écrit gagne.
                op = TextSplice.Diff(Text, proposed);
            }
            else
            {
                op = TextSplice.Diff(baseText, proposed);
                for (int r = baseRevision + 1; r <= Revision; r++)
                    op = op.TransformAgainst(_history[r - _firstRev].op);
            }
            if (op.IsEmpty) return (Text, Revision);

            Text = op.ApplyTo(Text);
            Revision++;
            _history.Add((Text, op));
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
                _firstRev++;
            }
            return (Text, Revision);
        }
    }

    private string? TextAt(int rev)
    {
        if (rev == Revision) return Text;
        if (rev == 0 && _firstRev == 1) return _initial;
        int i = rev - _firstRev;
        return i >= 0 && i < _history.Count ? _history[i].text : null;
    }
}
