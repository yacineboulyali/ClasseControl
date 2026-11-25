using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClasseControle.Agent;

/// <summary>
/// Bloque le clavier et la souris PHYSIQUES de l'élève grâce aux crochets système
/// bas niveau (WH_KEYBOARD_LL / WH_MOUSE_LL), sans droits administrateur.
/// Les entrées « injectées » (prise de contrôle du professeur via SendInput) passent :
/// le professeur peut continuer à piloter le poste pendant le blocage.
/// Le blocage est actif tant qu'au moins une raison est posée (« prof », « verrouillage »,
/// « écran noir », « diffusion »). Ctrl+Alt+Suppr reste géré par Windows.
/// Doit être appelé sur le thread de l'interface (qui pompe les messages Windows).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InputBlocker
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Les délégués doivent rester vivants tant que les crochets sont posés.
    private static readonly LowLevelProc KeyboardProc = OnKeyboard;
    private static readonly LowLevelProc MouseProc = OnMouse;
    private static readonly HashSet<string> Reasons = new();
    private static IntPtr _kbHook, _mouseHook;

    public static bool IsBlocked => Reasons.Count > 0;
    public static bool Has(string reason) => Reasons.Contains(reason);

    public static void Set(string reason, bool on)
    {
        if (on) Reasons.Add(reason); else Reasons.Remove(reason);
        if (IsBlocked) Install(); else Uninstall();
    }

    public static void ReleaseAll()
    {
        Reasons.Clear();
        Uninstall();
    }

    private static void Install()
    {
        IntPtr module = GetModuleHandle(null);
        if (_kbHook == IntPtr.Zero) _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, KeyboardProc, module, 0);
        if (_mouseHook == IntPtr.Zero) _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, MouseProc, module, 0);
    }

    private static void Uninstall()
    {
        if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    // KBDLLHOOKSTRUCT : vkCode(0) scanCode(4) flags(8) …
    private static IntPtr OnKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsBlocked && (Marshal.ReadInt32(lParam, 8) & LLKHF_INJECTED) == 0)
            return (IntPtr)1; // avalé
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // MSLLHOOKSTRUCT : pt(0, 8 octets) mouseData(8) flags(12) …
    private static IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsBlocked && (Marshal.ReadInt32(lParam, 12) & LLMHF_INJECTED) == 0)
            return (IntPtr)1;
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
