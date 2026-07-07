using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>Genera el icono de la app (un sol de brillo) por código, sin recursos externos.</summary>
public static class IconFactory
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateAppIcon(int size = 32)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float cx = size / 2f, cy = size / 2f;
            float bodyR = size * 0.22f;
            float rayIn = size * 0.32f, rayOut = size * 0.46f;

            // Rayos
            using var rayPen = new Pen(Color.FromArgb(255, 255, 179, 0), Math.Max(2f, size * 0.075f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                g.DrawLine(rayPen, cx + dx * rayIn, cy + dy * rayIn, cx + dx * rayOut, cy + dy * rayOut);
            }

            // Cuerpo del sol con degradado cálido
            var rect = new RectangleF(cx - bodyR, cy - bodyR, bodyR * 2, bodyR * 2);
            using var body = new LinearGradientBrush(rect,
                Color.FromArgb(255, 255, 213, 79), Color.FromArgb(255, 255, 143, 0), 55f);
            g.FillEllipse(body, rect);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone(); // copia independiente del handle GDI
        }
        finally
        {
            DestroyIcon(h);
        }
    }
}
