namespace ClasseControle.Agent;

/// <summary>
/// Paramètres de l'agent, lus/écrits dans "agent.ini" à côté de l'exécutable.
/// Format simple clé=valeur. Permet de fixer le code de salle par poste.
/// </summary>
internal sealed class AgentSettings
{
    public string RoomCode { get; set; } = "CLASSE";
    public string DisplayName { get; set; } = Environment.MachineName;

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "agent.ini");

    public static AgentSettings Load()
    {
        var s = new AgentSettings();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';')) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = t[..eq].Trim().ToLowerInvariant();
                    string val = t[(eq + 1)..].Trim();
                    switch (key)
                    {
                        case "roomcode": if (val.Length > 0) s.RoomCode = val; break;
                        case "displayname": if (val.Length > 0) s.DisplayName = val; break;
                    }
                }
            }
            else
            {
                s.Save(); // crée un fichier par défaut au premier lancement
            }
        }
        catch { /* valeurs par défaut en cas d'erreur de lecture */ }
        return s;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath,
                "# Configuration de l'agent Classe_controle\r\n" +
                $"RoomCode={RoomCode}\r\n" +
                $"DisplayName={DisplayName}\r\n");
        }
        catch { /* ignore */ }
    }
}
