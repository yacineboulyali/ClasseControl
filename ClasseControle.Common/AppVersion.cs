using System.Diagnostics;
using System.Reflection;

namespace ClasseControle.Common;

/// <summary>
/// Version de l'application et détection d'une mise à jour installée sur le disque.
/// La mise à jour est faite par une tâche planifiée SYSTEM (ClasseControle.exe --update) qui
/// télécharge la nouvelle version depuis GitHub et remplace ClasseControle.exe ;
/// le programme en cours d'exécution s'en aperçoit ici et redémarre (ou le propose).
/// </summary>
public static class AppVersion
{
    public const string Repository = "yacineboulyali/ClasseControl";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
    public const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Nom de la tâche planifiée de mise à jour créée par l'installateur.</summary>
    public const string UpdateTask = @"\Classe_controle\MiseAJour";

    /// <summary>Version du programme en cours d'exécution (3 nombres : 1.10.0).</summary>
    public static Version Running { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version);

    public static string RunningText => Running.ToString(3);

    /// <summary>Version du fichier ClasseControle.exe actuellement sur le disque (null si illisible).</summary>
    public static Version? OnDisk()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (path == null || !File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch { return null; }
    }

    /// <summary>Une version plus récente a été installée pendant que le programme tournait.</summary>
    public static bool NewerOnDisk() => OnDisk() is { } d && d > Running;

    public static Version Normalize(Version? v) => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>« v1.10.0 » ou « 1.10.0 » → Version, sinon null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '+', '-', ' ' });
        if (cut > 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    /// <summary>Lance la tâche de mise à jour (autorisée aux utilisateurs par l'installateur).</summary>
    public static bool StartUpdateTask(out string message)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", $"/Run /TN \"{UpdateTask}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p == null) { message = "schtasks introuvable"; return false; }
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            if (p.ExitCode == 0) { message = "recherche de mise à jour lancée"; return true; }
            message = string.IsNullOrWhiteSpace(err) ? $"code {p.ExitCode}" : err.Trim();
            return false;
        }
        catch (Exception ex) { message = ex.Message; return false; }
    }
}

/// <summary>Point d'accroche fourni par le lanceur : libère l'instance unique avant un redémarrage.</summary>
public static class AppRuntime
{
    public static Action? ReleaseSingleInstance { get; set; }
}
