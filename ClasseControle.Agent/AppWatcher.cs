using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Liste les applications ouvertes (fenêtres visibles) de la session de l'élève, avec
/// leur nom lisible, le titre de la fenêtre, l'icône, et celle qui est au premier plan.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AppWatcher
{
    // Processus d'arrière-plan à ne pas montrer comme « applications ».
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClasseControle", "ClasseControle.Agent", "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "SearchApp", "LockApp", "SystemSettingsBroker", "dwm", "ctfmon",
    };

    private static readonly Dictionary<string, (string name, byte[]? icon)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static List<AppInfo> Snapshot()
    {
        var list = new List<AppInfo>();
        int session = Process.GetCurrentProcess().SessionId;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint fgPid);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session || p.MainWindowHandle == IntPtr.Zero) continue;
                string title = p.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title) || Hidden.Contains(p.ProcessName)) continue;
                var (name, icon) = Describe(p);
                list.Add(new AppInfo
                {
                    ProcessName = p.ProcessName,
                    Name = name,
                    Title = title,
                    IsActive = p.Id == fgPid,
                    Icon = icon,
                });
            }
            catch { }
            finally { p.Dispose(); }
        }
        // L'application active d'abord, puis par nom.
        return list.OrderByDescending(a => a.IsActive).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string name, byte[]? icon) Describe(Process p)
    {
        string? path = null;
        try { path = p.MainModule?.FileName; } catch { /* processus protégé (administrateur) */ }
        string key = path ?? p.ProcessName;
        if (Cache.TryGetValue(key, out var hit)) return hit;

        string name = p.ProcessName;
        byte[]? icon = null;
        if (path != null)
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
            }
            catch { }
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(path);
                if (ico != null)
                {
                    using var bmp = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawIcon(ico, new Rectangle(0, 0, 32, 32));
                    }
                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    icon = ms.ToArray();
                }
            }
            catch { }
        }
        return Cache[key] = (name, icon);
    }
}
