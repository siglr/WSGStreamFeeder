using System.Globalization;
using System.Runtime.InteropServices;

namespace WsgStreamFeeder;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FlightData
{
    public double Altitude, IndicatedAltitude, Heading, GroundSpeed, VerticalSpeed;
    public double Agl, Tas, Ias, WindX, WindY, WindZ, Weight, FlapIndex, FlapCount;
    public double GroundAltitude, Temperature, Pressure, Track, Wingspan;
    public double OnGround, Gear, Slew, SimTime, SimRate, CameraState;

    // Field order and units are the wire contract, checked against structure offsets in tests.
    public static readonly (string Field, string Name, string Unit)[] Definition =
    [
        (nameof(Altitude), "PLANE ALTITUDE", "meters"),
        (nameof(IndicatedAltitude), "INDICATED ALTITUDE", "meters"),
        (nameof(Heading), "PLANE HEADING DEGREES TRUE", "degrees"),
        (nameof(GroundSpeed), "GROUND VELOCITY", "meters per second"),
        (nameof(VerticalSpeed), "VERTICAL SPEED", "meters per second"),
        (nameof(Agl), "PLANE ALT ABOVE GROUND", "meters"),
        (nameof(Tas), "AIRSPEED TRUE", "meters per second"),
        (nameof(Ias), "AIRSPEED INDICATED", "meters per second"),
        (nameof(WindX), "AMBIENT WIND X", "meters per second"),
        (nameof(WindY), "AMBIENT WIND Y", "meters per second"),
        (nameof(WindZ), "AMBIENT WIND Z", "meters per second"),
        (nameof(Weight), "TOTAL WEIGHT", "kilograms"),
        (nameof(FlapIndex), "FLAPS HANDLE INDEX", "number"),
        (nameof(FlapCount), "FLAPS NUM HANDLE POSITIONS", "number"),
        (nameof(GroundAltitude), "GROUND ALTITUDE", "meters"),
        (nameof(Temperature), "AMBIENT TEMPERATURE", "celsius"),
        (nameof(Pressure), "AMBIENT PRESSURE", "millibars"),
        (nameof(Track), "GPS GROUND TRUE TRACK", "degrees"),
        (nameof(Wingspan), "WING SPAN", "meters"),
        (nameof(OnGround), "SIM ON GROUND", "bool"),
        (nameof(Gear), "GEAR HANDLE POSITION", "bool"),
        (nameof(Slew), "IS SLEW ACTIVE", "bool"),
        (nameof(SimTime), "SIMULATION TIME", "seconds"),
        (nameof(SimRate), "SIMULATION RATE", "number"),
        (nameof(CameraState), "CAMERA STATE", "number")
    ];
}

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public struct AircraftIdentity
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Title;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Model;
}

// Filled by the optional aircraft catalog lookup. Null limits never block live measurements.
public sealed record AircraftMetadata(string? DisplayName = null, double? VneTasKph = null,
    double? MaxWeightKg = null, IReadOnlyDictionary<int, string>? FlapLabels = null, bool? HasFlaps = null, int? FlapCount = null, string? GliderKey = null);

public sealed record Channel(string Id, string Name, string File, string Category, string Description);
public static class Channels
{
    public static readonly Channel[] All =
    [
        new("altitude_asl", "Altitude ASL", "altitude_asl.txt", "Altitude", "Geometric altitude above sea level"),
        new("altitude_agl", "Altitude AGL", "altitude_agl.txt", "Altitude", "Height above the surface below, including obstacles reported by MSFS"),
        new("ground_native", "Ground elevation", "ground_altitude.txt", "Altitude", "Direct MSFS ground elevation; usually overlaps GRND"),
        new("heading", "True heading", "heading_true.txt", "Angle", "Aircraft nose direction relative to true north"),
        new("track", "Ground track", "track_true.txt", "Angle", "GPS direction of travel relative to true north"),
        new("speed", "Ground speed", "speed.txt", "Airspeed", "Speed over the ground; blue 0–400 km/h display range, not a speed limit"),
        new("ias", "Indicated airspeed", "airspeed_indicated.txt", "Airspeed", "IAS; blue range matches TAS scale (300 km/h without reference); no TAS VNE warning"),
        new("tas", "True airspeed", "airspeed_true.txt", "Airspeed", "TAS; catalog VNE marker, amber above 90%, red bar above VNE; reference required"),
        new("vspeed", "Vertical speed", "vspeed.txt", "Vertical", "MSFS vertical speed; centered climb/descent gauge with ±5 m/s display range; not TE/netto"),
        new("wind_speed", "Wind speed", "wind_speed.txt", "Wind", "Horizontal wind from X/Z components"),
        new("wind_direction", "Wind from", "wind_direction.txt", "Angle", "True bearing wind comes from; arrow points downwind"),
        new("netto", "Netto (NB21)", "netto.txt", "Vertical", "MSFS AMBIENT WIND Y, the source of NB21's IGC NET field; centered square-root ±5 m/s gauge; not an aircraft-specific instrument reading"),
        new("weight", "Total weight", "weight.txt", "Weight", "Current total aircraft mass; not ballast quantity"),
        new("flaps", "Flaps", "flaps.txt", "Index", "Catalog label, otherwise native zero-based handle index"),
        new("temperature", "Temperature", "temperature.txt", "Temperature", "Ambient air temperature"),
        new("pressure", "Pressure", "pressure.txt", "Pressure", "Ambient air pressure"),
        new("gear", "Landing gear", "gear.txt", "State", "Gear handle position"),
        new("on_ground", "Flight state", "flight_state.txt", "State", "Simulator on-ground state"),
        new("sim_rate", "Simulation rate", "simulation_rate.txt", "Rate", "Simulator speed multiplier")
    ];
    public static readonly string[] DefaultFiles = ["altitude_asl", "heading", "speed", "vspeed"];
    public static readonly string[] DefaultPanel = ["ground_native", "altitude_asl", "altitude_agl", "tas", "vspeed", "wind_speed", "wind_direction", "weight", "flaps"];
}

public sealed record Reading(double? Value, string Text, string Unit)
{
    public string Display => Text == "N/A" || Unit.Length == 0 ? Text : Text + " " + Unit;
}

public sealed class FlightModel
{
    public FlightData? Latest { get; private set; }
    public void Reset() => Latest = null;
    public void Accept(FlightData data) => Latest = data;
}

public static class Readings
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    public static Dictionary<string, Reading> Create(FlightData? sample, FeederPreferences preferences, AircraftMetadata? metadata = null)
    {
        var result = new Dictionary<string, Reading>();
        foreach (var channel in Channels.All)
        {
            double? value = null;
            if (sample is { } d)
            {
                value = channel.Id switch
                {
                    "altitude_asl" => d.Altitude, "altitude_agl" => d.Agl,
                    "ground_native" => d.GroundAltitude,
                    "heading" => d.Heading, "track" => d.Track, "speed" => d.GroundSpeed,
                    "ias" => d.Ias, "tas" => d.Tas, "vspeed" => d.VerticalSpeed,
                    "wind_speed" => Math.Sqrt(d.WindX * d.WindX + d.WindZ * d.WindZ),
                    "wind_direction" => d.WindX == 0 && d.WindZ == 0 ? null : (Math.Atan2(d.WindX, d.WindZ) * 180 / Math.PI + 540) % 360,
                    "netto" => d.WindY, "weight" => d.Weight,
                    "flaps" => metadata?.HasFlaps != false && d.FlapCount > 1 && d.FlapIndex >= 0 && d.FlapIndex < (metadata?.FlapCount ?? d.FlapCount) && d.FlapIndex == Math.Truncate(d.FlapIndex) ? d.FlapIndex : null,
                    "temperature" => d.Temperature,
                    "pressure" => d.Pressure,
                    "gear" => d.Gear, "on_ground" => d.OnGround, "sim_rate" => d.SimRate,
                    _ => null
                };
            }
            if (value is { } invalid && !double.IsFinite(invalid)) value = null;
            if (value < 0 && channel.Id is "ias" or "tas" or "speed" or "wind_speed") value = null;
            if (value <= 0 && channel.Id is "weight" or "pressure" or "sim_rate") value = null;
            var unit = Unit(channel.Category, preferences);
            var text = "N/A";
            if (value is { } raw)
            {
                var display = Convert(raw, channel.Category, preferences);
                text = channel.Category switch
                {
                    "Altitude" or "Weight" => Math.Round(display).ToString("N0", C),
                    "Airspeed" => channel.Id == "speed" ? Math.Round(display).ToString("0", C) : display.ToString("0.#", C),
                    "Wind" => display.ToString(preferences.Wind == "kt" ? "0" : "0.0", C),
                    "Vertical" => Math.Round(display, preferences.VerticalSpeed == "ft/min" ? 0 : 1).ToString(preferences.VerticalSpeed == "ft/min" ? "N0" : "0.0", C),
                    "Angle" => (((Math.Round(raw) % 360) + 360) % 360).ToString("0", C),
                    "State" => channel.Id == "gear" ? (raw != 0 ? "Down" : "Up") : (raw != 0 ? "On ground" : "Airborne"),
                    "Index" => metadata?.FlapLabels?.TryGetValue((int)raw + 1, out var label) == true ? label : ((int)raw).ToString(C),
                    _ => display.ToString("0.0", C)
                };
                if (channel.Id == "flaps" && !(metadata?.FlapLabels?.ContainsKey((int)raw + 1) ?? false)) unit = "index";
                if (channel.Id == "vspeed")
                {
                    var rounded = Math.Round(display, preferences.VerticalSpeed == "ft/min" ? 0 : 1);
                    text = rounded > 0 ? "▴ " + text : rounded < 0 ? "▾ " + text.TrimStart('-') : "0";
                }
                else if (channel.Id == "netto")
                {
                    var rounded = Math.Round(display, preferences.VerticalSpeed == "ft/min" ? 0 : 1);
                    if (rounded == 0) text = preferences.VerticalSpeed == "ft/min" ? "0" : "0.0";
                    else if (rounded > 0) text = "+" + text;
                }
            }
            result[channel.Id] = new Reading(value, text, unit);
        }
        return result;
    }
    public static string Unit(string category, FeederPreferences p) => category switch
    {
        "Altitude" => p.Altitude, "Airspeed" => p.Airspeed, "Vertical" => p.VerticalSpeed,
        "Wind" => p.Wind, "Weight" => p.Weight, "Temperature" => p.Temperature,
        "Pressure" => p.Pressure, "Length" => p.Length, "Angle" => "°", "Rate" => "×", _ => ""
    };
    public static double Convert(double value, string category, FeederPreferences p) => Unit(category, p) switch
    {
        "ft" => value / .3048, "kt" => value * 3600 / 1852, "km/h" => value * 3.6,
        "mph" => value * 3600 / 1609.344, "ft/min" => value * 60 / .3048,
        "lb" => value / .45359237, "°F" => value * 1.8 + 32,
        "inHg" => value / 33.8638866667, _ => value
    };
    public static Dictionary<string, string> Files(Dictionary<string, Reading> readings, FeederPreferences p) =>
        Channels.All.Where(c => p.TextChannels.Contains(c.Id)).ToDictionary(c => c.File, c =>
        {
            var r = readings[c.Id];
            // Degree suffix and the four existing filenames remain compatible by default.
            return p.IncludeUnits ? r.Display : r.Text == "N/A" ? "N/A" : c.Category == "Angle" ? r.Text + "°" : r.Text;
        });
}
