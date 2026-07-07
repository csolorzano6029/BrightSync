using System.Drawing;
using BrightSync.Core;

namespace BrightSync.UI;

/// <summary>Ventana de ajustes. Edita el perfil activo y lo aplica en vivo.</summary>
public sealed class SettingsForm : Form
{
    private readonly DisplayEngine _engine;
    private readonly Action _onConfigChanged;
    private AppConfig Config => _engine.Config;

    private ComboBox _profileBox = null!;
    private TrackBar _brightness = null!;
    private Label _brightnessLbl = null!;
    private TrackBar _temperature = null!;
    private Label _temperatureLbl = null!;
    private CheckBox _preferHardware = null!;
    private ComboBox _filterBox = null!;

    private TrackBar _custD = null!, _custT = null!, _custI = null!;
    private FlowLayoutPanel _customPanel = null!;

    private CheckBox _autoDim = null!;
    private TrackBar _autoMin = null!;
    private TrackBar _autoMax = null!;
    private CheckBox _startup = null!;
    private CheckBox _earlyStart = null!;
    private CheckBox _deepGamma = null!;
    private TableLayoutPanel _root = null!;

    private bool _loading;

    public SettingsForm(DisplayEngine engine, Action onConfigChanged)
    {
        _engine = engine;
        _onConfigChanged = onConfigChanged;
        BuildUi();
        LoadFromConfig();
        DarkTheme.Apply(this);
    }

    private void BuildUi()
    {
        Text = "BrightSync — Ajustes";
        FormBorderStyle = FormBorderStyle.Sizable; // redimensionable: robusto ante cualquier DPI
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(460, 720);
        MinimumSize = new Size(400, 460);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(16, 10, 16, 12),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_root);

        // --- Perfil ---
        Section("Perfil");
        var profRow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4)
        };
        _profileBox = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 6, 0) };
        _profileBox.SelectedIndexChanged += (_, _) => { if (!_loading) SwitchProfile(); };
        profRow.Controls.Add(_profileBox);
        profRow.Controls.Add(Btn("Nuevo", NewProfile));
        profRow.Controls.Add(Btn("Borrar", DeleteProfile));
        profRow.Controls.Add(Btn("Renombrar", RenameProfile));
        Add(profRow);

        // --- Brillo ---
        _brightnessLbl = Section("Brillo");
        _brightness = Track(5, 100, 5);
        _brightness.ValueChanged += (_, _) => OnLiveChange();
        Add(_brightness);

        // --- Temperatura ---
        _temperatureLbl = Section("Temperatura de color");
        _temperature = Track(3000, 6500, 250);
        _temperature.ValueChanged += (_, _) => OnLiveChange();
        Add(_temperature);

        // --- Hardware ---
        _preferHardware = Check("Preferir brillo de hardware (WMI/DDC-CI) si está disponible");
        _preferHardware.CheckedChanged += (_, _) => OnLiveChange();
        Add(_preferHardware);
        Add(new Label
        {
            AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 4),
            Text = _engine.HardwareAvailable ? "Hardware detectado ✓" : "Sin control de hardware: se usará gamma."
        });

        // --- Filtro ---
        Section("Filtro de color / accesibilidad");
        _filterBox = new ComboBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 4)
        };
        _filterBox.Items.AddRange(new object[]
        {
            "Ninguno", "Escala de grises", "Invertir", "Alto contraste",
            "Daltonismo: Protanopia (rojo)", "Daltonismo: Deuteranopia (verde)", "Daltonismo: Tritanopia (azul)",
            "Personalizado (Deuteranopia + Tritanopia) — para ti"
        });
        _filterBox.SelectedIndexChanged += (_, _) => OnLiveChange();
        Add(_filterBox);

        // Ajuste en vivo del filtro personalizado (panel visible solo cuando está seleccionado)
        _customPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 0, 0), Visible = false
        };
        _customPanel.Controls.Add(new Label
        {
            AutoSize = true, Text = "Ajusta tu filtro moviendo los controles durante el test:",
            ForeColor = Color.Gray, Margin = new Padding(0, 6, 0, 2)
        });
        _customPanel.Controls.Add(SmallLabel("Corrección rojo/verde (Deuteranopia)"));
        _custD = TrackFixed(0, 100, 10); _custD.ValueChanged += (_, _) => OnCustomChange(); _customPanel.Controls.Add(_custD);
        _customPanel.Controls.Add(SmallLabel("Corrección azul/amarillo (Tritanopia)"));
        _custT = TrackFixed(0, 100, 10); _custT.ValueChanged += (_, _) => OnCustomChange(); _customPanel.Controls.Add(_custT);
        _customPanel.Controls.Add(SmallLabel("Intensidad de la corrección"));
        _custI = TrackFixed(0, 150, 10); _custI.ValueChanged += (_, _) => OnCustomChange(); _customPanel.Controls.Add(_custI);
        var btnResetCustom = Btn("Restablecer valores por defecto", ResetCustom);
        btnResetCustom.Margin = new Padding(0, 6, 0, 4);
        _customPanel.Controls.Add(btnResetCustom);
        Add(_customPanel);

        // --- Auto-dim ---
        Section("Atenuación automática por contenido");
        _autoDim = Check("Activar auto-atenuación (por contenido de pantalla)");
        _autoDim.CheckedChanged += (_, _) => OnAutoDimChange();
        Add(_autoDim);
        Add(SmallLabel("Brillo mínimo"));
        _autoMin = Track(5, 100, 5);
        _autoMin.ValueChanged += (_, _) => OnAutoDimChange();
        Add(_autoMin);
        Add(SmallLabel("Brillo máximo"));
        _autoMax = Track(5, 100, 5);
        _autoMax.ValueChanged += (_, _) => OnAutoDimChange();
        Add(_autoMax);

        // --- Sistema ---
        Section("Sistema");
        _startup = Check("Iniciar con Windows");
        _startup.CheckedChanged += (_, _) => { if (!_loading) ToggleStartup(); };
        Add(_startup);

        _earlyStart = Check("Arranque temprano (antes que otras apps · pide admin)");
        _earlyStart.CheckedChanged += (_, _) => { if (!_loading) ToggleEarlyStart(); };
        Add(_earlyStart);

        _deepGamma = Check("Desbloquear gamma profundo (admin · reinicia sesión)");
        _deepGamma.CheckedChanged += (_, _) => { if (!_loading) ToggleDeepGamma(); };
        Add(_deepGamma);

        var btnRepairTask = Btn("Reparar / mejorar arranque temprano (prioridad alta)", UpgradeEarlyStart);
        btnRepairTask.Margin = new Padding(0, 6, 0, 0);
        Add(btnRepairTask);

        // --- Botones: barra fija abajo, SIEMPRE visible (no se va con el scroll) ---
        var bottomBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 12, 16, 14),
            BackColor = SystemColors.Control
        };
        var btnClose = Btn("Cerrar", Close);
        var btnSave = Btn("Guardar", () => { Save(); Close(); });
        btnSave.DialogResult = DialogResult.OK;
        bottomBar.Controls.Add(btnClose);
        bottomBar.Controls.Add(btnSave);
        Controls.Add(bottomBar); // tras _root: queda abajo y _root llena el resto
        AcceptButton = btnSave;

        // Tras el escalado por DPI, la ventana puede quedar más alta que la pantalla y
        // esconder la barra de botones. La limitamos al área de trabajo y la centramos;
        // el contenido de arriba se desplaza (AutoScroll), pero los botones siempre se ven.
        Load += (_, _) =>
        {
            var wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width);
            int h = Math.Min(Height, wa.Height);
            Size = new Size(w, h);
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
        };
    }

    private void Add(Control c) => _root.Controls.Add(c);

    private void SetCustomVisible(bool v) => _customPanel.Visible = v;

    private Label Section(string title)
    {
        var lbl = new Label
        {
            AutoSize = true, Text = title,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 3)
        };
        Add(lbl);
        return lbl;
    }

    private static Button Btn(string text, Action onClick)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 6, 12, 6), Margin = new Padding(8, 0, 0, 0)
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Label SmallLabel(string text) => new()
    {
        AutoSize = true, Text = text, ForeColor = Color.Gray, Margin = new Padding(0, 4, 0, 0)
    };

    private static CheckBox Check(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3)
    };

    private static TrackBar Track(int min, int max, int freq) => new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 40,
        Minimum = min, Maximum = max,
        TickFrequency = freq, LargeChange = freq, SmallChange = Math.Max(1, freq / 5),
        Margin = new Padding(0, 0, 0, 2)
    };

    // Slider de ancho fijo (para dentro del panel de flujo del filtro personalizado).
    private static TrackBar TrackFixed(int min, int max, int freq) => new()
    {
        Width = 360, Height = 40, Minimum = min, Maximum = max,
        TickFrequency = freq, LargeChange = freq, SmallChange = Math.Max(1, freq / 5),
        Margin = new Padding(0, 0, 0, 2)
    };

    private void LoadFromConfig()
    {
        _loading = true;
        _profileBox.Items.Clear();
        foreach (var p in Config.Profiles) _profileBox.Items.Add(p.Name);
        _profileBox.SelectedItem = Config.ActiveProfile;

        var cur = Config.Current;
        _brightness.Value = Math.Clamp(cur.Brightness, 5, 100);
        _temperature.Value = Math.Clamp(cur.Temperature, 3000, 6500);
        _preferHardware.Checked = cur.PreferHardware;
        _filterBox.SelectedIndex = (int)cur.Filter;

        _autoDim.Checked = Config.AutoDim.Enabled;
        _autoMin.Value = Math.Clamp(Config.AutoDim.MinBrightness, 5, 100);
        _autoMax.Value = Math.Clamp(Config.AutoDim.MaxBrightness, 5, 100);
        _startup.Checked = StartupManager.IsEnabled();
        _earlyStart.Checked = StartupManager.EarlyStartEnabled();
        _deepGamma.Checked = SystemTweaks.DeepGammaEnabled();

        _custD.Value = Math.Clamp(Config.CustomFilter.Deuteranopia, 0, 100);
        _custT.Value = Math.Clamp(Config.CustomFilter.Tritanopia, 0, 100);
        _custI.Value = Math.Clamp(Config.CustomFilter.Intensity, 0, 150);
        SetCustomVisible(cur.Filter == ColorFilter.Personalizado);

        UpdateLabels();
        _loading = false;
    }

    private void UpdateLabels()
    {
        _brightnessLbl.Text = $"Brillo — {_brightness.Value}%";
        _temperatureLbl.Text = $"Temperatura de color — {_temperature.Value} K";
    }

    private void OnLiveChange()
    {
        if (_loading) return;
        var p = Config.Current;
        p.Brightness = _brightness.Value;
        p.Temperature = _temperature.Value;
        p.PreferHardware = _preferHardware.Checked;
        p.Filter = (ColorFilter)Math.Max(0, _filterBox.SelectedIndex);
        SetCustomVisible(p.Filter == ColorFilter.Personalizado);
        UpdateLabels();
        _engine.ApplyProfile(p);
    }

    private void OnCustomChange()
    {
        if (_loading) return;
        Config.CustomFilter.Deuteranopia = _custD.Value;
        Config.CustomFilter.Tritanopia = _custT.Value;
        Config.CustomFilter.Intensity = _custI.Value;
        _engine.ApplyProfile(Config.Current); // vista previa en vivo
    }

    private void ResetCustom()
    {
        Config.CustomFilter.ResetToDefault();
        _loading = true;
        _custD.Value = Config.CustomFilter.Deuteranopia;
        _custT.Value = Config.CustomFilter.Tritanopia;
        _custI.Value = Config.CustomFilter.Intensity;
        _loading = false;
        _engine.ApplyProfile(Config.Current);
    }

    private void OnAutoDimChange()
    {
        if (_loading) return;
        // Coherencia: mín ≤ máx
        if (_autoMin.Value > _autoMax.Value) _autoMax.Value = _autoMin.Value;
        Config.AutoDim.Enabled = _autoDim.Checked;
        Config.AutoDim.MinBrightness = _autoMin.Value;
        Config.AutoDim.MaxBrightness = _autoMax.Value;
        _onConfigChanged();
    }

    private void SwitchProfile()
    {
        if (_profileBox.SelectedItem is string name)
        {
            _engine.SetProfile(name);
            LoadFromConfig();
        }
    }

    private void NewProfile()
    {
        var name = Prompt("Nombre del nuevo perfil:", "Perfil " + (Config.Profiles.Count + 1));
        if (string.IsNullOrWhiteSpace(name) || Config.Profiles.Any(p => p.Name == name)) return;
        var np = Config.Current.Clone();
        np.Name = name;
        Config.Profiles.Add(np);
        Config.ActiveProfile = name;
        Save();
        LoadFromConfig();
    }

    private void DeleteProfile()
    {
        if (Config.Profiles.Count < 2) { MessageBox.Show("Debe existir al menos un perfil."); return; }
        Config.Profiles.RemoveAll(p => p.Name == Config.ActiveProfile);
        Config.ActiveProfile = Config.Profiles[0].Name;
        _engine.ApplyCurrentProfile();
        Save();
        LoadFromConfig();
    }

    private void RenameProfile()
    {
        var name = Prompt("Nuevo nombre:", Config.ActiveProfile);
        if (string.IsNullOrWhiteSpace(name) || Config.Profiles.Any(p => p.Name == name)) return;
        Config.Current.Name = name;
        Config.ActiveProfile = name;
        Save();
        LoadFromConfig();
    }

    private void ToggleStartup()
    {
        if (_startup.Checked) StartupManager.Enable();
        else
        {
            StartupManager.Disable();
            _loading = true; _earlyStart.Checked = false; _loading = false;
        }
        Config.StartWithWindows = _startup.Checked;
        Save();
    }

    private void ToggleEarlyStart()
    {
        if (_earlyStart.Checked)
        {
            if (StartupManager.EnableEarlyStart())
            {
                _loading = true; _startup.Checked = true; _loading = false; // la tarea reemplaza al Run key
                Config.StartWithWindows = true;
            }
            else
            {
                // Usuario canceló el UAC o falló: revertimos el check.
                _loading = true; _earlyStart.Checked = false; _loading = false;
                MessageBox.Show("No se pudo crear la tarea de arranque temprano (se canceló el permiso de administrador).",
                    "BrightSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            StartupManager.DisableEarlyStart();
            // Al quitar la tarea, dejamos el arranque normal por Run key si sigue marcado.
            if (_startup.Checked) StartupManager.Enable();
        }
        Save();
    }

    private void UpgradeEarlyStart()
    {
        if (StartupManager.EnableEarlyStart())
        {
            _loading = true; _earlyStart.Checked = true; _startup.Checked = true; _loading = false;
            Config.StartWithWindows = true;
            Save();
            MessageBox.Show("Arranque temprano actualizado: prioridad alta, sin retraso y sin restricción de batería.\nSe aplicará en el próximo reinicio.",
                "BrightSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("No se pudo actualizar (se canceló el permiso de administrador).",
                "BrightSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ToggleDeepGamma()
    {
        bool ok = _deepGamma.Checked ? SystemTweaks.EnableDeepGamma() : SystemTweaks.DisableDeepGamma();
        if (!ok)
        {
            _loading = true; _deepGamma.Checked = SystemTweaks.DeepGammaEnabled(); _loading = false;
            MessageBox.Show("No se pudo cambiar el ajuste (se canceló el permiso de administrador).",
                "BrightSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MessageBox.Show("Ajuste aplicado. Reinicia la sesión de Windows para que surta efecto.",
            "BrightSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Save()
    {
        ConfigStore.Save(Config);
        _onConfigChanged();
    }

    /// <summary>Diálogo de texto simple (WinForms no trae uno nativo).</summary>
    private static string Prompt(string text, string def)
    {
        using var f = new Form
        {
            Text = "BrightSync", ClientSize = new Size(320, 110),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false
        };
        var lbl = new Label { Left = 12, Top = 12, Width = 296, Text = text };
        var tb = new TextBox { Left = 12, Top = 36, Width = 296, Text = def };
        var ok = new Button { Left = 152, Top = 70, Width = 70, Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Left = 238, Top = 70, Width = 70, Text = "Cancelar", DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        DarkTheme.Apply(f);
        return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : "";
    }
}
