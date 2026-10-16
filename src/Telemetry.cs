using System.Globalization;
using System.Runtime.InteropServices;

namespace WsgStreamFeeder;

// Order and packing must match the SimConnect data definition exactly.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Telemetry
{
    public double AltitudeFeet;
    public double IndicatedAltitudeFeet;
    public double TrueHeadingDegrees;
    public double GroundSpeedKnots;
    public double VerticalSpeedFeetPerMinute;
}

public sealed record OverlayText(string Altitude, string Heading, string Speed, string VerticalSpeed)
{
    public static readonly OverlayText Unavailable = new("N/A", "N/A", "N/A", "N/A");
    public IEnumerable<KeyValuePair<string, string>> Files()
    {
        yield return new("altitude.txt", Altitude);
        yield return new("heading_true.txt", Heading);
        yield return new("speed.txt", Speed);
        yield return new("vspeed.txt", VerticalSpeed);
    }
}

public static class TelemetryFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static OverlayText Format(Telemetry data)
    {
        // Prefer indicated altitude, falling back to geometric altitude.
        if (!double.IsFinite(data.AltitudeFeet) || !double.IsFinite(data.GroundSpeedKnots))
            return OverlayText.Unavailable;
        var altitude = double.IsFinite(data.IndicatedAltitudeFeet)
            ? data.IndicatedAltitudeFeet : data.AltitudeFeet;
        var heading = double.IsFinite(data.TrueHeadingDegrees)
            ? Math.Round(data.TrueHeadingDegrees).ToString(Culture) + "°" : "N/A";
        var vs = double.IsFinite(data.VerticalSpeedFeetPerMinute)
            ? Math.Round(data.VerticalSpeedFeetPerMinute) : 0;
        var verticalSpeed = vs > 0 ? "▴ " + vs.ToString("N0", Culture)
            : vs < 0 ? "▾ " + Math.Abs(vs).ToString("N0", Culture) : "0";
        return new OverlayText(
            Math.Round(altitude).ToString("N0", Culture), heading,
            Math.Round(data.GroundSpeedKnots).ToString(Culture), verticalSpeed);
    }
}
