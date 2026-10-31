using Microsoft.Win32;
using System.Drawing.Drawing2D;

namespace StillLife2Patcher;

enum ThemeChoice { System, Light, Dark }

sealed record Palette(Color Background, Color Panel, Color Text, Color Muted, Color Border, Color Link, Color Success, Color Warning, Color Disabled, Color DisabledText)
{
    public static Palette Light => new(Color.White, Color.FromArgb(245,247,250), Color.FromArgb(25,30,38), Color.FromArgb(80,90,105), Color.FromArgb(180,188,199), Color.FromArgb(25,80,145), Color.FromArgb(20,100,55), Color.FromArgb(132,80,0), Color.FromArgb(234,237,242), Color.FromArgb(88,96,108));
    public static Palette Dark => new(Color.FromArgb(25,28,33), Color.FromArgb(36,40,47), Color.FromArgb(240,243,248), Color.FromArgb(185,195,209), Color.FromArgb(88,98,113), Color.FromArgb(148,197,255), Color.FromArgb(128,220,165), Color.FromArgb(255,206,122), Color.FromArgb(47,53,63), Color.FromArgb(185,195,209));
    public static Palette Contrast => new(SystemColors.Window, SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.HotTrack, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Control, SystemColors.WindowText);
}

static class Appearance
{
    public static Palette Colors { get; set; } = Palette.Light;
    public static bool SystemDark
    {
        get
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
    }
    public static bool IsDark(ThemeChoice choice) => !SystemInformation.HighContrast && (choice == ThemeChoice.Dark || choice == ThemeChoice.System && SystemDark);
}

// Draw button text ourselves: WinForms otherwise overrides disabled text colors,
// which makes a disabled primary button dark-on-blue. Chevrons scale with DPI.
sealed class UiButton : Button
{
    [System.ComponentModel.DefaultValue(false)]
    public bool Primary { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool Chevron { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool Circular { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool Borderless { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool Expanded { get; set; }
    [System.ComponentModel.DefaultValue(ThemeChoice.System)]
    public ThemeChoice ThemeIcon { get; set; }
    bool hovered, pressed;
    public UiButton()
    {
        FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Appearance.Colors; bool accent = Primary && Enabled;
        Color bg = !Enabled ? p.Disabled : accent ? Color.FromArgb(36,91,151) : Borderless ? p.Background : p.Panel;
        Color fg = !Enabled ? p.DisabledText : accent ? Color.White : p.Text;
        if (SystemInformation.HighContrast && accent) { bg = SystemColors.Highlight; fg = SystemColors.HighlightText; }
        if (Enabled && (hovered || pressed)) bg = pressed ? ControlPaint.Dark(bg, .08f) : ControlPaint.Light(bg, .08f);
        if (Circular)
        {
            e.Graphics.Clear(Parent?.BackColor ?? p.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(bg);
            using var edge = new Pen(p.Border);
            var circle = new RectangleF(.5f, .5f, Width - 1, Height - 1);
            e.Graphics.FillEllipse(fill, circle); e.Graphics.DrawEllipse(edge, circle);
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
            float dpiScale = DeviceDpi / 96f; e.Graphics.ScaleTransform(dpiScale, dpiScale);
            using var ink = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (ThemeIcon == ThemeChoice.Light)
            {
                e.Graphics.DrawEllipse(ink, -3, -3, 6, 6);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    e.Graphics.DrawLine(ink, (float)Math.Cos(angle) * 5.5f, (float)Math.Sin(angle) * 5.5f, (float)Math.Cos(angle) * 8, (float)Math.Sin(angle) * 8);
                }
            }
            else if (ThemeIcon == ThemeChoice.Dark)
            {
                using var moon = new GraphicsPath();
                moon.AddArc(-7, -7, 14, 14, 60, 240);
                moon.AddBezier(3.5f, -6.062f, -3.5f, -4.5f, -3.5f, 4.5f, 3.5f, 6.062f);
                moon.CloseFigure(); using var moonInk = new SolidBrush(fg); e.Graphics.FillPath(moonInk, moon);
            }
            else
            {
                e.Graphics.DrawRectangle(ink, -8, -6, 16, 11);
                e.Graphics.DrawLine(ink, 0, 5, 0, 8); e.Graphics.DrawLine(ink, -4, 8, 4, 8);
            }
            e.Graphics.Restore(state);
            if (Focused && ShowFocusCues) { using var focus = new Pen(p.Link, 1.5f); e.Graphics.DrawEllipse(focus, 3, 3, Width - 7, Height - 7); }
            return;
        }
        e.Graphics.Clear(bg);
        if (!Borderless) { using var border = new Pen(accent ? bg : p.Border); e.Graphics.DrawRectangle(border, 0, 0, Width-1, Height-1); }
        float scale = DeviceDpi / 96f;
        var textBounds = Rectangle.Inflate(ClientRectangle, -(int)(8*scale), -2);
        if (Chevron) textBounds.Width -= (int)(20*scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Chevron)
        {
            float x = Width - 17*scale, y = Height/2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(fg, 1.7f*scale) { StartCap=LineCap.Round, EndCap=LineCap.Round };
            float direction = Expanded ? -1 : 1;
            e.Graphics.DrawLines(pen, [new PointF(x-4*scale,y-2*scale*direction),new PointF(x,y+2*scale*direction),new PointF(x+4*scale,y-2*scale*direction)]);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle,-4,-4),fg,bg);
    }
}

sealed partial class MainForm
{
    readonly UiButton theme = new() { Width = 32, Height = 32, Circular = true };
    readonly ToolTip themeTip = new();
    readonly ContextMenuStrip themeMenu = new();
    ThemeChoice themeChoice = ThemeChoice.System;
    bool dark, applyingTheme;
    void InitializeThemes()
    {
        foreach (ThemeChoice choice in Enum.GetValues<ThemeChoice>())
        {
            var option = new ToolStripMenuItem { Tag = choice };
            option.Click += (_, _) => SetTheme(choice); themeMenu.Items.Add(option);
        }
        theme.Click += (_,_) => themeMenu.Show(theme,new Point(0,theme.Height));
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        Disposed += (_,_) => { SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged; themeMenu.Dispose(); themeTip.Dispose(); };
    }
    void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (themeChoice == ThemeChoice.System && !IsDisposed && IsHandleCreated)
            BeginInvoke(() => { if (!IsDisposed && !applyingTheme) ApplyTheme(); });
    }
    internal void SetTheme(ThemeChoice choice) { themeChoice=choice; ApplyTheme(); }
    void ApplyTheme()
    {
        if (applyingTheme) return; applyingTheme=true;
        try
        {
            dark = Appearance.IsDark(themeChoice);
            Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
            var p = Appearance.Colors = SystemInformation.HighContrast ? Palette.Contrast : dark ? Palette.Dark : Palette.Light;
            void PaintControls(Control control)
            {
                control.BackColor = control is TextBox or DataGridView or Button || control == fileBox ? p.Panel : p.Background;
                control.ForeColor = p.Text;
                foreach (Control child in control.Controls) PaintControls(child);
            }
            PaintControls(this);
            help.ForeColor = scanStatus.ForeColor = p.Muted;
            status.ForeColor = inspection?.Compatible == true ? inspection.RequiresConsent ? p.Warning : p.Success : p.Muted;
            grid.EnableHeadersVisualStyles=false; grid.BackgroundColor=p.Panel; grid.GridColor=p.Border;
            grid.DefaultCellStyle.BackColor=p.Panel; grid.DefaultCellStyle.ForeColor=p.Text;
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(36,91,151); grid.DefaultCellStyle.SelectionForeColor=Color.White;
            grid.ColumnHeadersDefaultCellStyle.BackColor=p.Background; grid.ColumnHeadersDefaultCellStyle.ForeColor=p.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=p.Background;
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Tag is StillLife2Patcher.Core.SaveEntry entry) row.DefaultCellStyle.ForeColor=entry.Inspection?.Compatible == true ? entry.Inspection.RequiresConsent ? p.Warning : p.Success : p.Text;
            theme.ThemeIcon = themeChoice;
            theme.AccessibleName = T("appearance") + ": " + T("theme" + themeChoice);
            theme.AccessibleDescription = string.Join(" / ", Enum.GetValues<ThemeChoice>().Select(x => T("theme" + x)));
            themeTip.SetToolTip(theme, theme.AccessibleName);
            foreach (ToolStripMenuItem option in themeMenu.Items) { option.Text=T("theme"+option.Tag); option.Checked=(ThemeChoice)option.Tag! == themeChoice; }
            foreach (var menu in new[] { themeMenu, languageMenu })
            {
                menu.BackColor=p.Panel; menu.ForeColor=p.Text;
                menu.Renderer=new ToolStripProfessionalRenderer(new MenuColors(p));
                foreach (ToolStripItem item in menu.Items) { item.BackColor=p.Panel; item.ForeColor=p.Text; }
            }
            Invalidate(true);
        }
        finally { applyingTheme=false; }
    }
    sealed class MenuColors(Palette p) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => p.Panel;
        public override Color ImageMarginGradientBegin => p.Panel;
        public override Color ImageMarginGradientMiddle => p.Panel;
        public override Color ImageMarginGradientEnd => p.Panel;
        public override Color MenuItemSelected => p.Disabled;
        public override Color MenuItemBorder => p.Border;
        public override Color MenuBorder => p.Border;
        public override Color CheckBackground => p.Disabled;
    }
}
