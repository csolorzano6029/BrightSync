using System.Management;
using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>
/// Brillo real del hardware:
///  - WMI  → panel interno de portátiles.
///  - DDC/CI (dxva2) → monitores externos compatibles.
/// Devuelve si consiguió aplicar por hardware en al menos una pantalla.
/// </summary>
public static class HardwareBrightness
{
    // ---------- WMI (panel interno) ----------

    public static bool HasWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightness");
            foreach (var _ in searcher.Get()) return true;
        }
        catch { /* no soportado */ }
        return false;
    }

    public static bool SetWmiBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            bool any = false;
            foreach (ManagementObject mo in searcher.Get())
            {
                mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                any = true;
            }
            return any;
        }
        catch { return false; }
    }

    // ---------- DDC/CI (monitores externos) ----------

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint cur, out uint max);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint brightness);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public static bool SetDdcBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var hmonitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (h, _, _, _) => { hmonitors.Add(h); return true; }, IntPtr.Zero);

        bool anySuccess = false;

        foreach (var hmon in hmonitors)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint count) || count == 0)
                continue;

            var phys = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hmon, count, phys))
                continue;

            try
            {
                foreach (var pm in phys)
                {
                    if (GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out _, out uint max))
                    {
                        uint target = (uint)(min + (max - min) * percent / 100.0);
                        if (SetMonitorBrightness(pm.hPhysicalMonitor, target))
                            anySuccess = true;
                    }
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, phys);
            }
        }
        return anySuccess;
    }

    public static bool HasDdcBrightness()
    {
        var hmonitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (h, _, _, _) => { hmonitors.Add(h); return true; }, IntPtr.Zero);

        foreach (var hmon in hmonitors)
        {
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint count) && count > 0)
            {
                var phys = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hmon, count, phys))
                {
                    try
                    {
                        foreach (var pm in phys)
                            if (GetMonitorBrightness(pm.hPhysicalMonitor, out _, out _, out _))
                                return true;
                    }
                    finally { DestroyPhysicalMonitors(count, phys); }
                }
            }
        }
        return false;
    }

    /// <summary>Aplica por hardware a todo lo posible (WMI + DDC). True si algo respondió.</summary>
    public static bool Apply(int percent)
    {
        bool wmi = SetWmiBrightness(percent);
        bool ddc = SetDdcBrightness(percent);
        return wmi || ddc;
    }

    public static bool IsAvailable() => HasWmiBrightness() || HasDdcBrightness();
}
