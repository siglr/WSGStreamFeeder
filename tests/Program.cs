using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WsgStreamFeeder;

var count = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    count++;
    Console.WriteLine("PASS: " + description);
}
FlightData Sample(double altitude = 12345.6, double heading = 273.6, double speed = 123.6, double vs = 1234.6) => new()
{
    Altitude = altitude * .3048,
    IndicatedAltitude = altitude * .3048,
    Heading = heading,
    GroundSpeed = speed * 1852 / 3600,
    VerticalSpeed = vs * .3048 / 60
};

Check(TestOutput.Format(Sample()) == new TestOutput("12,346", "274°", "124", "▴ 1,235"), "vertical speed formatting and geometric ASL altitude");
Check(TestOutput.Format(Sample(vs: -1234.6)).VerticalSpeed == "▾ 1,235", "descent uses down triangle and positive magnitude");
Check(TestOutput.Format(Sample(vs: -0.4)).VerticalSpeed == "0", "near-zero vertical speed has no arrow");
Check(TestOutput.Format(Sample(altitude: 12344.5, heading: 272.5, speed: 122.5, vs: 1234.5)) == new TestOutput("12,344", "272°", "122", "▴ 1,234"), "midpoints round to even");
Check(TestOutput.Format(Sample(altitude: double.NaN)).Altitude == "N/A", "missing geometric altitude remains unavailable");
Check(TestOutput.Format(Sample(speed: double.NaN)) .Speed == "N/A", "invalid speed yields N/A independently");
Check(TestOutput.Format(Sample(heading: double.NaN)).Heading == "N/A", "invalid heading does not leak NaN into OBS");
Check(TestOutput.Format(Sample(vs: double.NaN)).VerticalSpeed == "N/A", "missing vertical speed is unavailable");
Check(TestOutput.Format(Sample(altitude: -150.6)).Altitude == "-151", "negative altitude is retained");
Check(TestOutput.Format(Sample(heading: 359.8)).Heading == "0°", "heading wraps whole-degree rounding at north");
var originalCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    Check(TestOutput.Format(Sample()) == new TestOutput("12,346", "274°", "124", "▴ 1,235"), "OBS formatting stays stable across system locales");
}
finally { CultureInfo.CurrentCulture = originalCulture; }

var root = Path.Combine(Path.GetTempPath(), "WSG Stream Feeder-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var writer = new OutputWriter(root);
    var sample = TestOutput.Format(Sample());
    Check(writer.Write(sample.Files()) == null, "creates output folder and four files");
    Check(Directory.GetFiles(root).Select(Path.GetFileName).Order().SequenceEqual(new[] { "altitude_asl.txt", "heading_true.txt", "speed.txt", "vspeed.txt" }.Order()), "exactly the four requested files are produced");
    foreach (var (name, content) in sample.Files())
    {
        Check(File.ReadAllBytes(Path.Combine(root, name)).SequenceEqual(new UTF8Encoding(false).GetBytes(content)), name + " is exact UTF-8 without BOM or newline");
    }
    var altitudePath = Path.Combine(root, "altitude_asl.txt");
    var timestamp = DateTime.UtcNow.AddDays(-1);
    File.SetLastWriteTimeUtc(altitudePath, timestamp);
    var actualTimestamp = File.GetLastWriteTimeUtc(altitudePath);
    Check(writer.Write(sample.Files()) == null && File.GetLastWriteTimeUtc(altitudePath) == actualTimestamp, "unchanged data does not rewrite OBS files");
    File.Delete(Path.Combine(root, "heading_true.txt"));
    Check(writer.Write(sample.Files()) == null && File.ReadAllText(Path.Combine(root, "heading_true.txt")) == "274°", "deleted heading file is recreated");
    var updated = sample with { Altitude = "15,000" };
    using (var locked = new FileStream(altitudePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Check(writer.Write(updated.Files())?.Contains("altitude_asl.txt") == true, "reader without delete sharing yields a visible retryable error");
        Check(File.ReadAllText(altitudePath) == sample.Altitude, "failed replacement preserves the previous complete value");
    }
    Check(writer.Write(updated.Files()) == null && File.ReadAllText(altitudePath) == "15,000", "retry after file lock release succeeds");
    Check(!Directory.GetFiles(root, "*.tmp").Any(), "temporary files are cleaned up after write failure");
    File.WriteAllText(Path.Combine(root, "unrelated.txt"), "leave me alone");
    Check(writer.Write(TestOutput.Unavailable.Files()) == null && TestOutput.Unavailable.Files().All(pair => File.ReadAllText(Path.Combine(root, pair.Key)) == "N/A"), "disconnect resets all four files");
    Check(File.ReadAllText(Path.Combine(root, "unrelated.txt")) == "leave me alone", "unrelated output files are untouched");
    var blocker = Path.Combine(root, "blocked-folder");
    File.WriteAllText(blocker, "file instead of directory");
    var blockedWriter = new OutputWriter(blocker);
    Check(blockedWriter.Write(sample.Files()) != null, "invalid output directory is reported");
    File.Delete(blocker);
    Check(blockedWriter.Write(sample.Files()) == null, "output directory failure can recover on a later tick");
}
finally
{
    // Only this test's freshly generated temporary directory is removed.
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
count += await GliderChecks.Run();
Console.WriteLine($"All {count} checks passed.");

// A test-only view of the four default filenames, sourced from the production formatter.
record TestOutput(string Altitude, string Heading, string Speed, string VerticalSpeed)
{
    public static TestOutput Unavailable => new("N/A", "N/A", "N/A", "N/A");
    public static TestOutput Format(FlightData data)
    {
        var preferences = new FeederPreferences();
        var files = Readings.Files(Readings.Create(data, preferences), preferences);
        return new(files["altitude_asl.txt"], files["heading_true.txt"], files["speed.txt"], files["vspeed.txt"]);
    }
    public Dictionary<string, string> Files() => new()
    {
        ["altitude_asl.txt"] = Altitude, ["heading_true.txt"] = Heading,
        ["speed.txt"] = Speed, ["vspeed.txt"] = VerticalSpeed
    };
}
