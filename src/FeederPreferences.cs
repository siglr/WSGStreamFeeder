using System.Text.Json;

namespace WsgStreamFeeder;

public sealed class FeederPreferences
{
    public string Altitude { get; set; } = "ft";
    public string Airspeed { get; set; } = "kt";
    public string VerticalSpeed { get; set; } = "ft/min";
    public string Wind { get; set; } = "kt";
    public string Weight { get; set; } = "kg";
    public string Temperature { get; set; } = "°C";
    public string Pressure { get; set; } = "hPa";
    public string Length { get; set; } = "m";
    public bool IncludeUnits { get; set; }
    public int PanelScale { get; set; } = 100;
    public string[] TextChannels { get; set; } = [.. Channels.DefaultFiles];
    public string[] PanelChannels { get; set; } = [.. Channels.DefaultPanel];
    public static readonly IReadOnlyDictionary<string, string[]> Units = new Dictionary<string, string[]>
    {
        [nameof(Altitude)] = ["ft", "m"], [nameof(Airspeed)] = ["kt", "km/h", "mph", "m/s"],
        [nameof(VerticalSpeed)] = ["ft/min", "m/s", "kt"], [nameof(Wind)] = ["kt", "km/h", "mph", "m/s"],
        [nameof(Weight)] = ["kg", "lb"], [nameof(Temperature)] = ["°C", "°F"],
        [nameof(Pressure)] = ["hPa", "inHg"]
    };
    public void Normalize()
    {
        var defaults = new FeederPreferences();
        foreach (var (name, choices) in Units)
        {
            var property = typeof(FeederPreferences).GetProperty(name)!;
            if (!choices.Contains(property.GetValue(this) as string)) property.SetValue(this, property.GetValue(defaults));
        }
        var ids = Channels.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        TextChannels = (TextChannels ?? defaults.TextChannels).Select(MigrateChannel).Where(ids.Contains).Distinct().ToArray();
        PanelChannels = (PanelChannels ?? defaults.PanelChannels).Select(MigrateChannel).Where(ids.Contains).Distinct().ToArray();
        PanelScale = Math.Clamp(PanelScale, 75, 175);
    }
    private static string MigrateChannel(string id) => id switch
    {
        "altitude" => "altitude_asl", "ground_asl" => "ground_native", "climb" => "vspeed", _ => id
    };
    public static FeederPreferences Load(string path, out string? error)
    {
        error = null;
        try
        {
            var result = File.Exists(path) ? JsonSerializer.Deserialize<FeederPreferences>(File.ReadAllText(path)) ?? new() : new();
            result.Normalize();
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Could not read telemetry settings; defaults loaded. " + ex.Message;
            return new();
        }
    }
    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
