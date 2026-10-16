namespace WsgStreamFeeder;

public sealed record GaugeScale(double Position, bool Warning = false, bool LowOverflow = false, bool HighOverflow = false);
public static class GaugeScales
{
    public static GaugeScale? Calculate(string id, double? raw, FeederPreferences preferences, AircraftMetadata? metadata)
    {
        if (raw is not { } v || !double.IsFinite(v)) return null;
        switch (id)
        {
            case "altitude_agl": return new(Math.Clamp(Math.Sqrt(Math.Max(0, v / 609.6)), 0, 1), false, v < 0, v > 609.6);
            case "tas" when metadata?.VneTasKph is > 0 and var vne && double.IsFinite(vne):
                return new(Math.Clamp(v / (vne / 3.6 * 1.1), 0, 1), v > vne / 3.6, false, v > vne / 3.6 * 1.1);
            // Display ranges only: a TAS limit is not an IAS or groundspeed limit.
            case "ias":
                var iasRange = metadata?.VneTasKph is > 0 and var reference && double.IsFinite(reference) ? reference * 1.1 / 3.6 : 300 / 3.6;
                return new(Math.Clamp(v / iasRange, 0, 1), false, v < 0, v > iasRange);
            case "speed": return new(Math.Clamp(v / (400 / 3.6), 0, 1), false, v < 0, v > 400 / 3.6);
            case "maccready": return new(Math.Clamp(v / 5, 0, 1), false, v < 0, v > 5);
            case "weight" when metadata?.MaxWeightKg is > 0 and var maximum && double.IsFinite(maximum):
                return new(Math.Clamp((v - maximum * .45) / (maximum * .55), 0, 1), v > maximum);
            case "vspeed":
                var visual = Math.Round(Readings.Convert(v, "Vertical", preferences), preferences.VerticalSpeed == "ft/min" ? 0 : 1) == 0 ? 0 : v;
                return new(.5 + Math.Sign(visual) * Math.Sqrt(Math.Min(Math.Abs(visual), 5) / 5) / 2, false, v < -5, v > 5);
            default: return null;
        }
    }
}
