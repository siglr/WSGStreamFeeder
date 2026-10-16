using System.Drawing;
using WsgStreamFeeder;

static class WindowPlacementChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAILED: " + label);
            count++;
            Console.WriteLine("PASS: " + label);
        }
        var primary = new Rectangle(0, 0, 1920, 1040);
        var left = new Rectangle(-1920, 0, 1920, 1040);
        var minimum = new Size(1020, 760);
        var placement = new WindowPlacement(-1700, 80, 1200, 900, true);
        Check(placement.Fit(minimum, [primary, left], primary) == new Rectangle(-1700, 80, 1200, 900), "position and size retained on secondary monitor with negative coordinates");
        Check(placement.Fit(minimum, [primary], primary) == new Rectangle(0, 80, 1200, 900), "removed monitor returns window to visible primary working area");
        Check(new WindowPlacement(1800, 1000, 2200, 1500, false).Fit(minimum, [primary], primary) == primary, "oversized and off-screen window fits current working area");
        Check(new WindowPlacement(20, 30, 200, 100, false).Fit(minimum, [primary], primary).Size == minimum, "main window respects minimum size");
        Check(placement.Fit(new Size(200, 400), [primary, left], primary, 450) == new Rectangle(-1700, 80, 1200, 450), "capture restores width but uses current telemetry height");
        var small = new Rectangle(0, 0, 800, 600);
        Check(placement.Fit(minimum, [small], small).Location == Point.Empty, "title bar reachable on a screen smaller than minimum size");
        Check(!new WindowPlacement(int.MaxValue, 0, 1200, 900, false).IsValid && !new WindowPlacement(0, 0, -1, 900, false).IsValid, "invalid stored coordinates and dimensions rejected");
        var directory = Path.Combine(Path.GetTempPath(), "WSG-placement-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "window.json");
        try
        {
            Check(WindowPlacement.Load(path) == null, "first launch uses default window placement");
            placement.Save(path);
            Check(WindowPlacement.Load(path) == placement, "position, size and maximized preference survive file round trip");
            File.WriteAllText(path, "{}");
            Check(WindowPlacement.Load(path) == null, "incomplete placement falls back to defaults");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return count;
    }
}
