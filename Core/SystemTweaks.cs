using System.Diagnostics;
using Microsoft.Win32;

namespace BrightSync.Core;

/// <summary>
/// Ajustes del sistema que requieren admin.
/// Principal: desbloquear el rango de gamma (GdiIcmGammaRange) para que
/// SetDeviceGammaRamp acepte atenuaciones y temperaturas fuertes por software.
/// Por defecto Windows limita el gamma a ~50%; con la clave a 256 se levanta el límite.
/// Requiere reiniciar sesión para surtir efecto.
/// </summary>
public static class SystemTweaks
{
    private const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";
    private const string ValueName = "GdiIcmGammaRange";

    public static bool DeepGammaEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(IcmKey);
            return key?.GetValue(ValueName) is int v && v >= 256;
        }
        catch { return false; }
    }

    /// <summary>Escribe la clave de forma elevada (UAC). Requiere reiniciar sesión.</summary>
    public static bool EnableDeepGamma()
    {
        string args = $"add \"HKLM\\{IcmKey}\" /v {ValueName} /t REG_DWORD /d 256 /f";
        return RunElevated("reg.exe", args);
    }

    public static bool DisableDeepGamma()
    {
        string args = $"delete \"HKLM\\{IcmKey}\" /v {ValueName} /f";
        return RunElevated("reg.exe", args);
    }

    private static bool RunElevated(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }
}
