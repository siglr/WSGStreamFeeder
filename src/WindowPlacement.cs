using System.Drawing;
using System.Text.Json;

namespace WsgStreamFeeder;

internal sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Width is > 0 and <= 100000 && Height is > 0 and <= 100000
        && X is >= -1000000 and <= 1000000 && Y is >= -1000000 and <= 1000000;

    // Keep negative coordinates on monitors left/above the primary display.
    public Rectangle Fit(Size minimum, Rectangle[] workingAreas, Rectangle primary, int? fixedHeight = null)
    {
        var saved = new Rectangle(X, Y, Width, Height);
        var area = primary;
        long largest = 0;
        foreach (var candidate in workingAreas)
        {
            var overlap = Rectangle.Intersect(saved, candidate);
            var pixels = (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
            if (pixels > largest) { area = candidate; largest = pixels; }
        }
        var width = Math.Max(minimum.Width, Math.Min(Width, area.Width));
        var height = fixedHeight ?? Math.Max(minimum.Height, Math.Min(Height, area.Height));
        // If the minimum/content size exceeds the screen, keep the title bar accessible.
        return new Rectangle(Math.Clamp(X, area.Left, area.Left + Math.Max(0, area.Width - width)),
            Math.Clamp(Y, area.Top, area.Top + Math.Max(0, area.Height - height)), width, height);
    }

    public static WindowPlacement? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var saved = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path));
        return saved is { IsValid: true } ? saved : null;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
