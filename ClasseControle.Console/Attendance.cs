using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace ClasseControle.Console;

/// <summary>Présence d'un poste pendant la séance.</summary>
internal sealed class AttendanceRow : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Machine { get; init; }
    public required string User { get; init; }
    public required string Ip { get; init; }
    public DateTime FirstSeen { get; init; }

    private DateTime? _leftAt;
    private bool _present;
    private int _hands;

    public bool Present { get => _present; set { _present = value; Changed(); Changed(nameof(StatusText)); } }
    public DateTime? LeftAt { get => _leftAt; set { _leftAt = value; Changed(); Changed(nameof(LeftText)); } }
    /// <summary>Nombre de fois où l'élève a levé la main.</summary>
    public int Hands { get => _hands; set { _hands = value; Changed(); } }

    public string FirstText => FirstSeen.ToString("HH:mm");
    public string LeftText => LeftAt?.ToString("HH:mm") ?? "—";
    public string StatusText => Present ? "🟢 Présent" : "🔴 Parti";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

/// <summary>
/// Liste de présence de la séance : arrivée (1re connexion), départ (dernière
/// déconnexion), mains levées. Exportable en CSV.
/// </summary>
internal static class Attendance
{
    public static DateTime SessionStart { get; } = DateTime.Now;
    public static ObservableCollection<AttendanceRow> Rows { get; } = new();

    // Toutes les méthodes sont appelées sur le thread UI.
    public static void MarkConnected(AgentConnection c)
    {
        var row = Rows.FirstOrDefault(r => r.Key == c.Key);
        if (row == null)
        {
            row = new AttendanceRow
            {
                Key = c.Key, Machine = c.Info.MachineName, User = c.Info.UserName,
                Ip = c.Info.IpAddress, FirstSeen = DateTime.Now,
            };
            Rows.Add(row);
        }
        row.Present = true;
        row.LeftAt = null;
    }

    public static void MarkDisconnected(AgentConnection c)
    {
        var row = Rows.FirstOrDefault(r => r.Key == c.Key);
        if (row == null || !row.Present) return;
        row.Present = false;
        row.LeftAt = DateTime.Now;
    }

    public static void CountHand(AgentConnection c)
    {
        var row = Rows.FirstOrDefault(r => r.Key == c.Key);
        if (row != null) row.Hands++;
    }

    public static string ExportCsv(string teacher, string room)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Séance du {SessionStart:dd/MM/yyyy} à {SessionStart:HH:mm};Professeur : {ActionLog.Csv(teacher)};Salle : {ActionLog.Csv(room)}");
        sb.AppendLine("Poste;Utilisateur Windows;Adresse IP;Arrivée;Départ;Statut;Mains levées");
        foreach (var r in Rows.OrderBy(r => r.Machine, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine(string.Join(';', ActionLog.Csv(r.Machine), ActionLog.Csv(r.User), r.Ip,
                r.FirstText, r.Present ? "" : r.LeftText, r.Present ? "Présent" : "Parti", r.Hands));
        return sb.ToString();
    }
}
