namespace WsgStreamFeeder;

internal sealed class CaptureForm : Form
{
    private readonly TelemetryPanel panel;
    private bool fitting;

    public CaptureForm(TelemetryPanel panel)
    {
        this.panel = panel;
        Text = "WSG Stream Feeder — Telemetry";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        MaximizeBox = false;
        ShowInTaskbar = true;
        panel.Dock = DockStyle.Fill;
        Controls.Add(panel);
        ClientSize = panel.ContentSize;
        FitContent();
    }

    // Height follows rows/DPI/settings, never the user's resize gesture.
    // Preserve the chosen width when a new telemetry sample arrives.
    public void FitContent()
    {
        if (fitting || IsDisposed) return;
        fitting = true;
        try
        {
            var content = panel.ContentSize;
            var outer = SizeFromClientSize(new Size(panel.MinimumContentWidth, content.Height));
            if (MinimumSize == outer && ClientSize.Height == content.Height) return;
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = new Size(Math.Max(ClientSize.Width, panel.MinimumContentWidth), content.Height);
            MinimumSize = outer;
            MaximumSize = new Size(32767, outer.Height);
        }
        finally { fitting = false; }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke((Action)FitContent);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84) return; // WM_NCHITTEST
        m.Result = (int)m.Result switch
        {
            12 or 15 => (IntPtr)18, // top/bottom: ordinary border
            13 or 16 => (IntPtr)10, // left corners: horizontal resize
            14 or 17 => (IntPtr)11, // right corners: horizontal resize
            _ => m.Result
        };
    }
}
