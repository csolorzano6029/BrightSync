using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrightSync.Core;

public enum ColorFilter
{
    None = 0,
    Grayscale,
    Invert,
    HighContrast,
    Protanopia,   // corrección para no ver rojo
    Deuteranopia, // corrección para no ver verde
    Tritanopia,   // corrección para no ver azul
    Personalizado // combinación Deuteranopia + Tritanopia (ajustado al usuario)
}

/// <summary>Un perfil = un conjunto completo de ajustes que se puede aplicar de una vez.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Default";

    /// <summary>0–100. Brillo objetivo.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Kelvin 3000 (cálido) – 6500 (neutro). Solo afecta a la gamma.</summary>
    public int Temperature { get; set; } = 6500;

    /// <summary>Si true intenta hardware (WMI/DDC-CI); si false o no disponible usa gamma.</summary>
    public bool PreferHardware { get; set; } = true;

    public ColorFilter Filter { get; set; } = ColorFilter.None;

    public Profile Clone() => (Profile)MemberwiseClone();
}

/// <summary>Parámetros ajustables del filtro de daltonismo personalizado.</summary>
public sealed class CustomFilterConfig
{
    /// <summary>Peso de la corrección rojo/verde (Deuteranopia), 0–100.</summary>
    public int Deuteranopia { get; set; } = 50;
    /// <summary>Peso de la corrección azul/amarillo (Tritanopia), 0–100.</summary>
    public int Tritanopia { get; set; } = 50;
    /// <summary>Intensidad global de la corrección, 0–150 (100 = normal).</summary>
    public int Intensity { get; set; } = 100;

    public void ResetToDefault() { Deuteranopia = 50; Tritanopia = 50; Intensity = 100; }
}

public sealed class AutoDimConfig
{
    public bool Enabled { get; set; } = false;
    public int MinBrightness { get; set; } = 30;
    public int MaxBrightness { get; set; } = 100;
    /// <summary>Cada cuánto se muestrea la pantalla.</summary>
    public int SampleIntervalMs { get; set; } = 1500;
    /// <summary>Suavizado 0.05–1.0: más bajo = transición más lenta.</summary>
    public double Smoothing { get; set; } = 0.25;
}

public sealed class HotkeyBinding
{
    /// <summary>Acción: BrightnessUp, BrightnessDown, TempWarmer, TempCooler, NextProfile, CycleFilter, o "Profile:NombrePerfil".</summary>
    public string Action { get; set; } = "";
    /// <summary>Combinación de Ctrl/Alt/Shift/Win separada por '+', p.ej. "Ctrl+Alt".</summary>
    public string Modifiers { get; set; } = "Ctrl+Alt";
    /// <summary>Tecla, p.ej. "PageUp", "Up", "F9".</summary>
    public string Key { get; set; } = "";
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>Reaplica la gamma periódicamente por si Windows la resetea (pantalla completa, RDP, reanudar).</summary>
    public bool ReapplyOnDrift { get; set; } = true;
    public int ReapplyIntervalSeconds { get; set; } = 5;

    public string ActiveProfile { get; set; } = "Default";

    public List<Profile> Profiles { get; set; } = new() { new Profile() };

    public AutoDimConfig AutoDim { get; set; } = new();

    public CustomFilterConfig CustomFilter { get; set; } = new();

    public List<HotkeyBinding> Hotkeys { get; set; } = new()
    {
        new HotkeyBinding { Action = "BrightnessUp",   Modifiers = "Ctrl+Alt", Key = "PageUp" },
        new HotkeyBinding { Action = "BrightnessDown", Modifiers = "Ctrl+Alt", Key = "PageDown" },
        new HotkeyBinding { Action = "NextProfile",    Modifiers = "Ctrl+Alt", Key = "P" },
        new HotkeyBinding { Action = "CycleFilter",    Modifiers = "Ctrl+Alt", Key = "F" },
    };

    [JsonIgnore]
    public Profile Current =>
        Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles.First();
}

/// <summary>Carga/guarda la config en %AppData%\BrightSync\config.json de forma atómica.</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrightSync");

    public static string FilePath => Path.Combine(Directory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg is not null)
                {
                    if (cfg.Profiles.Count == 0) cfg.Profiles.Add(new Profile());
                    Log.Write($"Config cargada: perfil='{cfg.ActiveProfile}', brillo={cfg.Current.Brightness}, temp={cfg.Current.Temperature}, prefHW={cfg.Current.PreferHardware}");
                    return cfg;
                }
            }
            else
            {
                Log.Write("No existe config.json; usando valores por defecto.");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERROR al cargar config: {ex.Message}. Usando valores por defecto.");
        }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(cfg, Options);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, FilePath, overwrite: true);
        File.Delete(tmp);
    }
}
