using System.Drawing;
using System.Runtime.InteropServices;

namespace BrightSync.UI;

/// <summary>Tema oscuro para las ventanas y menús (WinForms no lo trae de serie).</summary>
public static class DarkTheme
{
    public static readonly Color Back = Color.FromArgb(32, 32, 32);
    public static readonly Color Fore = Color.FromArgb(238, 238, 238);
    public static readonly Color Hint = Color.FromArgb(165, 165, 165);
    public static readonly Color Control = Color.FromArgb(58, 58, 58);
    public static readonly Color Border = Color.FromArgb(90, 90, 90);
    public static readonly Color Selected = Color.FromArgb(70, 70, 74);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Form form)
    {
        form.BackColor = Back;
        form.ForeColor = Fore;

        void SetTitleBar()
        {
            if (!form.IsHandleCreated) return;
            int on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 2004+); 19 en compilaciones previas.
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }

        if (form.IsHandleCreated) SetTitleBar();
        form.HandleCreated += (_, _) => SetTitleBar();

        ApplyToControls(form.Controls);
    }

    private static void ApplyToControls(Control.ControlCollection controls)
    {
        foreach (Control c in controls)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Control;
                    b.ForeColor = Fore;
                    b.FlatAppearance.BorderColor = Border;
                    break;
                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Control;
                    cb.ForeColor = Fore;
                    break;
                case TrackBar tb:
                    tb.BackColor = Back;
                    break;
                case CheckBox chk:
                    chk.BackColor = Back;
                    chk.ForeColor = Fore;
                    break;
                case Label lb:
                    lb.BackColor = Color.Transparent;
                    lb.ForeColor = lb.ForeColor == Color.Gray ? Hint : Fore;
                    break;
                case TextBox txt:
                    txt.BackColor = Control;
                    txt.ForeColor = Fore;
                    txt.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case Panel p:            // cubre FlowLayoutPanel y TableLayoutPanel
                    p.BackColor = Back;
                    break;
            }
            if (c.HasChildren) ApplyToControls(c.Controls);
        }
    }

    /// <summary>Aplica tema oscuro a un menú contextual (el de la bandeja).</summary>
    public static void ApplyMenu(ToolStripDropDown menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
        StyleItems(menu.Items);
    }

    private static void StyleItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            it.ForeColor = Fore;
            it.BackColor = Back;
            if (it is ToolStripMenuItem mi && mi.HasDropDownItems)
            {
                mi.DropDown.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
                StyleItems(mi.DropDownItems);
            }
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Selected;
        public override Color MenuItemSelectedGradientBegin => Selected;
        public override Color MenuItemSelectedGradientEnd => Selected;
        public override Color MenuItemPressedGradientBegin => Selected;
        public override Color MenuItemPressedGradientEnd => Selected;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
