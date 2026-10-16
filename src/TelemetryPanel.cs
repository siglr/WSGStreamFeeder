using System.Drawing.Drawing2D;

namespace WsgStreamFeeder;

internal sealed class TelemetryPanel : Control
{
    public Palette Palette { get; set; } = Palette.Night;
    public FeederPreferences Preferences { get; set; } = new();
    public Dictionary<string, Reading> Readings { get; set; } = new();
    public AircraftMetadata? Metadata { get; set; }
    public string Aircraft { get; set; } = "Your glider";
    public string State { get; set; } = "WAITING FOR SIMULATOR";
    public bool Preview { get; set; }
    public double FlapCount { get; set; }
    public TelemetryPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = "Glider telemetry capture panel";
        AccessibleRole = AccessibleRole.Graphic;
    }
    private IEnumerable<string> VisibleChannels => PanelPresentation.Rows(Preferences.PanelChannels, Metadata?.HasFlaps);
    private float ContentScale => Preferences.PanelScale / 100f * DeviceDpi / 96f;
    public Size ContentSize => new((int)Math.Ceiling(280 * ContentScale), (int)Math.Ceiling((72 + Math.Max(1, VisibleChannels.Count()) * 23) * ContentScale));
    public int MinimumContentWidth => (int)Math.Ceiling(240 * ContentScale);
    public void RefreshSize() => Size = ContentSize;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Background);
        var scale = ContentScale;
        g.ScaleTransform(scale, scale);
        var height = Height / scale;
        var width = Width / scale;
        var right = width - 10;
        using var shape = RoundedShape.Create(new Rectangle(0, 0, (int)width - 1, (int)height - 1), 8);
        using var background = new SolidBrush(Palette.Sidebar);
        using var outline = new Pen(Palette.Line);
        g.FillPath(background, shape);
        g.DrawPath(outline, shape);
        using var titleFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var font = new Font("Segoe UI", 8);
        using var valueFont = new Font("Segoe UI", 9, FontStyle.Bold);
        using var small = new Font("Segoe UI", 7, FontStyle.Bold);
        DrawText(g, "WSG", small, Palette.Accent, new RectangleF(10, 5, 32, 15));
        DrawText(g, Preview ? "PREVIEW • PANEL ONLY" : State, small, Preview ? Palette.Focus : Palette.Muted, new RectangleF(45, 5, right - 45, 15), true);
        DrawText(g, Metadata?.DisplayName ?? Aircraft, titleFont, Palette.Text, new RectangleF(10, 20, right - 10, 23));
        g.DrawLine(outline, 10, 47, right, 47);
        var y = 51f;
        foreach (var id in VisibleChannels)
        {
            var reading = Readings.GetValueOrDefault(id) ?? new Reading(null, "N/A", "");
            DrawText(g, PanelPresentation.Label(id), font, Palette.Muted, new RectangleF(10, y, 52, 23));
            var rail = new RectangleF(66, y + 3, right - 66, 17);
            if (id is "wind" or "heading" or "track")
            {
                var text = id == "wind" ? PanelPresentation.Wind(Readings) : reading.Value != null ? reading.Text + "°" : "N/A";
                var bearing = id == "wind" ? Readings.GetValueOrDefault("wind_direction")?.Value : reading.Value;
                var textWidth = g.MeasureString(text, valueFont).Width;
                var textLeft = Math.Max(86, right - 2 - textWidth);
                if (PanelPresentation.ArrowBearing(id, bearing) is { } direction)
                {
                    var state = g.Save();
                    g.TranslateTransform(textLeft - 10, y + 11.5f);
                    g.RotateTransform((float)direction);
                    using var arrow = new Pen(Color.FromArgb(125, 211, 252), 1.8f);
                    g.DrawLine(arrow, 0, 7, 0, -7);
                    g.DrawLine(arrow, 0, -7, -4, -2);
                    g.DrawLine(arrow, 0, -7, 4, -2);
                    g.Restore(state);
                }
                DrawText(g, text, valueFont, Palette.Text, new RectangleF(textLeft, y, right - 2 - textLeft, 23), true);
            }
            else
            {
                var gauge = GaugeScales.Calculate(id, reading.Value, Preferences, Metadata);
                if (gauge != null) PaintRail(g, rail, id, gauge);
                var flapLadder = id == "flaps" && reading.Value is { } flap && FlapCount >= 2 && flap >= 0 && flap < FlapCount;
                if (flapLadder) PaintFlaps(g, rail, (int)reading.Value!.Value + 1, (int)FlapCount, small);
                else DrawText(g, reading.Display, valueFont, gauge?.Warning == true ? Color.White : Palette.Text, new RectangleF(66, y, right - 68, 23), true);
            }
            y += 23;
        }
        if (!VisibleChannels.Any())
            DrawText(g, "Choose values in Settings", font, Palette.Muted, new RectangleF(10, y, right - 10, 23));
        DrawText(g, Preview ? "WE SIM GLIDE / SAMPLE DATA" : Metadata == null ? "Aircraft limits unavailable" : "WE SIM GLIDE / LIVE TELEMETRY", small, Palette.Muted, new RectangleF(10, height - 19, right - 10, 15));
    }
    private void PaintRail(Graphics g, RectangleF rail, string id, GaugeScale scale)
    {
        using var background = new SolidBrush(Palette.Dark ? Color.FromArgb(4, 10, 18) : Color.FromArgb(223, 233, 240));
        using var outline = new Pen(Palette.Line);
        g.FillRectangle(background, rail);
        var end = rail.Left + rail.Width * (float)scale.Position;
        var green = Color.FromArgb(42, 169, 101); var amber = Color.FromArgb(232, 161, 36); var red = Color.FromArgb(216, 58, 58);
        if (id == "vspeed")
        {
            var center = rail.Left + rail.Width / 2;
            if (Math.Abs(end - center) > .01)
            {
                var color = end < center ? red : green;
                using var gradient = new LinearGradientBrush(new PointF(rail.Left, 0), new PointF(rail.Right, 0), end < center ? color : Color.Transparent, end < center ? Color.Transparent : color);
                g.FillRectangle(gradient, Math.Min(center, end), rail.Top, Math.Abs(end - center), rail.Height);
            }
            g.DrawLine(outline, center, rail.Top, center, rail.Bottom);
        }
        else if (id is "weight" or "ias" or "speed" or "maccready" || id == "tas" && scale.Warning)
        {
            using var fill = new SolidBrush(scale.Warning ? red : Color.FromArgb(37, 139, 210));
            g.FillRectangle(fill, rail.Left, rail.Top, end - rail.Left, rail.Height);
        }
        else
        {
            var bounds = id == "tas" ? new[] { 0d, .9 / 1.1, 1 / 1.1, 1d } : new[] { 0d, Math.Sqrt(.1), .5, 1d };
            var colors = id == "tas" ? new[] { green, amber, red } : new[] { red, amber, green };
            for (var i = 0; i < 3; i++)
            {
                var left = rail.Left + rail.Width * (float)bounds[i];
                var right = Math.Min(end, rail.Left + rail.Width * (float)bounds[i + 1]);
                if (right <= left) continue;
                using var fill = new SolidBrush(colors[i]);
                g.FillRectangle(fill, left, rail.Top, right - left, rail.Height);
            }
        }
        g.DrawRectangle(outline, rail.X, rail.Y, rail.Width, rail.Height);
        if (id == "tas") g.DrawLine(outline, rail.Left + rail.Width / 1.1f, rail.Top, rail.Left + rail.Width / 1.1f, rail.Bottom);
        if (id != "weight")
        {
            var x = Math.Clamp(end, rail.Left + 1, rail.Right - 1);
            using var dark = new Pen(Color.Black, 4); using var light = new Pen(Color.White, 2);
            g.DrawLine(dark, x, rail.Top + 1, x, rail.Bottom - 1); g.DrawLine(light, x, rail.Top + 1, x, rail.Bottom - 1);
            using var triangle = new SolidBrush(Palette.Text);
            if (scale.LowOverflow) g.FillPolygon(triangle, new PointF[] { new(rail.Left, rail.Top + rail.Height / 2), new(rail.Left + 5, rail.Top + 2), new(rail.Left + 5, rail.Bottom - 2) });
            if (scale.HighOverflow) g.FillPolygon(triangle, new PointF[] { new(rail.Right, rail.Top + rail.Height / 2), new(rail.Right - 5, rail.Top + 2), new(rail.Right - 5, rail.Bottom - 2) });
        }
    }
    private void PaintFlaps(Graphics g, RectangleF rail, int position, int count, Font font)
    {
        using var background = new SolidBrush(Palette.Panel);
        using var active = new SolidBrush(Color.FromArgb(38, 117, 216));
        using var border = new Pen(Palette.Line);
        g.FillRectangle(background, rail);
        if (count <= 12)
        {
            var width = rail.Width / count;
            for (var column = 0; column < count; column++)
            {
                var index = count - column;
                var cell = new RectangleF(rail.Left + column * width, rail.Top, width, rail.Height);
                if (index == position) g.FillRectangle(active, cell);
                g.DrawRectangle(border, cell.X, cell.Y, cell.Width, cell.Height);
                var label = Metadata?.FlapLabels?.GetValueOrDefault(index) ?? index.ToString();
                DrawText(g, label, font, index == position ? Color.White : Palette.Muted, cell, center: true);
            }
        }
        else
        {
            var width = rail.Width / 12;
            var x = rail.Left + (float)(count - position) / (count - 1) * (rail.Width - width);
            g.FillRectangle(active, x, rail.Top, width, rail.Height);
            DrawText(g, (Metadata?.FlapLabels?.GetValueOrDefault(position) ?? position.ToString()) + $" · {position}/{count}", font, Palette.Text, rail, true);
        }
        g.DrawRectangle(border, rail.X, rail.Y, rail.Width, rail.Height);
    }
    private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rectangle, bool right = false, bool center = false)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = center ? StringAlignment.Center : right ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, rectangle, format);
    }
}
