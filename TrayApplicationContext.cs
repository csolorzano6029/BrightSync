using System.Drawing;
using BrightSync.Core;
using BrightSync.UI;
using Microsoft.Win32;

namespace BrightSync;

/// <summary>
/// Contexto de aplicación sin ventana principal: solo icono en la bandeja.
/// Carga la configuración, la APLICA al arrancar (el fix principal de Gammy),
/// registra atajos, arranca la auto-atenuación y reacciona a eventos del sistema.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private readonly OverlayDimmer _dimmer;
    private readonly DisplayEngine _engine;
    private readonly HotkeyManager _hotkeys;
    private AutoDimEngine? _autoDim;
    private readonly System.Windows.Forms.Timer _driftTimer;
    private readonly Control _marshaler; // para volver al hilo de UI desde eventos del sistema
    private SettingsForm? _settings;

    public TrayApplicationContext()
    {
        var config = ConfigStore.Load();
        _dimmer = new OverlayDimmer();
        _engine = new DisplayEngine(config, _dimmer);

        // En el primer arranque dejamos el archivo escrito para que exista desde ya.
        if (!File.Exists(ConfigStore.FilePath))
            ConfigStore.Save(config);

        _marshaler = new Control();
        _ = _marshaler.Handle; // fuerza la creación del handle en el hilo de UI

        _appIcon = IconFactory.CreateAppIcon(32);
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "BrightSync",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        RebuildMenu();

        _hotkeys = new HotkeyManager();
        _hotkeys.Triggered += OnHotkey;
        _hotkeys.Register(config.Hotkeys);

        _driftTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(2, config.ReapplyIntervalSeconds) * 1000,
            Enabled = config.ReapplyOnDrift
        };
        _driftTimer.Tick += (_, _) => _engine.ReapplyGammaIfDrifted();

        // Asegurar autoarranque si el usuario lo quiere. Si no lo gestiona la tarea
        // programada, refrescamos siempre la clave Run para mantener la ruta del .exe
        // actualizada (evita rutas obsoletas si el ejecutable se mueve).
        if (config.StartWithWindows && !StartupManager.EarlyStartEnabled())
            StartupManager.Enable();

        // Reaccionar a eventos que resetean la gamma.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        StartAutoDimIfEnabled();

        // Hook de prueba: abrir Ajustes al arrancar (para verificación visual).
        if (Environment.GetEnvironmentVariable("BRIGHTSYNC_OPEN_SETTINGS") == "1")
            _marshaler.BeginInvoke(new Action(OpenSettings));

        // Aplicar la última configuración guardada EN CUANTO arranque el bucle de mensajes.
        _marshaler.BeginInvoke(new Action(() =>
        {
            _engine.ApplyCurrentProfile();
            _tray.ShowBalloonTip(1500, "BrightSync",
                $"Perfil «{config.ActiveProfile}» aplicado ({config.Current.Brightness}%).", ToolTipIcon.Info);
        }));
    }

    // ---------- Menú de la bandeja ----------

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Ajustes…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Brillo +10%", null, (_, _) => { _engine.NudgeBrightness(+10); Persist(); RebuildMenu(); });
        menu.Items.Add("Brillo −10%", null, (_, _) => { _engine.NudgeBrightness(-10); Persist(); RebuildMenu(); });

        // Perfiles
        var profiles = new ToolStripMenuItem("Perfiles");
        foreach (var p in _engine.Config.Profiles)
        {
            var item = new ToolStripMenuItem(p.Name) { Checked = p.Name == _engine.Config.ActiveProfile };
            string name = p.Name;
            item.Click += (_, _) => { _engine.SetProfile(name); Persist(); RebuildMenu(); };
            profiles.DropDownItems.Add(item);
        }
        menu.Items.Add(profiles);

        // Filtros
        var filters = new ToolStripMenuItem("Filtro de color");
        string[] names = { "Ninguno", "Escala de grises", "Invertir", "Alto contraste",
                           "Protanopia", "Deuteranopia", "Tritanopia", "Personalizado (para ti)" };
        for (int i = 0; i < names.Length; i++)
        {
            var f = (ColorFilter)i;
            var item = new ToolStripMenuItem(names[i]) { Checked = _engine.Config.Current.Filter == f };
            item.Click += (_, _) =>
            {
                var prof = _engine.Config.Current;
                prof.Filter = f;
                _engine.ApplyProfile(prof);
                Persist();
                RebuildMenu();
            };
            filters.DropDownItems.Add(item);
        }
        menu.Items.Add(filters);

        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Iniciar con Windows") { Checked = StartupManager.IsEnabled() };
        startup.Click += (_, _) =>
        {
            if (StartupManager.IsEnabled()) StartupManager.Disable();
            else StartupManager.Enable();
            _engine.Config.StartWithWindows = StartupManager.IsEnabled();
            Persist();
            RebuildMenu();
        };
        menu.Items.Add(startup);

        menu.Items.Add("Salir", null, (_, _) => ExitThread());

        UI.DarkTheme.ApplyMenu(menu);
        _tray.ContextMenuStrip = menu;
    }

    // ---------- Acciones ----------

    private void OpenSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(_engine, OnConfigChanged) { Icon = _appIcon };
        _settings.FormClosed += (_, _) => { _settings = null; RebuildMenu(); };
        _settings.Show();
    }

    private void OnHotkey(string action)
    {
        switch (action)
        {
            case "BrightnessUp":   _engine.NudgeBrightness(+10); break;
            case "BrightnessDown": _engine.NudgeBrightness(-10); break;
            case "TempWarmer":     _engine.NudgeTemperature(-250); break;
            case "TempCooler":     _engine.NudgeTemperature(+250); break;
            case "NextProfile":    _engine.NextProfile(); break;
            case "CycleFilter":    _engine.CycleFilter(); break;
            default:
                if (action.StartsWith("Profile:", StringComparison.Ordinal))
                    _engine.SetProfile(action["Profile:".Length..]);
                break;
        }
        Persist();
        RebuildMenu();
    }

    /// <summary>Llamado por SettingsForm tras guardar: re-registra atajos, auto-dim y timer.</summary>
    private void OnConfigChanged()
    {
        _hotkeys.Register(_engine.Config.Hotkeys);
        _driftTimer.Interval = Math.Max(2, _engine.Config.ReapplyIntervalSeconds) * 1000;
        _driftTimer.Enabled = _engine.Config.ReapplyOnDrift;
        RestartAutoDim();
        RebuildMenu();
    }

    private void Persist() => ConfigStore.Save(_engine.Config);

    // ---------- Auto-atenuación ----------

    private void StartAutoDimIfEnabled()
    {
        if (!_engine.Config.AutoDim.Enabled) return;
        _autoDim = new AutoDimEngine(_engine.Config.AutoDim, () => _engine.CurrentOverlay);
        _autoDim.BrightnessSuggested += b => _engine.ApplyGammaBrightness(b);
        _autoDim.Start();
    }

    private void RestartAutoDim()
    {
        _autoDim?.Dispose();
        _autoDim = null;
        StartAutoDimIfEnabled();
        // Si se desactivó, restaurar el brillo del perfil.
        if (!_engine.Config.AutoDim.Enabled)
            _engine.ApplyCurrentProfile();
    }

    // ---------- Eventos del sistema (resetean la gamma) ----------

    private void OnPowerModeChanged(object? s, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _marshaler.BeginInvoke(new Action(() => _engine.Reapply()));
    }

    private void OnDisplaySettingsChanged(object? s, EventArgs e)
        => _marshaler.BeginInvoke(new Action(() => _engine.Reapply()));

    private void OnSessionSwitch(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
            _marshaler.BeginInvoke(new Action(() => _engine.Reapply()));
    }

    // ---------- Limpieza ----------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;

            _driftTimer.Dispose();
            _autoDim?.Dispose();
            _hotkeys.Dispose();
            ColorEffectController.Shutdown(); // quita el filtro de color
            _dimmer.Dispose();                // quita el veil de atenuación
            _tray.Visible = false;
            _tray.Dispose();
            _appIcon.Dispose();
            _marshaler.Dispose();
        }
        base.Dispose(disposing);
    }
}
