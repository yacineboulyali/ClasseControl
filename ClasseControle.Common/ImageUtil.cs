using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace ClasseControle.Common;

[SupportedOSPlatform("windows")]
public static class ImageUtil
{
    /// <summary>
    /// Recompresse une image quelconque (PNG, JPEG, BMP…) en JPEG d'au plus
    /// <paramref name="maxWidth"/> pixels de large, pour l'envoyer sur le réseau.
    /// </summary>
    public static byte[] ToJpeg(byte[] source, int maxWidth = 1920, int quality = 80)
    {
        using var input = new MemoryStream(source);
        using var img = Image.FromStream(input);
        int w = img.Width, h = img.Height;
        if (w > maxWidth) { h = (int)(h * (double)maxWidth / w); w = maxWidth; }

        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Black);
            g.DrawImage(img, 0, 0, w, h);
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        using var output = new MemoryStream();
        bmp.Save(output, codec, p);
        return output.ToArray();
    }
}
