using System.Runtime.Versioning;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Injecte les entrées souris/clavier reçues du professeur (contrôle à distance)
/// via SendInput. Les coordonnées sont normalisées (0..1) sur l'écran virtuel.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InputInjector
{
    public static void Apply(RemoteInputPayload p)
    {
        switch (p.Kind)
        {
            case InputKind.MouseMove:
                MoveMouse(p.X, p.Y);
                break;
            case InputKind.MouseDown:
                MouseButton(p.Button, down: true);
                break;
            case InputKind.MouseUp:
                MouseButton(p.Button, down: false);
                break;
            case InputKind.MouseWheel:
                Wheel(p.Wheel);
                break;
            case InputKind.KeyDown:
                Key((ushort)p.KeyCode, down: true);
                break;
            case InputKind.KeyUp:
                Key((ushort)p.KeyCode, down: false);
                break;
        }
    }

    private static void MoveMouse(double nx, double ny)
    {
        // SendInput en mode ABSOLUTE attend des coordonnées 0..65535 sur l'écran
        // virtuel (avec le flag VIRTUALDESK).
        int ax = (int)Math.Round(Math.Clamp(nx, 0, 1) * 65535);
        int ay = (int)Math.Round(Math.Clamp(ny, 0, 1) * 65535);
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            U = new Native.InputUnion
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = ax,
                    dy = ay,
                    dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK,
                }
            }
        });
    }

    private static void MouseButton(int button, bool down)
    {
        uint flag = button switch
        {
            1 => down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP,
            2 => down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP,
            _ => down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP,
        };
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = flag } }
        });
    }

    private static void Wheel(int delta)
    {
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            U = new Native.InputUnion
            {
                mi = new Native.MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = Native.MOUSEEVENTF_WHEEL }
            }
        });
    }

    private static void Key(ushort vk, bool down)
    {
        Send(new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = down ? 0 : Native.KEYEVENTF_KEYUP,
                }
            }
        });
    }

    private static void Send(Native.INPUT input)
    {
        var arr = new[] { input };
        Native.SendInput(1, arr, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }
}
