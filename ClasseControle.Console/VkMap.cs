using Avalonia.Input;

namespace ClasseControle.Console;

/// <summary>
/// Conversion des touches Avalonia vers les codes de touches virtuelles Windows
/// (VK) pour le contrôle à distance du clavier. Couverture des touches usuelles.
/// </summary>
internal static class VkMap
{
    public static int ToVk(Key k)
    {
        // Lettres A..Z -> 0x41..0x5A
        if (k >= Key.A && k <= Key.Z) return 0x41 + (k - Key.A);
        // Chiffres rangée du haut D0..D9 -> 0x30..0x39
        if (k >= Key.D0 && k <= Key.D9) return 0x30 + (k - Key.D0);
        // Pavé numérique NumPad0..9 -> 0x60..0x69
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return 0x60 + (k - Key.NumPad0);
        // Fonctions F1..F24 -> 0x70..0x87
        if (k >= Key.F1 && k <= Key.F24) return 0x70 + (k - Key.F1);

        return k switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.LeftShift => 0xA0,
            Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2,
            Key.RightCtrl => 0xA3,
            Key.LeftAlt => 0xA4,
            Key.RightAlt => 0xA5,
            Key.CapsLock => 0x14,
            Key.LWin => 0x5B,
            Key.RWin => 0x5C,
            Key.OemMinus => 0xBD,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemSemicolon => 0xBA,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemCloseBrackets => 0xDD,
            Key.OemPipe => 0xDC,
            Key.OemQuotes => 0xDE,
            Key.OemBackslash => 0xE2,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            _ => 0,
        };
    }
}
