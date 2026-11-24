using System.Collections.ObjectModel;
using System.Text;

namespace ClasseControle.Console;

/// <summary>Une ligne du journal des actions.</summary>
internal sealed record LogEntry(DateTime Time, string Action, string Targets, string Details)
{
    public string Display => $"{Time:HH:mm:ss}  {Action}  →  {Targets}" + (Details.Length > 0 ? $"  ({Details})" : "");
}

/// <summary>
/// Journal des actions du professeur : gardé en mémoire pour l'affichage et
/// ajouté au fil de l'eau dans Documents\Classe_controle\journal-AAAA-MM-JJ.csv
/// (une ligne par action, séparateur « ; » pour ouverture directe dans Excel).
/// </summary>
internal static class ActionLog
{
    private static readonly object Gate = new();

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Classe_controle");

    public static string TodayFile => Path.Combine(Folder, $"journal-{DateTime.Now:yyyy-MM-dd}.csv");

    public static void Add(string action, string targets, string details = "")
    {
        var e = new LogEntry(DateTime.Now, action, targets, details);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Entries.Insert(0, e);
            while (Entries.Count > 1000) Entries.RemoveAt(Entries.Count - 1);
        });

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                bool isNew = !File.Exists(TodayFile);
                using var w = new StreamWriter(TodayFile, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew));
                if (isNew) w.WriteLine("Heure;Action;Postes;Détails");
                w.WriteLine(string.Join(';', $"{e.Time:HH:mm:ss}", Csv(e.Action), Csv(e.Targets), Csv(e.Details)));
            }
            catch { /* le journal ne doit jamais bloquer une action */ }
        }
    }

    /// <summary>Résume une liste de postes : « Toute la classe (12) » ou « PC-01, PC-02 ».</summary>
    public static string Describe(IReadOnlyCollection<AgentConnection> targets, bool allSelected)
    {
        if (targets.Count == 0) return "aucun poste";
        if (allSelected) return $"Toute la classe ({targets.Count})";
        var names = targets.Select(t => t.Info.MachineName).ToList();
        return names.Count <= 4 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(4))} +{names.Count - 4}";
    }

    public static string Csv(string v) =>
        v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
