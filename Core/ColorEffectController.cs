using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>
/// Efecto de color a pantalla completa vía Magnification API (MagSetFullscreenColorEffect,
/// una matriz 5x5 aplicada por DWM). Compone en UNA sola matriz, y en el orden correcto:
///   color real → filtro (accesibilidad) → temperatura → atenuación de brillo.
/// Así invertir/contraste funcionan aunque la pantalla esté muy atenuada, la temperatura
/// cálida no choca con el límite de gamma de Windows, y no hace falta un veil aparte.
///
/// Debe usarse desde el hilo de UI (necesita bucle de mensajes).
/// </summary>
public static class ColorEffectController
{
    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGCOLOREFFECT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] transform; // 5x5, convención vector-fila: out = [r g b a 1] * M
    }

    private static bool _initialized;
    private static bool _initTried;

    /// <summary>True si Magnification está disponible (puede aplicar efectos).</summary>
    public static bool Available
    {
        get
        {
            if (!_initTried)
            {
                _initTried = true;
                _initialized = MagInitialize();
            }
            return _initialized;
        }
    }

    public static void Shutdown()
    {
        if (_initialized)
        {
            Clear();
            MagUninitialize();
            _initialized = false;
        }
    }

    public static bool Clear() => ApplyMatrix(ToFloat(Identity()));

    /// <summary>
    /// Aplica en una sola matriz: filtro + temperatura (ganancias RGB) + escala de brillo.
    /// brightnessScale 0..1 (1 = sin atenuar por matriz). Devuelve false si no hay Magnification.
    /// </summary>
    public static bool ApplyState(double brightnessScale, double rGain, double gGain, double bGain, ColorFilter filter,
        double customD = 0.5, double customT = 0.5, double customI = 1.0)
    {
        if (!Available) return false;

        double[] f = FilterMatrix(filter, customD, customT, customI);
        double[] t = Diagonal(rGain, gGain, bGain);
        double[] b = Diagonal(brightnessScale, brightnessScale, brightnessScale);

        // M = F * T * B  (el color fluye: in * F * T * B)
        double[] m = Mul5(Mul5(f, t), b);
        return ApplyMatrix(ToFloat(m));
    }

    private static bool ApplyMatrix(float[] m)
    {
        if (!Available) return false;
        var effect = new MAGCOLOREFFECT { transform = m };
        return MagSetFullscreenColorEffect(ref effect);
    }

    // ---------- Matrices de filtro (double[25], vector-fila) ----------

    private static double[] FilterMatrix(ColorFilter filter, double customD, double customT, double customI) => filter switch
    {
        ColorFilter.None => Identity(),
        ColorFilter.Grayscale => new double[]
        {
            0.299,0.299,0.299,0,0,
            0.587,0.587,0.587,0,0,
            0.114,0.114,0.114,0,0,
            0,0,0,1,0,
            0,0,0,0,1
        },
        ColorFilter.Invert => new double[]
        {
            -1,0,0,0,0,
            0,-1,0,0,0,
            0,0,-1,0,0,
            0,0,0,1,0,
            1,1,1,0,1
        },
        ColorFilter.HighContrast => Contrast(1.7),
        ColorFilter.Protanopia => Embed3(Daltonize(Sim.Protanopia)),
        ColorFilter.Deuteranopia => Embed3(Daltonize(Sim.Deuteranopia)),
        ColorFilter.Tritanopia => Embed3(Daltonize(Sim.Tritanopia)),
        ColorFilter.Personalizado => Embed3(CustomCorrection(customD, customT, customI)),
        _ => Identity()
    };

    /// <summary>
    /// Corrección personalizada: I + intensidad·(pesoDeuter·ΔDeuter + pesoTrit·ΔTrit),
    /// donde Δ es la desviación de cada corrección respecto a la identidad.
    /// Con pesos 0.5/0.5 e intensidad 1.0 equivale al promedio Deuteranopia+Tritanopia.
    /// </summary>
    private static double[,] CustomCorrection(double wD, double wT, double intensity)
    {
        var dD = Sub3(Daltonize(Sim.Deuteranopia), Id3());
        var dT = Sub3(Daltonize(Sim.Tritanopia), Id3());
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = Id3()[i, j] + intensity * (wD * dD[i, j] + wT * dT[i, j]);
        return r;
    }

    private static double[] Contrast(double c)
    {
        double tr = 0.5 * (1.0 - c);
        return new double[]
        {
            c,0,0,0,0,
            0,c,0,0,0,
            0,0,c,0,0,
            0,0,0,1,0,
            tr,tr,tr,0,1
        };
    }

    // ---------- Utilidades de matriz 5x5 ----------

    private static double[] Identity() => new double[]
    {
        1,0,0,0,0,
        0,1,0,0,0,
        0,0,1,0,0,
        0,0,0,1,0,
        0,0,0,0,1
    };

    private static double[] Diagonal(double r, double g, double b) => new double[]
    {
        r,0,0,0,0,
        0,g,0,0,0,
        0,0,b,0,0,
        0,0,0,1,0,
        0,0,0,0,1
    };

    private static double[] Mul5(double[] a, double[] b)
    {
        var r = new double[25];
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                double s = 0;
                for (int k = 0; k < 5; k++) s += a[i * 5 + k] * b[k * 5 + j];
                r[i * 5 + j] = s;
            }
        return r;
    }

    private static float[] ToFloat(double[] m)
    {
        var f = new float[25];
        for (int i = 0; i < 25; i++) f[i] = (float)m[i];
        return f;
    }

    // ---------- Daltonismo (corrección aproximada) ----------
    // Simulación de Viénot (1999), convención vector-columna: out = M · [r g b]ᵀ.

    private static class Sim
    {
        public static readonly double[,] Protanopia =
        {
            {0.567, 0.433, 0.000},
            {0.558, 0.442, 0.000},
            {0.000, 0.242, 0.758}
        };
        public static readonly double[,] Deuteranopia =
        {
            {0.625, 0.375, 0.000},
            {0.700, 0.300, 0.000},
            {0.000, 0.300, 0.700}
        };
        public static readonly double[,] Tritanopia =
        {
            {0.950, 0.050, 0.000},
            {0.000, 0.433, 0.567},
            {0.000, 0.475, 0.525}
        };
    }

    private static readonly double[,] ErrorShift =
    {
        {0.0, 0.0, 0.0},
        {0.7, 1.0, 0.0},
        {0.7, 0.0, 1.0}
    };

    /// <summary>correcciónColumna = I + Shift · (I − Sim). Devuelve 3x3 (vector-columna).</summary>
    private static double[,] Daltonize(double[,] sim)
    {
        var iMinusSim = Sub3(Id3(), sim);
        var shifted = Mul3(ErrorShift, iMinusSim);
        return Add3(Id3(), shifted);
    }

    /// <summary>Convierte 3x3 (vector-columna out=M·in) a 5x5 vector-fila (T = Mᵀ).</summary>
    private static double[] Embed3(double[,] m) => new double[]
    {
        m[0,0], m[1,0], m[2,0], 0, 0,
        m[0,1], m[1,1], m[2,1], 0, 0,
        m[0,2], m[1,2], m[2,2], 0, 0,
        0,      0,      0,      1, 0,
        0,      0,      0,      0, 1
    };

    private static double[,] Id3() => new double[,] { {1,0,0},{0,1,0},{0,0,1} };
    private static double[,] Add3(double[,] a, double[,] b)
    {
        var r = new double[3,3];
        for (int i=0;i<3;i++) for (int j=0;j<3;j++) r[i,j]=a[i,j]+b[i,j];
        return r;
    }
    private static double[,] Sub3(double[,] a, double[,] b)
    {
        var r = new double[3,3];
        for (int i=0;i<3;i++) for (int j=0;j<3;j++) r[i,j]=a[i,j]-b[i,j];
        return r;
    }
    private static double[,] Mul3(double[,] a, double[,] b)
    {
        var r = new double[3,3];
        for (int i=0;i<3;i++) for (int j=0;j<3;j++)
        {
            double s=0; for (int k=0;k<3;k++) s+=a[i,k]*b[k,j];
            r[i,j]=s;
        }
        return r;
    }
}
