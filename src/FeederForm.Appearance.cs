using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WsgStreamFeeder;

internal sealed partial class FeederForm
{
    private Palette palette = ThemePreference.IsLight() ? Palette.Day : Palette.Night;
    private readonly Label status = LabelFor("Waiting for MSFS 2024…", 11);
    private readonly Label connectionBadge = LabelFor("●  WAITING", 9, true, "muted");
    private readonly Label altitudeValue = LabelFor("N/A", 27, true);
    private readonly Label headingValue = LabelFor("N/A", 27, true);
    private readonly Label speedValue = LabelFor("N/A", 27, true);
    private readonly Label verticalSpeedValue = LabelFor("N/A", 27, true);
    private readonly PictureBox logo = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
    private readonly WsgButton darkButton = new() { Text = "Dark", Centered = true };
    private readonly WsgButton lightButton = new() { Text = "Light", Centered = true };
    private Icon appIcon = null!;

    private static Label LabelFor(string text, float size, bool bold = false, string? role = null) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true,
        BackColor = Color.Transparent, Tag = role, Margin = Padding.Empty
    };

    private static TableLayoutPanel Grid(int columns = 1) => new()
    {
        ColumnCount = columns, Dock = DockStyle.Fill, Margin = Padding.Empty,
        Padding = Padding.Empty, BackColor = Color.Transparent
    };

    private void InitializeAppearance()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WsgStreamFeeder.AppIcon")!)
            appIcon = new Icon(stream);
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WsgStreamFeeder.Logo")!)
        using (var original = Image.FromStream(stream)) logo.Image = new Bitmap(original);
        Text = $"WSG Stream Feeder v{Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";
        Icon = appIcon;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var shell = Grid(2);
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        var sidebar = Grid();
        sidebar.Tag = "sidebar";
        sidebar.Padding = new Padding(18, 22, 18, 18);
        sidebar.RowCount = 10;
        foreach (var height in new[] { 78, 28, 44, 44, 28, 40, 0, 28, 40, 44 })
            sidebar.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
        shell.Controls.Add(sidebar, 0, 0);
        var brand = Grid(2);
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logo.Margin = new Padding(0, 0, 10, 8);
        brand.Controls.Add(logo, 0, 0);
        var brandText = LabelFor("WE SIM GLIDE", 10, true);
        brand.Controls.Add(brandText, 1, 0);
        sidebar.Controls.Add(brand, 0, 0);
        sidebar.Controls.Add(LabelFor("STREAM", 8, true, "muted"), 0, 1);
        sidebar.Controls.Add(ActionButton("Open output folder", (_, _) => OpenFolder(), true), 0, 2);
        sidebar.Controls.Add(ActionButton("Minimize to tray", (_, _) => Hide()), 0, 3);
        sidebar.Controls.Add(LabelFor("APPEARANCE", 8, true, "muted"), 0, 4);
        var themes = Grid(2);
        themes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        themes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        darkButton.Dock = lightButton.Dock = DockStyle.Fill;
        darkButton.Margin = new Padding(0, 0, 3, 0);
        lightButton.Margin = new Padding(3, 0, 0, 0);
        darkButton.Click += (_, _) => SetTheme(false);
        lightButton.Click += (_, _) => SetTheme(true);
        themes.Controls.Add(darkButton, 0, 0);
        themes.Controls.Add(lightButton, 1, 0);
        sidebar.Controls.Add(themes, 0, 5);
        sidebar.Controls.Add(LabelFor("WESIMGLIDE", 8, true, "muted"), 0, 7);
        var about = new LinkLabel
        {
            Text = "About  ↗", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AccessibleName = "About — opens WeSimGlide.org", Margin = Padding.Empty
        };
        about.LinkClicked += (_, _) => OpenAbout();
        sidebar.Controls.Add(about, 0, 8);
        sidebar.Controls.Add(ActionButton("Exit", (_, _) => Close()), 0, 9);

        var main = Grid();
        main.Padding = new Padding(24, 24, 24, 16);
        main.RowCount = 6;
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.Controls.Add(main, 1, 0);
        var header = Grid();
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(LabelFor("MSFS 2024  /  OBS", 9, false, "muted"), 0, 0);
        header.Controls.Add(LabelFor("WSG Stream Feeder", 25, true), 0, 1);
        header.Controls.Add(LabelFor("Your flight data, ready for OBS.", 10, false, "muted"), 0, 2);
        main.Controls.Add(header, 0, 0);
        var statusPanel = new Surface { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), Margin = new Padding(0, 8, 0, 8) };
        var statusLayout = Grid();
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusLayout.Controls.Add(connectionBadge, 0, 0);
        statusLayout.Controls.Add(status, 0, 1);
        statusPanel.Controls.Add(statusLayout);
        main.Controls.Add(statusPanel, 0, 1);
        var metrics = Grid(2);
        metrics.RowCount = 2;
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        metrics.Controls.Add(Metric("ALTITUDE", "feet", "altitude.txt", altitudeValue, new Padding(0, 4, 6, 6)), 0, 0);
        metrics.Controls.Add(Metric("TRUE HEADING", "degrees", "heading_true.txt", headingValue, new Padding(6, 4, 0, 6)), 1, 0);
        metrics.Controls.Add(Metric("GROUND SPEED", "knots", "speed.txt", speedValue, new Padding(0, 6, 6, 8)), 0, 1);
        metrics.Controls.Add(Metric("VERTICAL SPEED", "feet / min", "vspeed.txt", verticalSpeedValue, new Padding(6, 6, 0, 8)), 1, 1);
        main.Controls.Add(metrics, 0, 2);
        var output = new Surface { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), Margin = new Padding(0, 8, 0, 0) };
        var outputLayout = Grid();
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outputLayout.Controls.Add(LabelFor("OBS TEXT FILES", 8, true, "muted"), 0, 0);
        outputLayout.Controls.Add(new TextBox
        {
            Text = options.OutputDirectory, ReadOnly = true, Multiline = true, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9), Margin = Padding.Empty,
            AccessibleName = "OBS output folder", Tag = "path"
        }, 0, 1);
        output.Controls.Add(outputLayout);
        main.Controls.Add(output, 0, 3);
        main.Controls.Add(LabelFor("Updates every second  ·  Reconnects automatically", 8, false, "muted"), 0, 4);
        var updates = Grid(2);
        updates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        updates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        updateStatus.Text = AppUpdate.Enabled ? "Checks for updates when opened" : "Development build — automatic updates disabled";
        updateButton.Enabled = AppUpdate.Enabled;
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        updates.Controls.Add(updateStatus, 0, 0);
        updates.Controls.Add(updateButton, 1, 0);
        main.Controls.Add(updates, 0, 5);
    }

    private static WsgButton ActionButton(string text, EventHandler action, bool primary = false)
    {
        var button = new WsgButton { Text = text, Primary = primary, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        button.Click += action;
        return button;
    }

    private static Surface Metric(string name, string unit, string file, Label value, Padding margin)
    {
        var panel = new Surface { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12), Margin = margin };
        var layout = Grid();
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.Controls.Add(LabelFor(name, 9, true, "muted"), 0, 0);
        layout.Controls.Add(value, 0, 1);
        layout.Controls.Add(LabelFor(unit + "  ·  " + file, 8, false, "muted"), 0, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private void OpenAbout()
    {
        try { Process.Start(new ProcessStartInfo("https://WeSimGlide.org/") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open WeSimGlide.org"); }
    }

    private void SetTheme(bool light)
    {
        palette = light ? Palette.Day : Palette.Night;
        ApplyAppearance();
        ThemePreference.Save(light);
    }

    private void ApplyAppearance()
    {
        SuspendLayout();
        BackColor = palette.Background;
        ForeColor = palette.Text;
        darkButton.Selected = palette.Dark;
        lightButton.Selected = !palette.Dark;
        darkButton.AccessibleDescription = palette.Dark ? "Selected theme" : "Switch to dark theme";
        lightButton.AccessibleDescription = !palette.Dark ? "Selected theme" : "Switch to light theme";
        ApplyColors(this);
        if (tray?.ContextMenuStrip is { } menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new WsgMenuColors(palette));
            menu.BackColor = palette.Sidebar;
            menu.ForeColor = palette.Text;
            foreach (ToolStripItem item in menu.Items) item.ForeColor = palette.Text;
        }
        ApplyTitleBar();
        ResumeLayout(true);
        Invalidate(true);
    }

    private void ApplyColors(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = control.Tag as string == "muted" ? palette.Muted : palette.Text;
            switch (control)
            {
                case WsgButton button: button.Palette = palette; break;
                case Surface surface: surface.BackColor = palette.Panel; surface.BorderColor = palette.Line; break;
                case TextBox text: text.BackColor = palette.Panel; break;
                case LinkLabel link:
                    link.LinkColor = palette.Accent;
                    link.ActiveLinkColor = palette.Secondary;
                    link.VisitedLinkColor = palette.Accent;
                    link.BackColor = parent.BackColor;
                    break;
                default: control.BackColor = control.Tag as string == "sidebar" ? palette.Sidebar : parent.BackColor; break;
            }
            ApplyColors(control);
            control.Invalidate();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void ApplyTitleBar()
    {
        if (!IsHandleCreated) return;
        var dark = palette.Dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        var color = ColorTranslator.ToWin32(palette.Background);
        DwmSetWindowAttribute(Handle, 35, ref color, sizeof(int));
        var text = ColorTranslator.ToWin32(palette.Text);
        DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBar();
    }
}
