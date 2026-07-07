using System.IO;

namespace BrightSync.Core;

/// <summary>Log de diagnóstico simple a %AppData%\BrightSync\log.txt.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(ConfigStore.Directory, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.Directory);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* el log nunca debe romper la app */ }
    }
}
