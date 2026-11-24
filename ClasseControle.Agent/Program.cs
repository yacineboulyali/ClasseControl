using System.Runtime.Versioning;
using System.Threading;
using Avalonia;

namespace ClasseControle.Agent;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // Empêche plusieurs instances de l'agent sur le même poste.
        _mutex = new Mutex(initiallyOwned: true, "ClasseControle.Agent.SingleInstance", out bool created);
        if (!created) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
