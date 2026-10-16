namespace WsgStreamFeeder;

// Presentation-only grouping: the two wind exports remain independent.
internal static class PanelPresentation
{
    // TITLE can contain a paint/livery name. Use only model identities in the heading.
    public static string AircraftName(string? model, string? catalogName)
    {
        if (!string.IsNullOrWhiteSpace(catalogName)) return catalogName.Trim();
        var name = model?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.StartsWith("TT:", StringComparison.OrdinalIgnoreCase)
            ? "Your glider" : name;
    }

    public static IEnumerable<string> Rows(IEnumerable<string> selected, bool? hasFlaps)
    {
        var windShown = false;
        foreach (var id in selected)
        {
            if (id == "flaps" && hasFlaps == false) continue;
            if (id is "wind_speed" or "wind_direction")
            {
                if (!windShown) { yield return "wind"; windShown = true; }
            }
            else yield return id;
        }
    }

    public static string Wind(IReadOnlyDictionary<string, Reading> readings)
    {
        var direction = readings.GetValueOrDefault("wind_direction");
        var speed = readings.GetValueOrDefault("wind_speed");
        return (direction?.Value != null ? direction.Text + "°" : "N/A") + " @ " + (speed?.Display ?? "N/A");
    }

    public static double? ArrowBearing(string id, double? bearing) => bearing is { } value && double.IsFinite(value)
        ? ((value + (id == "wind" ? 180 : 0)) % 360 + 360) % 360 : null;

    public static string Label(string id) => id switch
    {
        "altitude_asl" => "ASL", "altitude_agl" => "AGL",
        "ground_native" => "ELEV", "heading" => "HDG",
        "track" => "TRK", "speed" => "GS", "ias" => "IAS", "tas" => "TAS",
        "vspeed" => "V/SPD", "wind" => "WIND",
        "wind_vertical" => "AIR ↑", "weight" => "WT", "flaps" => "FLAPS",
        "maccready" => "MC", "temperature" => "TEMP", "pressure" => "PRESS",
        "gear" => "GEAR", "on_ground" => "STATE",
        "sim_rate" => "SIM", _ => id
    };
}
