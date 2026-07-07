using System.Runtime.InteropServices;

namespace BrightSync.Core;

/// <summary>
/// Atenuación profunda mediante una ventana negra semitransparente, topmost y
/// "click-through" (no captura ratón ni teclado), que cubre todos los monitores.
/// Se apila sobre el brillo de hardware/gamma y puede oscurecer casi a negro,
/// sin el límite ~50% que Windows impone a la gamma.
/// </summary>
public sealed class OverlayDimmer : IDisposable
{
    private DimForm? _form;
    private readonly Control _ui; // marshaler al hilo de UI
    private double _opacity;

    public OverlayDimmer()
    {
        _ui = new Control();
        _ = _ui.Handle; // fuerza handle en el hilo de UI
    }

    /// <summary>Opacidad 0 (nada) … 0.9 (casi negro). Se marshala al hilo de UI.</summary>
    public void SetDim(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.0, 0.92);
        _opacity = opacity;

        if (_ui.InvokeRequired)
        {
            try { _ui.BeginInvoke(new Action(() => Apply(opacity))); } catch { }
            return;
        }
        Apply(opacity);
    }

    public double CurrentOpacity => _opacity;

    private void Apply(double opacity)
    {
        if (opacity <= 0.001)
        {
            _form?.Hide();
            return;
        }
        _form ??= new DimForm();
        _form.Bounds = SystemInformation.VirtualScreen; // cubre todos los monitores
        _form.Opacity = opacity;
        if (!_form.Visible) _form.Show();
        _form.ReassertTopmost(); // mantenerlo por encima de las apps del usuario
    }

    public void Dispose()
    {
        if (_ui.InvokeRequired) { try { _ui.BeginInvoke(new Action(Dispose)); } catch { } return; }
        _form?.Dispose();
        _form = null;
        _ui.Dispose();
    }

    /// <summary>Ventana-veil: sin bordes, topmost, transparente al ratón, sin activar.</summary>
    private sealed class DimForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        public DimForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = System.Drawing.Color.Black;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ReassertTopmost()
        {
            if (!IsHandleCreated) return;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
