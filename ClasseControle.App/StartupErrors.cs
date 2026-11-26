using System.Runtime.InteropServices;
using System.Text;

namespace ClasseControle.App;

/// <summary>
/// Si le programme ne peut pas démarrer (fichier bloqué par une stratégie de contrôle des
/// applications, bibliothèque manquante…), il l'explique dans une boîte de dialogue claire
/// et l'enregistre dans %LocalAppData%\Classe_controle\erreurs.log, au lieu de se fermer
/// sans rien dire.
/// </summary>
internal static class StartupErrors
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000;

    public static void Report(Exception ex)
    {
        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Classe_controle", "erreurs.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\r\n\r\n", Encoding.UTF8);
        }
        catch { }

        bool blocked = IsApplicationControlBlock(ex);
        string text = blocked
            ? "Windows a bloqué une partie de Classe_controle (erreur 4551 : stratégie de contrôle des applications).\n\n" +
              "• PC personnel sous Windows 11 : Sécurité Windows › Contrôle des applications et du navigateur › " +
              "Smart App Control bloque les programmes non signés.\n" +
              "• PC de l'établissement : demandez au service informatique d'autoriser Classe_controle " +
              "(dossier C:\\Program Files\\Classe_controle).\n\n" +
              "Détails : " + log
            : "Classe_controle n'a pas pu démarrer :\n\n" + ex.GetBaseException().Message + "\n\nDétails : " + log;
        try { MessageBoxW(IntPtr.Zero, text, "Classe_controle", (blocked ? MB_ICONWARNING : MB_ICONERROR) | MB_TOPMOST); } catch { }
    }

    /// <summary>Erreur 4551 (ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION, HRESULT 0x800711C7) quelque part dans la chaîne.</summary>
    private static bool IsApplicationControlBlock(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.HResult == unchecked((int)0x800711C7) || e.HResult == 4551) return true;
            string m = e.Message;
            if (m.Contains("4551") || m.Contains("contrôle d'application", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("Application Control", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
