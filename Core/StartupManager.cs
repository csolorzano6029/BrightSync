using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace BrightSync.Core;

/// <summary>
/// Autoarranque con Windows.
///  - Modo normal: clave Run del registro (sin admin, fiable). Arranca al iniciar sesión.
///  - Modo "arranque temprano": Tarea Programada ONLOGON, que arranca antes que las apps
///    normales, PERO su creación pide confirmación de administrador (UAC) una vez.
/// </summary>
public static class StartupManager
{
    private const string TaskName = "BrightSync";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "BrightSync";
    private const string SerializeKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>¿Está activo el autoarranque por cualquiera de los dos métodos?</summary>
    public static bool IsEnabled() => RegistryEnabled() || TaskExists();

    /// <summary>Autoarranque normal (Run key, sin admin) + quita el retraso de inicio de Windows.</summary>
    public static void Enable()
    {
        SetRegistry();
        ReduceStartupDelay();
    }

    /// <summary>
    /// Windows retrasa las apps de inicio (~10s) tras cargar el escritorio. Esta clave (HKCU,
    /// sin admin) elimina ese retraso, así la app del Run key arranca en cuanto carga Explorer.
    /// </summary>
    public static void ReduceStartupDelay()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SerializeKey);
            key?.SetValue("StartupDelayInMSec", 0, RegistryValueKind.DWord);
        }
        catch { /* ignora */ }
    }

    public static void Disable()
    {
        RemoveRegistry();
        TryDeleteTask(); // por si existía la tarea; puede requerir admin, se ignora si falla
    }

    // ---------- Modo arranque temprano (tarea programada, con elevación) ----------

    public static bool EarlyStartEnabled() => TaskExists();

    /// <summary>
    /// Crea la tarea ONLOGON de forma elevada (UAC), con prioridad alta y SIN retraso,
    /// para que arranque antes que las apps normales. Devuelve true si quedó creada.
    /// Al usar la tarea, quitamos la clave Run para no arrancar dos veces.
    /// </summary>
    public static bool EnableEarlyStart()
    {
        try
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "brightsync_task.xml");
            File.WriteAllText(xmlPath, BuildTaskXml(), System.Text.Encoding.Unicode);
            string args = $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";
            if (RunElevated("schtasks.exe", args))
            {
                RemoveRegistry();
                try { File.Delete(xmlPath); } catch { /* ignora */ }
                return TaskExists();
            }
        }
        catch { /* ignora */ }
        return false;
    }

    /// <summary>Definición XML de la tarea: al iniciar sesión, sin retraso, prioridad alta (4).</summary>
    private static string BuildTaskXml()
    {
        string exe = SecurityElement.Escape(ExePath) ?? ExePath;
        return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>BrightSync — aplica brillo/temperatura/filtro al iniciar sesión, lo antes posible.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT0S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
    </Exec>
  </Actions>
</Task>";
    }

    public static void DisableEarlyStart()
    {
        if (TaskExists())
            RunElevated("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
    }

    // ---------- Task Scheduler ----------

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"");

    private static void TryDeleteTask() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static bool RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Ejecuta un comando elevado (verbo runas → prompt de UAC).</summary>
    private static bool RunElevated(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,   // requerido para 'runas'
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            // El usuario canceló el UAC o hubo error.
            return false;
        }
    }

    // ---------- Registro (modo normal) ----------

    private static bool RegistryEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
        catch { return false; }
    }

    private static void SetRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(RunValue, $"\"{ExePath}\"");
        }
        catch { /* ignora */ }
    }

    private static void RemoveRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { /* ignora */ }
    }
}
