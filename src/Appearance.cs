using System.Drawing.Drawing2D;

namespace WsgStreamFeeder;

// The palette comes from the installed WSG Client's Web/styles.css.
internal sealed record Palette(Color Background, Color Sidebar, Color Panel, Color Button,
    Color Line, Color Text, Color Muted, Color Accent, Color Secondary, Color Focus, bool Dark)
{
    private static Color C(string value) => ColorTranslator.FromHtml(value);
    public static Palette Night { get; } = new(C("#0d1418"), C("#111c21"), C("#17242a"), C("#20323a"),
        C("#304a53"), C("#edf7f6"), C("#a8bcc4"), C("#78d8b7"), C("#9db7ff"), C("#f7dd8a"), true);
    public static Palette Day { get; } = new(C("#edf3f7"), C("#e5eef3"), C("#ffffff"), C("#eef4f7"),
        C("#a9bdc8"), C("#183142"), C("#586f7d"), C("#168060"), C("#3e63a8"), C("#a86600"), false);
}

internal static class ThemePreference
{
    private static string FilePath => Path.Combine(Path.GetDirectoryName(Log.FilePath)!, "theme.txt");
    public static bool IsLight()
    {
        try { return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "Light"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static void Save(bool light)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, light ? "Light" : "Dark");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log.Write("Unable to save appearance preference: " + ex.Message); }
    }
}

internal static class RoundedShape
{
    public static GraphicsPath Create(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class Surface : Panel
{
    public Color BorderColor { get; set; }
    public Surface() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        if (Width < 16 || Height < 16) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = RoundedShape.Create(new Rectangle(0, 0, Width - 1, Height - 1), 7);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor);
        e.Graphics.FillPath(brush, shape);
        e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class WsgButton : Button
{
    public Palette Palette { get; set; } = Palette.Night;
    public bool Primary { get; init; }
    public bool Selected { get; set; }
    public bool Centered { get; init; }
    private bool hovered;
    private bool pressed;
    public WsgButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10, FontStyle.Bold);
        UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Sidebar);
        if (Width < 16 || Height < 16) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = Primary ? Palette.Accent : Selected
            ? (Palette.Dark ? Color.FromArgb(35, 53, 79) : Color.FromArgb(215, 231, 239)) : Palette.Button;
        if (hovered) fill = ControlPaint.Light(fill, .08f);
        if (pressed) fill = ControlPaint.Dark(fill, .08f);
        using var shape = RoundedShape.Create(new Rectangle(1, 1, Width - 3, Height - 3), 5);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Focused ? Palette.Focus : Selected ? Palette.Secondary : Palette.Line, Focused ? 2 : 1);
        e.Graphics.FillPath(brush, shape);
        e.Graphics.DrawPath(pen, shape);
        var color = Primary ? (Palette.Dark ? Palette.Background : Color.White) : Palette.Text;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(12, 0, Width - 24, Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            (Centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
    }
}

internal sealed class WsgMenuColors(Palette palette) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => palette.Sidebar;
    public override Color MenuBorder => palette.Line;
    public override Color MenuItemBorder => palette.Line;
    public override Color MenuItemSelected => palette.Button;
    public override Color MenuItemSelectedGradientBegin => palette.Button;
    public override Color MenuItemSelectedGradientEnd => palette.Button;
    public override Color SeparatorDark => palette.Line;
    public override Color SeparatorLight => palette.Line;
}
