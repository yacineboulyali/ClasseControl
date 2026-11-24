using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace ClasseControle.Agent;

/// <summary>
/// Capture l'écran virtuel (tous moniteurs) via GDI BitBlt, redimensionne et
/// encode en JPEG. Conçu pour être appelé en boucle pour un flux d'écran.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenCapture
{
    private readonly ImageCodecInfo _jpegCodec = GetEncoder(ImageFormat.Jpeg);

    public int VirtualX => Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
    public int VirtualY => Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
    public int VirtualWidth => Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN));
    public int VirtualHeight => Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));

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
        IntPtr hDesktop = Native.GetDesktopWindow();
        IntPtr hSrc = Native.GetWindowDC(hDesktop);
        IntPtr hMem = Native.CreateCompatibleDC(hSrc);
        IntPtr hBmp = Native.CreateCompatibleBitmap(hSrc, w, h);
        IntPtr hOld = Native.SelectObject(hMem, hBmp);
        try
        {
            Native.BitBlt(hMem, 0, 0, w, h, hSrc, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
            DrawCursor(hMem, x, y);
            return Image.FromHbitmap(hBmp);
        }
        finally
        {
            Native.SelectObject(hMem, hOld);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(hMem);
            Native.ReleaseDC(hDesktop, hSrc);
        }
    }

    /// <summary>Dessine le curseur de la souris dans le DC capturé.</summary>
    private static void DrawCursor(IntPtr hdc, int originX, int originY)
    {
        var ci = new Native.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.CURSORINFO>() };
        if (Native.GetCursorInfo(ref ci) && (ci.flags & Native.CURSOR_SHOWING) != 0)
        {
            int cx = ci.ptScreenPos.X - originX;
            int cy = ci.ptScreenPos.Y - originY;
            Native.DrawIconEx(hdc, cx, cy, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid) return codec;
        throw new InvalidOperationException("Encodeur JPEG introuvable");
    }
}
