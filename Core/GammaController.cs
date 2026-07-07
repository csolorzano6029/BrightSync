using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>
/// Brillo y temperatura de color por SOFTWARE mediante la rampa de gamma (gdi32).
/// Funciona en cualquier pantalla. Es el fallback cuando no hay control de hardware,
/// y el único mecanismo capaz de aplicar temperatura de color.
///
/// Nota: se usa un array PLANO ushort[768] (no un struct con ByValArray) porque el
/// marshaling del struct hace que SetDeviceGammaRamp devuelva false en algunos equipos.
/// </summary>
public static class GammaController
{
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ushort[] lpRamp);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, ushort[] lpRamp);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    private static ushort[]? _lastApplied;

    /// <summary>
    /// True si la última llamada a Apply tuvo que degradarse porque Windows rechazó
    /// la rampa deseada (clamp GdiIcmGammaRange). Útil para avisar al usuario.
    /// </summary>
    public static bool LastWasClamped { get; private set; }

    /// <summary>Aplica brillo (0–100) y temperatura (Kelvin, 3000–6500).</summary>
    public static bool Apply(int brightnessPercent, int temperatureK)
    {
        double b = Math.Clamp(brightnessPercent, 5, 100) / 100.0; // nunca a negro total
        var (rGain, gGain, bGain) = KelvinToRgbGain(temperatureK);

        var target = new ushort[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            double baseVal = i / 255.0 * 65535.0;
            target[i]       = Scale(baseVal * b * rGain);
            target[256 + i] = Scale(baseVal * b * gGain);
            target[512 + i] = Scale(baseVal * b * bGain);
        }
        return ApplyWithFallback(target);
    }

    /// <summary>
    /// Atenúa por gamma con los tres canales IGUALES (sin temperatura), que Windows
    /// siempre acepta hasta ~50 %. La gamma se aplica en scanout, así que ESTO SÍ
    /// atenúa el cursor del ratón (a diferencia de la matriz de color). scale 0..1.
    /// </summary>
    public static bool ApplyBrightnessScale(double scale)
    {
        scale = Math.Clamp(scale, 0.0, 1.0);
        var target = new ushort[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            ushort v = Scale(i / 255.0 * 65535.0 * scale);
            target[i] = v; target[256 + i] = v; target[512 + i] = v;
        }
        return ApplyWithFallback(target);
    }

    private static ushort[] BuildIdentity()
    {
        var id = new ushort[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            ushort v = Scale(i / 255.0 * 65535.0);
            id[i] = v; id[256 + i] = v; id[512 + i] = v;
        }
        return id;
    }

    /// <summary>
    /// Intenta aplicar la rampa deseada. Si Windows la rechaza (clamp de gamma),
    /// mezcla progresivamente hacia la identidad hasta que la acepte, para aplicar
    /// el efecto MÁS fuerte permitido en vez de no aplicar nada.
    /// </summary>
    private static bool ApplyWithFallback(ushort[] target)
    {
        if (SetRamp(target)) { LastWasClamped = false; return true; }

        var identity = BuildIdentity();
        for (double factor = 0.9; factor >= 0.0; factor -= 0.1)
        {
            var blended = new ushort[256 * 3];
            for (int k = 0; k < blended.Length; k++)
                blended[k] = (ushort)Math.Clamp(identity[k] + (target[k] - identity[k]) * factor, 0, 65535);

            if (SetRamp(blended))
            {
                LastWasClamped = true;
                Log.Write($"Gamma degradado por clamp de Windows: factor aplicado={factor:0.0}");
                return true;
            }
        }
        Log.Write("Gamma: ni siquiera la identidad se aceptó (raro).");
        return false;
    }

    private static bool SetRamp(ushort[] ramp)
    {
        // CreateDC("DISPLAY") es más fiable en apps sin ventana que GetDC(NULL).
        IntPtr hdc = CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            try
            {
                if (SetDeviceGammaRamp(hdc, ramp)) { _lastApplied = ramp; return true; }
            }
            finally { DeleteDC(hdc); }
        }

        IntPtr hdc2 = GetDC(IntPtr.Zero);
        if (hdc2 == IntPtr.Zero) return false;
        try
        {
            bool ok = SetDeviceGammaRamp(hdc2, ramp);
            if (ok) _lastApplied = ramp;
            return ok;
        }
        finally { ReleaseDC(IntPtr.Zero, hdc2); }
    }

    /// <summary>Restaura la rampa lineal (gamma neutra 1.0).</summary>
    public static void Reset()
    {
        SetRamp(BuildIdentity());
        _lastApplied = null;
    }

    /// <summary>
    /// True si la rampa actual del sistema difiere de la que aplicamos
    /// (Windows la resetea al entrar/salir de pantalla completa, RDP o reanudar).
    /// </summary>
    public static bool HasDrifted()
    {
        if (_lastApplied is null) return false;
        var current = new ushort[256 * 3];
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            if (!GetDeviceGammaRamp(hdc, current)) return false;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        int[] probes = { 64, 128, 192, 320, 384, 576, 640 };
        foreach (int p in probes)
            if (Math.Abs(current[p] - _lastApplied[p]) > 512)
                return true;
        return false;
    }

    private static ushort Scale(double v) => (ushort)Math.Clamp(v, 0, 65535);

    /// <summary>
    /// Kelvin → multiplicadores RGB (aprox. Tanner Helland). 6500K ≈ (1,1,1).
    /// Valores más bajos = más cálido (menos azul/verde).
    /// </summary>
    public static (double r, double g, double b) KelvinToRgbGain(int kelvin)
    {
        kelvin = Math.Clamp(kelvin, 1000, 6500);
        double temp = kelvin / 100.0;
        double r, g, b;

        if (temp <= 66) r = 255;
        else r = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);

        if (temp <= 66) g = 99.4708025861 * Math.Log(temp) - 161.1195681661;
        else g = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);

        if (temp >= 66) b = 255;
        else if (temp <= 19) b = 0;
        else b = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;

        return (Clamp01(r / 255.0), Clamp01(g / 255.0), Clamp01(b / 255.0));
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}
