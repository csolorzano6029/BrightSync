using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>
/// Registra atajos de teclado globales (funcionan aunque la app no tenga foco)
/// mediante una ventana-mensaje oculta y RegisterHotKey.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 1;

    /// <summary>Se dispara con el string de acción del atajo pulsado.</summary>
    public event Action<string>? Triggered;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>Registra la lista de atajos. Devuelve los que no se pudieron registrar.</summary>
    public List<HotkeyBinding> Register(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        var failed = new List<HotkeyBinding>();

        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Key) || string.IsNullOrWhiteSpace(b.Action))
                continue;

            uint mods = ParseModifiers(b.Modifiers) | MOD_NOREPEAT;
            if (!TryParseVk(b.Key, out uint vk)) { failed.Add(b); continue; }

            int id = _nextId++;
            if (RegisterHotKey(Handle, id, mods, vk))
                _actions[id] = b.Action;
            else
                failed.Add(b);
        }
        return failed;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (_actions.TryGetValue(id, out var action))
                Triggered?.Invoke(action);
        }
        base.WndProc(ref m);
    }

    private static uint ParseModifiers(string mods)
    {
        uint result = 0;
        if (string.IsNullOrWhiteSpace(mods)) return result;
        foreach (var part in mods.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": result |= MOD_CONTROL; break;
                case "alt": result |= MOD_ALT; break;
                case "shift": result |= MOD_SHIFT; break;
                case "win": case "windows": result |= MOD_WIN; break;
            }
        }
        return result;
    }

    private static bool TryParseVk(string key, out uint vk)
    {
        vk = 0;
        if (Enum.TryParse<Keys>(key, ignoreCase: true, out var k))
        {
            vk = (uint)k;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
