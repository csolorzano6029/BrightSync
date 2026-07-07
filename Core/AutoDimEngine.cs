using System.Drawing;
using System.Drawing.Imaging;

namespace BrightSync.Core;

/// <summary>
/// Auto-atenuación por contenido: muestrea la pantalla, calcula la luminancia media
/// y sugiere un brillo (contenido brillante → más atenuación, como el modo auto de Gammy).
/// Captura el framebuffer ANTES de la gamma, así que no hay bucle de realimentación.
/// </summary>
public sealed class AutoDimEngine : IDisposable
{
    private readonly AutoDimConfig _cfg;
    private readonly Func<double>? _overlayProvider;
    private System.Threading.Timer? _timer;
    private double _smoothed = -1;

    /// <summary>Brillo sugerido (0–100). Se invoca en un hilo del pool.</summary>
    public event Action<int>? BrightnessSuggested;

    /// <param name="overlayProvider">Devuelve la opacidad del veil actual, para descontarla
    /// de la luminancia capturada y evitar la realimentación.</param>
    public AutoDimEngine(AutoDimConfig cfg, Func<double>? overlayProvider = null)
    {
        _cfg = cfg;
        _overlayProvider = overlayProvider;
    }

    public bool Running { get; private set; }

    public void Start()
    {
        if (Running) return;
        Running = true;
        _smoothed = -1;
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, Math.Max(300, _cfg.SampleIntervalMs));
    }

    public void Stop()
    {
        Running = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        if (!Running) return;
        try
        {
            double lum = SampleScreenLuminance(); // 0..1 (incluye el veil)

            // Descontar el veil negro para no realimentar (captura = real * (1-veil)).
            double veil = _overlayProvider?.Invoke() ?? 0.0;
            if (veil > 0.001 && veil < 0.95) lum = Math.Min(1.0, lum / (1.0 - veil));

            // Contenido brillante ⇒ menos brillo. Mapa lineal a [Min,Max].
            double target = _cfg.MaxBrightness - lum * (_cfg.MaxBrightness - _cfg.MinBrightness);

            _smoothed = _smoothed < 0
                ? target
                : _smoothed + (target - _smoothed) * Math.Clamp(_cfg.Smoothing, 0.05, 1.0);

            BrightnessSuggested?.Invoke((int)Math.Round(_smoothed));
        }
        catch { /* captura puede fallar en sesiones bloqueadas; ignoramos ese tick */ }
    }

    /// <summary>Luminancia media 0..1 muestreando la pantalla principal reducida a 32x32.</summary>
    private static double SampleScreenLuminance()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        const int W = 64, H = 36;
        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        using var small = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.DrawImage(full, 0, 0, W, H);
        }

        var data = small.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        long sum = 0;
        unsafe
        {
            byte* p = (byte*)data.Scan0;
            for (int i = 0; i < W * H; i++)
            {
                byte b = p[i * 4 + 0];
                byte gr = p[i * 4 + 1];
                byte r = p[i * 4 + 2];
                // Luminancia perceptual aproximada (0..255)
                sum += (long)(0.114 * b + 0.587 * gr + 0.299 * r);
            }
        }
        small.UnlockBits(data);

        return sum / (double)(W * H) / 255.0;
    }

    public void Dispose() => Stop();
}
