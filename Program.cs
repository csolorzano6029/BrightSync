using System.Threading;

namespace BrightSync;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        // Instancia única: el arranque por Tarea Programada + una copia manual
        // no deben pisarse. Si ya hay una instancia, salimos.
        _singleInstance = new Mutex(initiallyOwned: true, "BrightSync_SingleInstance_9F3A", out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(_singleInstance);
    }
}
