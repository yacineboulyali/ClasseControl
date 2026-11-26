using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClasseControle.Common;
using Microsoft.Win32;

namespace ClasseControle.App;

/// <summary>
/// Commandes d'administration intégrées à ClasseControle.exe (sans interface), utilisées par
/// l'installateur et la tâche de mise à jour. Tout passe par CE SEUL programme : aucun script
/// PowerShell à autoriser, rien n'est exécuté depuis le dossier temporaire.
///
///   --setup eleve|prof   tâche de démarrage de l'agent (élève) + règles du pare-feu
///   --setup-update       tâche planifiée SYSTEM de mise à jour automatique
///   --update             mise à jour depuis GitHub (lancée par la tâche, compte SYSTEM)
///   --remove-games       supprime les jeux préinstallés de Windows pour tous les utilisateurs
///   --cleanup            désinstallation : supprime tâches et règles du pare-feu
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Maintenance
{
    private const string AgentTask = @"\Classe_controle\Classe_controle Agent";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6C4B2F0E-3E7A-4C38-9B8E-5A1C2D3E4F10}_is1";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClasseControle.exe");
    private static string AppDir => Path.GetDirectoryName(Exe)!;
    private static string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Classe_controle");

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "--setup" or "--setup-update" or "--update" or "--remove-games" or "--cleanup";

    public static int Run(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--setup" => Setup(args.Length > 1 ? args[1] : "eleve"),
                "--setup-update" => SetupUpdateTask(),
                "--update" => Update(),
                "--remove-games" => RemoveGames(),
                "--cleanup" => Cleanup(),
                _ => 2,
            };
        }
        catch (Exception ex)
        {
            Log("maintenance.log", $"{string.Join(' ', args)} : échec — {ex.Message}");
            return 1;
        }
    }

    // =====================================================================
    //  Installation
    // =====================================================================
    private static int Setup(string role)
    {
        DeleteFirewallRules();
        if (role is "eleve" or "student")
        {
            // Tâche cachée : l'agent démarre à l'ouverture de session de chaque utilisateur
            // (groupe Utilisateurs) et redémarre s'il est arrêté.
            string xml = TaskXml(
                description: "Agent élève Classe_controle (supervision de salle)",
                triggers: "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>",
                principal: "<GroupId>S-1-5-32-545</GroupId><RunLevel>HighestAvailable</RunLevel>",
                arguments: "--eleve",
                hidden: true, timeLimit: "PT0S", restartOnFailure: true);
            RegisterTask(AgentTask, xml);
            Netsh($"advfirewall firewall add rule name=\"Classe_controle Agent (découverte UDP 5401)\" dir=in action=allow program=\"{Exe}\" protocol=UDP localport={Net.DiscoveryPort} profile=private,domain");
            Netsh($"advfirewall firewall add rule name=\"Classe_controle Agent (contrôle TCP 5402)\" dir=in action=allow program=\"{Exe}\" protocol=TCP localport={Net.ControlPort} profile=private,domain");
        }
        else
        {
            Netsh($"advfirewall firewall add rule name=\"Classe_controle Console\" dir=in action=allow program=\"{Exe}\" enable=yes profile=private,domain");
        }
        Log("maintenance.log", $"Installation du poste ({role}) : tâches et pare-feu configurés.");
        return 0;
    }

    private static int SetupUpdateTask()
    {
        string xml = TaskXml(
            description: "Mise à jour automatique de Classe_controle depuis GitHub (version officielle uniquement)",
            // Ordre imposé par le format des tâches Windows : Repetition AVANT StartBoundary.
            triggers: "<BootTrigger><Enabled>true</Enabled><Delay>PT2M</Delay></BootTrigger>" +
                      "<CalendarTrigger>" +
                      "<Repetition><Interval>PT1H</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>" +
                      "<StartBoundary>2024-01-01T00:05:00</StartBoundary><Enabled>true</Enabled>" +
                      "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>",
            principal: "<UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel>",
            arguments: "--update",
            startWhenAvailable: true, networkOnly: true, timeLimit: "PT30M");
        RegisterTask(AppVersion.UpdateTask, xml);

        // Les utilisateurs du poste peuvent LANCER la tâche (bouton de la console), pas la modifier.
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Planificateur indisponible");
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            dynamic folder = service.GetFolder(@"\Classe_controle");
            dynamic task = folder.GetTask("MiseAJour");
            task.SetSecurityDescriptor("D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;BU)", 0);
        }
        catch (Exception ex) { Log("maintenance.log", "Droits de la tâche de mise à jour : " + ex.Message); }

        Log("maintenance.log", "Tâche de mise à jour automatique configurée.");
        return 0;
    }

    private static int Cleanup()
    {
        Process("schtasks.exe", $"/Delete /TN \"{AgentTask}\" /F");
        Process("schtasks.exe", $"/Delete /TN \"{AppVersion.UpdateTask}\" /F");
        DeleteFirewallRules();
        return 0;
    }

    private static void DeleteFirewallRules()
    {
        foreach (var name in new[] { "Classe_controle Console", "Classe_controle Agent (découverte UDP 5401)", "Classe_controle Agent (contrôle TCP 5402)" })
            Netsh($"advfirewall firewall delete rule name=\"{name}\"");
    }

    /// <summary>
    /// XML d'une tâche planifiée, éléments dans l'ordre exact du format Windows (celui d'une
    /// tâche exportée) : schtasks refuse un ordre différent.
    /// </summary>
    private static string TaskXml(string description, string triggers, string principal, string arguments,
        bool hidden = false, bool startWhenAvailable = false, bool networkOnly = false,
        string timeLimit = "PT72H", bool restartOnFailure = false)
    {
        static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        static string B(bool b) => b ? "true" : "false";
        var settings = new StringBuilder()
            .Append("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>")
            .Append("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>")
            .Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>")
            .Append("<AllowHardTerminate>true</AllowHardTerminate>")
            .Append($"<StartWhenAvailable>{B(startWhenAvailable)}</StartWhenAvailable>")
            .Append($"<RunOnlyIfNetworkAvailable>{B(networkOnly)}</RunOnlyIfNetworkAvailable>")
            .Append("<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>")
            .Append("<AllowStartOnDemand>true</AllowStartOnDemand>")
            .Append("<Enabled>true</Enabled>")
            .Append($"<Hidden>{B(hidden)}</Hidden>")
            .Append("<RunOnlyIfIdle>false</RunOnlyIfIdle>")
            .Append("<WakeToRun>false</WakeToRun>")
            .Append($"<ExecutionTimeLimit>{timeLimit}</ExecutionTimeLimit>")
            .Append("<Priority>7</Priority>");
        if (restartOnFailure) settings.Append("<RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure>");

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
               "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
               $"<RegistrationInfo><Description>{X(description)}</Description></RegistrationInfo>" +
               $"<Triggers>{triggers}</Triggers>" +
               $"<Principals><Principal id=\"Author\">{principal}</Principal></Principals>" +
               $"<Settings>{settings}</Settings>" +
               $"<Actions Context=\"Author\"><Exec><Command>{X(Exe)}</Command><Arguments>{X(arguments)}</Arguments><WorkingDirectory>{X(AppDir)}</WorkingDirectory></Exec></Actions>" +
               "</Task>";
    }

    private static void RegisterTask(string name, string xml)
    {
        string file = Path.Combine(Path.GetTempPath(), $"classecontrole-task-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, xml, Encoding.Unicode); // simple fichier de données, aucun exécutable
        try
        {
            var (code, output) = Process("schtasks.exe", $"/Create /TN \"{name}\" /XML \"{file}\" /F");
            if (code != 0) throw new InvalidOperationException($"schtasks ({code}) : {output.Trim()}");
        }
        finally { try { File.Delete(file); } catch { } }
    }

    // =====================================================================
    //  Mise à jour automatique (compte SYSTEM)
    // =====================================================================
    private static int Update()
    {
        using var mutex = new Mutex(false, @"Global\ClasseControle.Update");
        if (!mutex.WaitOne(0)) return 0;
        try
        {
            foreach (var old in Directory.GetFiles(AppDir, "*.old-*"))
                try { File.Delete(old); } catch { /* encore utilisé : sera supprimé la prochaine fois */ }

            var current = AppVersion.OnDisk() ?? AppVersion.Running;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClasseControle-Updater/" + current.ToString(3));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var doc = JsonDocument.Parse(http.GetStringAsync(AppVersion.LatestApi).GetAwaiter().GetResult());
            var root = doc.RootElement;
            var latest = AppVersion.Parse(root.GetProperty("tag_name").GetString());
            if (latest == null || latest <= current) return 0;

            JsonElement? asset = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString() == "ClasseControle-portable.zip") asset = a;
            if (asset == null) { Log("update.log", $"Version {latest.ToString(3)} sans ClasseControle-portable.zip"); return 1; }

            Log("update.log", $"Mise à jour {current.ToString(3)} -> {latest.ToString(3)} : téléchargement…");
            string work = Path.Combine(AppDir, ".maj");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "paquet.zip");
            using (var s = http.GetStreamAsync(asset.Value.GetProperty("browser_download_url").GetString()).GetAwaiter().GetResult())
            using (var f = File.Create(zip)) s.CopyTo(f);

            long expected = asset.Value.GetProperty("size").GetInt64();
            if (new FileInfo(zip).Length != expected) { Log("update.log", "Taille inattendue du paquet : annulé"); return 1; }

            string extracted = Path.Combine(work, "x");
            ZipFile.ExtractToDirectory(zip, extracted);
            string? newExe = Directory.GetFiles(extracted, "ClasseControle.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (newExe == null) { Log("update.log", "ClasseControle.exe absent du paquet : annulé"); return 1; }
            var info = FileVersionInfo.GetVersionInfo(newExe);
            var fileVersion = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            if (fileVersion != latest) { Log("update.log", $"Version du fichier {fileVersion} ≠ {latest.ToString(3)} : annulé"); return 1; }

            // Remplacement fichier par fichier (programme et bibliothèques) ; un fichier en cours
            // d'utilisation peut être renommé, pas écrasé. Retour arrière en cas d'erreur.
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var done = new List<(string dest, string? backup)>();
            try
            {
                foreach (var src in Directory.GetFiles(Path.GetDirectoryName(newExe)!))
                {
                    string ext = Path.GetExtension(src).ToLowerInvariant();
                    if (ext is not (".exe" or ".dll")) continue;
                    string dest = Path.Combine(AppDir, Path.GetFileName(src));
                    string? backup = null;
                    if (File.Exists(dest)) { backup = dest + ".old-" + stamp; File.Move(dest, backup); }
                    File.Move(src, dest);
                    done.Add((dest, backup));
                }
            }
            catch
            {
                foreach (var (dest, backup) in done.AsEnumerable().Reverse())
                {
                    try { if (File.Exists(dest)) File.Delete(dest); if (backup != null) File.Move(backup, dest); } catch { }
                }
                throw;
            }
            try { Directory.Delete(work, true); } catch { }
            try { using var k = Registry.LocalMachine.OpenSubKey(UninstallKey, writable: true); k?.SetValue("DisplayVersion", latest.ToString(3)); } catch { }
            Log("update.log", $"Version {latest.ToString(3)} installée (l'agent redémarre tout seul ; la console propose de redémarrer).");
            return 0;
        }
        catch (Exception ex) { Log("update.log", "Échec : " + ex.Message); return 1; }
        finally { mutex.ReleaseMutex(); }
    }

    // =====================================================================
    //  Jeux préinstallés de Windows (tous les utilisateurs)
    // =====================================================================
    private static int RemoveGames()
    {
        // Uniquement des commandes PowerShell standard : fonctionne aussi en « mode de langage
        // restreint » imposé par les stratégies de contrôle des applications.
        string patterns = string.Join(",", RestrictionCatalog.UwpGamePackagePatterns.Select(p => "'" + p.Replace("'", "''") + "'"));
        string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            $"foreach($p in @({patterns})){{" +
            "  foreach($k in @(Get-AppxPackage -AllUsers -Name $p)){" +
            "    Remove-AppxPackage -Package $k.PackageFullName -AllUsers; Write-Output ('Supprimé pour tous les utilisateurs : ' + $k.Name) }" +
            "  foreach($v in @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $p })){" +
            "    Remove-AppxProvisionedPackage -Online -PackageName $v.PackageName | Out-Null; Write-Output ('Retiré de l''image Windows : ' + $v.DisplayName) } }";
        var (_, output) = Process(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"", timeoutMs: 600_000);
        foreach (var line in output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)) Log("remove-games.log", line);

        // Plus d'installation automatique d'applications « suggérées » (Windows Éducation / Entreprise).
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent");
            k.SetValue("DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord);
            Log("remove-games.log", "Applications suggérées par Windows désactivées.");
        }
        catch (Exception ex) { Log("remove-games.log", "Stratégie CloudContent : " + ex.Message); }
        return 0;
    }

    // =====================================================================
    //  Utilitaires
    // =====================================================================
    private static void Netsh(string args) => Process("netsh.exe", args);

    private static (int code, string output) Process(string file, string args, int timeoutMs = 60_000)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (p == null) return (-1, "");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "délai dépassé"); }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    internal static void Log(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(Path.Combine(LogDir, file), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\r\n", Encoding.UTF8);
        }
        catch { }
    }
}
