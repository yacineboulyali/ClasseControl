using System.Diagnostics;
using Avalonia;

namespace ClasseControle.App;

/// <summary>Mode de fonctionnement de l'application unique.</summary>
public enum AppMode { None, Teacher, Student }

/// <summary>
/// Point d'entrée unique de Classe_controle.
/// Ordre de décision du mode :
///   1. ligne de commande : --prof / --teacher  ou  --eleve / --student  (--choisir pour redemander) ;
///   2. fichier « classecontrole.ini » à côté de l'exe (écrit par l'installateur) ;
///   3. choix mémorisé par l'utilisateur dans %APPDATA%\Classe_controle\mode.ini ;
///   4. sinon, fenêtre de choix au premier démarrage.
/// </summary>
public static class Program
{
    private static Mutex? _agentMutex;

    [STAThread]
    public static int Main(string[] args)
    {
        bool forceChoice = args.Any(a => Is(a, "--choisir", "--choose"));
        AppMode mode = forceChoice ? AppMode.None : ModeFromArgs(args);
        if (mode == AppMode.None && !forceChoice) mode = ModeStore.Load();

        if (mode == AppMode.None)
        {
            // Fenêtre de choix (application Avalonia dédiée), puis relance dans le mode choisi :
            // Avalonia ne peut être initialisé qu'une fois par processus.
            var chooser = AppBuilder.Configure<ChooserApp>().UsePlatformDetect().LogToTrace();
            chooser.StartWithClassicDesktopLifetime(args);
            if (ChooserApp.Choice == AppMode.None) return 0;
            return Relaunch(ChooserApp.Choice);
        }

        if (mode == AppMode.Student)
        {
            // Un seul agent par session Windows.
            _agentMutex = new Mutex(initiallyOwned: true, "ClasseControle.Agent.SingleInstance", out bool created);
            if (!created) return 0;
            // Avant un redémarrage sur une nouvelle version, l'agent libère l'instance unique.
            ClasseControle.Common.AppRuntime.ReleaseSingleInstance = () =>
            {
                try { _agentMutex?.ReleaseMutex(); } catch { }
                _agentMutex?.Dispose();
                _agentMutex = null;
            };
            return AppBuilder.Configure<ClasseControle.Agent.App>().UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }

        return AppBuilder.Configure<ClasseControle.Console.App>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private static bool Is(string arg, params string[] names) =>
        names.Any(n => string.Equals(arg.TrimStart('/'), n.TrimStart('-'), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, n, StringComparison.OrdinalIgnoreCase));

    internal static AppMode ModeFromArgs(string[] args)
    {
        if (args.Any(a => Is(a, "--prof", "--teacher", "--professeur"))) return AppMode.Teacher;
        if (args.Any(a => Is(a, "--eleve", "--élève", "--student"))) return AppMode.Student;
        return AppMode.None;
    }

    private static int Relaunch(AppMode mode)
    {
        try
        {
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClasseControle.exe");
            Process.Start(new ProcessStartInfo(exe, mode == AppMode.Teacher ? "--prof" : "--eleve") { UseShellExecute = false });
            return 0;
        }
        catch { return 1; }
    }
}

/// <summary>Lecture / écriture du mode mémorisé.</summary>
public static class ModeStore
{
    /// <summary>Fichier écrit par l'installateur (protégé dans Program Files sur les postes élèves).</summary>
    public static string InstallFile => Path.Combine(AppContext.BaseDirectory, "classecontrole.ini");

    /// <summary>Choix fait par l'utilisateur dans la fenêtre de démarrage (version portable).</summary>
    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Classe_controle", "mode.ini");

    public static AppMode Load() =>
        Read(InstallFile) is var m && m != AppMode.None ? m : Read(UserFile);

    private static AppMode Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return AppMode.None;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (!t.StartsWith("Mode", StringComparison.OrdinalIgnoreCase) || !t.Contains('=')) continue;
                var v = t[(t.IndexOf('=') + 1)..].Trim().ToLowerInvariant();
                if (v is "prof" or "teacher" or "professeur") return AppMode.Teacher;
                if (v is "eleve" or "élève" or "student") return AppMode.Student;
            }
        }
        catch { }
        return AppMode.None;
    }

    public static void SaveUserChoice(AppMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, "# Mode de Classe_controle sur ce poste (prof ou eleve)\r\nMode=" +
                (mode == AppMode.Teacher ? "prof" : "eleve") + "\r\n");
        }
        catch { }
    }
}
