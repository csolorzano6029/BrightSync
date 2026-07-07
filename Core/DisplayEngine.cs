namespace BrightSync.Core;

/// <summary>
/// Orquesta la aplicación de un perfil completo:
///  1) Brillo → hardware (WMI/DDC) si se prefiere y está disponible; si no, gamma.
///  2) Temperatura de color → siempre por gamma.
///  3) Filtro de color → Magnification API.
/// Guarda el último estado de gamma para poder reaplicarlo si Windows lo resetea.
/// </summary>
public sealed class DisplayEngine
{
    private int _lastTemperature = 6500;
    private double _lastRGain = 1, _lastGGain = 1, _lastBGain = 1;
    private double _lastDim;              // parte de la atenuación que afecta a la captura (matriz o veil)
    private double _lastGammaScale = 1.0; // atenuación por gamma (afecta al cursor)
    private ColorFilter _lastFilter = ColorFilter.None;
    private double _customD = 0.5, _customT = 0.5, _customI = 1.0; // parámetros del filtro personalizado
    private readonly OverlayDimmer? _dimmer;

    // Límite práctico de atenuación por gamma en Windows (~50%). La gamma atenúa el
    // cursor del ratón; la matriz de color, no. Por eso atenuamos por gamma hasta este
    // suelo (cursor incluido) y el resto lo hace la matriz.
    private const double GammaFloor = 0.5;

    public AppConfig Config { get; private set; }

    public DisplayEngine(AppConfig config, OverlayDimmer? dimmer = null)
    {
        Config = config;
        _dimmer = dimmer;
    }

    public bool HardwareAvailable { get; private set; } = HardwareBrightness.IsAvailable();

    /// <summary>
    /// True si la atenuación profunda se hace con la matriz de color (Magnification),
    /// que NO afecta a la captura de pantalla → la auto-atenuación no se realimenta.
    /// </summary>
    public bool UsesColorMatrix => ColorEffectController.Available;

    /// <summary>Atenuación profunda aplicada (matriz o veil). La auto-atenuación la descuenta de la captura.</summary>
    public double CurrentOverlay => _lastDim;

    /// <summary>
    /// Mapea el brillo percibido (5–100) a la atenuación profunda extra (0 = nada, ~0.88 = casi negro).
    /// El tercio superior (≥70) no atenúa por software (solo hardware); por debajo crece.
    /// </summary>
    private static double DimAmount(int brightness)
    {
        double b = Math.Clamp(brightness, 5, 100) / 100.0;
        const double knee = 0.70, maxDim = 0.88;
        if (b >= knee) return 0.0;
        return (knee - b) / knee * maxDim;
    }

    /// <summary>Aplica el perfil activo de la config.</summary>
    public void ApplyCurrentProfile() => ApplyProfile(Config.Current);

    public void ApplyProfile(Profile p)
    {
        bool hardwareUsed = false;
        if (p.PreferHardware && HardwareAvailable)
            hardwareUsed = HardwareBrightness.Apply(p.Brightness);

        (_lastRGain, _lastGGain, _lastBGain) = GammaController.KelvinToRgbGain(p.Temperature);
        _lastTemperature = p.Temperature;
        _lastFilter = p.Filter;

        var cf = Config.CustomFilter;
        _customD = cf.Deuteranopia / 100.0;
        _customT = cf.Tritanopia / 100.0;
        _customI = cf.Intensity / 100.0;

        ApplyLevels(1.0 - DimAmount(p.Brightness), p.Filter, p.Name, p.Brightness, hardwareUsed);
    }

    /// <summary>Ajusta solo el brillo percibido (auto-atenuación, sin tocar el hardware).</summary>
    public void ApplyGammaBrightness(int brightnessPercent)
        => ApplyLevels(1.0 - DimAmount(brightnessPercent), _lastFilter, "auto", brightnessPercent, false);

    /// <summary>
    /// Reparte la atenuación: gamma hasta ~50% (atenúa el cursor) + matriz para lo demás
    /// (atenúa el resto sin el límite de gamma, y lleva temperatura + filtro).
    /// </summary>
    private void ApplyLevels(double perceivedScale, ColorFilter filter, string tag, int brightness, bool hardwareUsed)
    {
        perceivedScale = Math.Clamp(perceivedScale, 0.05, 1.0);

        if (ColorEffectController.Available)
        {
            double gammaScale = Math.Clamp(perceivedScale, GammaFloor, 1.0);
            double matrixScale = perceivedScale / gammaScale; // ≤ 1

            GammaController.ApplyBrightnessScale(gammaScale); // atenúa TODO, incluido el cursor
            bool ok = ColorEffectController.ApplyState(matrixScale, _lastRGain, _lastGGain, _lastBGain, filter, _customD, _customT, _customI);

            _lastGammaScale = gammaScale;
            _dimmer?.SetDim(0);
            _lastDim = 1.0 - matrixScale; // solo la matriz afecta a la captura de pantalla
            Log.Write($"Apply '{tag}': brillo={brightness}, hwUsed={hardwareUsed}, gammaScale={gammaScale:0.00}, matrixScale={matrixScale:0.00}, matrizOk={ok}, temp={_lastTemperature}, filtro={filter}");
        }
        else
        {
            // Fallback sin Magnification: gamma (brillo+temp, con clamp) + veil. Sin filtros.
            int b = (int)Math.Round(perceivedScale * 100);
            GammaController.Apply(b, _lastTemperature);
            double veil = 1.0 - perceivedScale;
            _dimmer?.SetDim(veil);
            _lastGammaScale = perceivedScale;
            _lastDim = _dimmer != null ? veil : 0.0;
            Log.Write($"Apply '{tag}' (fallback): brillo={brightness}, veil={veil:0.00}, filtro NO disponible");
        }
    }

    /// <summary>Reaplica si el sistema reseteó la gamma (pantalla completa, reanudar, RDP…).</summary>
    public void ReapplyGammaIfDrifted()
    {
        if (!Config.ReapplyOnDrift) return;
        if (!GammaController.HasDrifted()) return;

        if (ColorEffectController.Available)
            GammaController.ApplyBrightnessScale(_lastGammaScale);
        else
            GammaController.Apply((int)Math.Round(_lastGammaScale * 100), _lastTemperature);
    }

    /// <summary>Reaplica todo (tras cambio de resolución o reanudar).</summary>
    public void Reapply() => ApplyCurrentProfile();

    // ---------- Ajustes rápidos (atajos de teclado) ----------

    public void NudgeBrightness(int delta)
    {
        var p = Config.Current;
        p.Brightness = Math.Clamp(p.Brightness + delta, 5, 100);
        ApplyProfile(p);
    }

    public void NudgeTemperature(int deltaK)
    {
        var p = Config.Current;
        p.Temperature = Math.Clamp(p.Temperature + deltaK, 3000, 6500);
        ApplyProfile(p);
    }

    public void NextProfile()
    {
        if (Config.Profiles.Count < 2) return;
        int idx = Config.Profiles.FindIndex(p => p.Name == Config.ActiveProfile);
        idx = (idx + 1) % Config.Profiles.Count;
        Config.ActiveProfile = Config.Profiles[idx].Name;
        ApplyCurrentProfile();
    }

    public void SetProfile(string name)
    {
        if (Config.Profiles.Any(p => p.Name == name))
        {
            Config.ActiveProfile = name;
            ApplyCurrentProfile();
        }
    }

    public void CycleFilter()
    {
        var p = Config.Current;
        var values = (ColorFilter[])Enum.GetValues(typeof(ColorFilter));
        int idx = Array.IndexOf(values, p.Filter);
        p.Filter = values[(idx + 1) % values.Length];
        ApplyProfile(p);
    }
}
