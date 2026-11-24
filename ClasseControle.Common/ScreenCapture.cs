using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClasseControle.Common;

/// <summary>
/// Capture l'écran virtuel (tous moniteurs) via GDI BitBlt, redimensionne et
/// encode en JPEG. Conçu pour être appelé en boucle pour un flux d'écran.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenCapture
{
    private readonly ImageCodecInfo _jpegCodec = GetEncoder(ImageFormat.Jpeg);

    public int VirtualX => Gdi.GetSystemMetrics(Gdi.SM_XVIRTUALSCREEN);
    public int VirtualY => Gdi.GetSystemMetrics(Gdi.SM_YVIRTUALSCREEN);
    public int VirtualWidth => Math.Max(1, Gdi.GetSystemMetrics(Gdi.SM_CXVIRTUALSCREEN));
    public int VirtualHeight => Math.Max(1, Gdi.GetSystemMetrics(Gdi.SM_CYVIRTUALSCREEN));

    /// <summary>
    /// Capture une image, la redimensionne pour que la largeur n'excède pas
    /// <paramref name="maxWidth"/>, et renvoie le JPEG + dimensions.
    /// </summary>
    public (byte[] jpeg, int w, int h, int screenW, int screenH) CaptureJpeg(int maxWidth, int quality)
    {
        int sx = VirtualX, sy = VirtualY, sw = VirtualWidth, sh = VirtualHeight;

        using var full = CaptureRaw(sx, sy, sw, sh);

        int tw = sw, th = sh;
        if (maxWidth > 0 && sw > maxWidth)
        {
            double scale = (double)maxWidth / sw;
            tw = Math.Max(1, (int)(sw * scale));
            th = Math.Max(1, (int)(sh * scale));
        }

        using var target = (tw == sw && th == sh) ? null : new Bitmap(tw, th, PixelFormat.Format24bppRgb);
        Bitmap toEncode = full;
        if (target != null)
        {
            using var g = Graphics.FromImage(target);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(full, 0, 0, tw, th);
            toEncode = target;
        }

        using var ms = new MemoryStream();
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        toEncode.Save(ms, _jpegCodec, p);
        return (ms.ToArray(), tw, th, sw, sh);
    }

    private Bitmap CaptureRaw(int x, int y, int w, int h)
    {
        IntPtr hDesktop = Gdi.GetDesktopWindow();
        IntPtr hSrc = Gdi.GetWindowDC(hDesktop);
        IntPtr hMem = Gdi.CreateCompatibleDC(hSrc);
        IntPtr hBmp = Gdi.CreateCompatibleBitmap(hSrc, w, h);
        IntPtr hOld = Gdi.SelectObject(hMem, hBmp);
        try
        {
            Gdi.BitBlt(hMem, 0, 0, w, h, hSrc, x, y, Gdi.SRCCOPY | Gdi.CAPTUREBLT);
            DrawCursor(hMem, x, y);
            return Image.FromHbitmap(hBmp);
        }
        finally
        {
            Gdi.SelectObject(hMem, hOld);
            Gdi.DeleteObject(hBmp);
            Gdi.DeleteDC(hMem);
            Gdi.ReleaseDC(hDesktop, hSrc);
        }
    }

    /// <summary>Dessine le curseur de la souris dans le DC capturé.</summary>
    private static void DrawCursor(IntPtr hdc, int originX, int originY)
    {
        var ci = new Gdi.CURSORINFO { cbSize = Marshal.SizeOf<Gdi.CURSORINFO>() };
        if (Gdi.GetCursorInfo(ref ci) && (ci.flags & Gdi.CURSOR_SHOWING) != 0)
        {
            int cx = ci.ptScreenPos.X - originX;
            int cy = ci.ptScreenPos.Y - originY;
            Gdi.DrawIconEx(hdc, cx, cy, ci.hCursor, 0, 0, 0, IntPtr.Zero, Gdi.DI_NORMAL);
        }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid) return codec;
        throw new InvalidOperationException("Encodeur JPEG introuvable");
    }

    /// <summary>Sous-ensemble des API Win32 nécessaires à la capture.</summary>
    private static class Gdi
    {
        public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77,
                         SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
        public const int CURSOR_SHOWING = 0x00000001, DI_NORMAL = 0x0003;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
                                                                 IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
                                                                      int cx, int cy, int step, IntPtr brush, int flags);
    }
}
