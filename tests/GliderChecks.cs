using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using WsgStreamFeeder;

static class GliderChecks
{
    public static async Task<int> Run()
    {
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); count++; Console.WriteLine("PASS: " + label); }
        bool Near(double? a, double b) => a.HasValue && Math.Abs(a.Value - b) < .00001;
        Check(Marshal.SizeOf<AircraftIdentity>() == 512 && Marshal.OffsetOf<AircraftIdentity>(nameof(AircraftIdentity.Model)).ToInt32() == 256, "two identity strings match TITLE and ATC MODEL wire layout");
        var identity = new AircraftIdentity { Title = "Jonker JS3 (18m) DIAMONDS", Model = "JS3-18" };
        Check(PanelPresentation.AircraftName(identity.Model, null) == "JS3-18", "livery-specific title is excluded from the uncataloged glider heading");
        Check(PanelPresentation.AircraftName(identity.Model, "Jonker JS3 (18m)") == "Jonker JS3 (18m)", "catalog model name takes precedence when available");
        Check(PanelPresentation.AircraftName(" JS3-18 ", " ") == "JS3-18", "blank catalog name falls back to trimmed simulator model");
        Check(PanelPresentation.AircraftName(null, null) == "Your glider", "missing model uses neutral heading rather than a livery title");
        Check(PanelPresentation.AircraftName("TT:ATCCOM.AC_MODEL_EXAMPLE.0.text", null) == "Your glider", "unresolved model localization token is not shown");
        Check(Marshal.SizeOf<FlightData>() == 25 * 8 && FlightData.Definition.Length == 25, "25 numeric fields match SimConnect payload size");
        Check(FlightData.Definition.Select((f, i) => Marshal.OffsetOf<FlightData>(f.Field).ToInt32() == i * 8).All(x => x), "every SimConnect field matches its declared wire offset");
        Check(FlightData.Definition.Single(f => f.Field == nameof(FlightData.WindY)) == (nameof(FlightData.WindY), "AMBIENT WIND Y", "meters per second"), "NB21 NET source is requested as ambient vertical wind in metres per second");
        var prefs = new FeederPreferences();
        var d = new FlightData { Altitude = 1000, IndicatedAltitude = 1100, Agl = 152.4, Heading = 359.8, GroundSpeed = 50,
            VerticalSpeed = -2, Tas = 60, Ias = 55, WindX = 3, WindZ = 4, WindY = 20, Weight = 606,
            FlapIndex = 0, FlapCount = 6, SimTime = 100, GroundAltitude = 820, SimRate = 1, Temperature = 20, Pressure = 1013.25, Wingspan = 18 };
        var metadata = new AircraftMetadata("Example", 240, 600, new Dictionary<int, string> { [1] = "N" }, true, 6);
        var r = Readings.Create(d, prefs, metadata);
        Check(Near(r["ground_native"].Value, 820), "ELEV uses native ground elevation rather than inferred ASL minus AGL");
        Check(Near(r["wind_speed"].Value, 5), "horizontal wind excludes vertical wind");
        Check(r["flaps"].Text == "N", "native zero flap index maps to catalog label one exactly once");
        Check(Readings.Create(d, prefs)["flaps"].Display == "0 index", "unknown flap metadata keeps raw zero-based index");
        Check(Readings.Create(d, prefs, metadata with { HasFlaps = false })["flaps"].Text == "N/A", "known unflapped aircraft has no flap value");
        Check(Readings.Create(d, prefs, metadata with { FlapCount = 2 })["flaps"].Text == "N", "partial catalog metadata remains usable");
        Check(PanelPresentation.Rows(["tas", "wind_direction", "weight", "wind_speed", "flaps"], true).SequenceEqual(new[] { "tas", "wind", "weight", "flaps" }), "wind combines at first selected position without reordering other rows");
        Check(PanelPresentation.Rows(["wind_speed", "flaps"], false).SequenceEqual(new[] { "wind" }), "single wind selection shows combined row and unflapped row stays hidden");
        Check(!PanelPresentation.Rows(["tas"], null).Contains("wind"), "unselected wind stays hidden");
        Check(PanelPresentation.Wind(new Dictionary<string, Reading> { ["wind_direction"] = new(90, "90", "°"), ["wind_speed"] = new(6, "12", "kt") }) == "90° @ 12 kt", "combined wind uses compact bearing and selected speed unit");
        Check(PanelPresentation.Wind(Readings.Create(null, prefs)) == "N/A @ N/A", "unavailable wind does not invent a bearing or speed");
        Check(PanelPresentation.Label("altitude_agl") == "AGL" && PanelPresentation.Label("ground_native") == "ELEV" && PanelPresentation.Label("vspeed") == "V/SPD", "compact labels keep vertical speed distinct");
        var invalidFlap = d; invalidFlap.FlapIndex = 5;
        Check(Readings.Create(invalidFlap, prefs, metadata with { FlapCount = 2 })["flaps"].Text == "N/A", "invalid catalog detent is unavailable");
        var invalid = d; invalid.VerticalSpeed = double.NaN; invalid.Ias = double.NaN;
        var invalidReadings = Readings.Create(invalid, prefs);
        Check(invalidReadings["vspeed"].Text == "N/A" && invalidReadings["tas"].Text != "N/A", "missing channels are independent and never silently zero");
        Check(Readings.Create(null, prefs).Values.All(v => v.Text == "N/A"), "disconnected telemetry is unavailable for every channel");
        Check(r["heading"].Text == "0", "heading wraps to zero at north");
        foreach (var (x, z, bearing) in new[] { (0d, -5d, 0d), (-5d, 0d, 90d), (0d, 5d, 180d), (5d, 0d, 270d) })
        {
            var wind = d; wind.WindX = x; wind.WindZ = z;
            Check(Near(Readings.Create(wind, prefs)["wind_direction"].Value, bearing), "cardinal wind-from bearing " + bearing);
        }
        var calm = d; calm.WindX = calm.WindZ = 0;
        Check(Readings.Create(calm, prefs)["wind_direction"].Text == "N/A", "calm wind does not invent a direction");
        Check(Near(Readings.Convert(1, "Altitude", prefs), 1 / .3048), "feet conversion");
        prefs.Altitude = "m"; prefs.Airspeed = "km/h"; prefs.VerticalSpeed = "m/s"; prefs.Wind = "m/s";
        prefs.Weight = "lb"; prefs.Temperature = "°F"; prefs.Pressure = "inHg";
        r = Readings.Create(d, prefs, metadata);
        Check(r["altitude_asl"].Text == "1,000" && r["tas"].Text == "216" && r["temperature"].Text == "68.0", "selected units affect normalized readouts");
        prefs.TextChannels = ["tas", "weight", "altitude_asl"];
        var files = Readings.Files(r, prefs);
        Check(files.Count == 3 && files["airspeed_true.txt"] == r["tas"].Text && !files.ContainsKey("vspeed.txt"), "only selected files use the same formatted values as panel");
        prefs.IncludeUnits = true;
        Check(Readings.Files(r, prefs)["weight.txt"] == r["weight"].Display, "optional file suffix matches panel units");
        Check(Near(GaugeScales.Calculate("altitude_agl", 152.4, prefs, null)?.Position, .5), "500 ft is midpoint of nonlinear AGL rail");
        foreach (var (value, expected) in new[] { (-1.25, .25), (0d, .5), (1.25, .75) })
            Check(Near(GaugeScales.Calculate("vspeed", value, prefs, null)?.Position, expected), "vertical speed rail at " + value);
        Check(Near(GaugeScales.Calculate("vspeed", .01, prefs, null)?.Position, .5), "rounded zero vertical speed centers visual marker");
        Check(GaugeScales.Calculate("vspeed", 5, prefs, null)?.HighOverflow == false && GaugeScales.Calculate("vspeed", 5.01, prefs, null)?.HighOverflow == true, "vertical speed overflow uses strict raw boundary");
        foreach (var (weight, position, warning) in new[] { (270d, 0d, false), (435d, .5, false), (600d, 1d, false), (606d, 1d, true) })
        {
            var scale = GaugeScales.Calculate("weight", weight, prefs, metadata);
            Check(Near(scale?.Position, position) && scale?.Warning == warning, "MTOW rail and strict warning at " + weight);
        }
        Check(GaugeScales.Calculate("tas", 70, prefs, null) == null && GaugeScales.Calculate("weight", 500, prefs, null) == null, "unknown aircraft never gets invented reference scales");
        Check(GaugeScales.Calculate("tas", 240 / 3.6, prefs, metadata)?.Warning == false && GaugeScales.Calculate("tas", 240.001 / 3.6, prefs, metadata)?.Warning == true, "TAS warning uses unrounded value and strict VNE boundary");
        foreach (var (value, expected) in new[] { (-5d, 0d), (-1.25, .25), (0d, .5), (1.25, .75), (5d, 1d) })
            Check(Near(GaugeScales.Calculate("netto", value, prefs, null)?.Position, expected), "NB21 netto uses centered square-root scale at " + value);
        Check(GaugeScales.Calculate("netto", 6, prefs, null) is { Position: 1, Warning: false, HighOverflow: true } && GaugeScales.Calculate("netto", -6, prefs, null) is { Position: 0, LowOverflow: true }, "netto lift and sink beyond range show overflow markers");
        Check(Near(GaugeScales.Calculate("speed", 200 / 3.6, prefs, metadata)?.Position, .5), "groundspeed range is independent of TAS VNE");
        Check(GaugeScales.Calculate("speed", 450 / 3.6, prefs, metadata) is { Warning: false, HighOverflow: true }, "high groundspeed does not invent an overspeed warning");
        Check(Near(GaugeScales.Calculate("ias", 120 / 3.6, prefs, metadata)?.Position, .5 / 1.1), "IAS shares TAS visual range when reference exists");
        Check(GaugeScales.Calculate("ias", 300 / 3.6, prefs, metadata) is { Warning: false, HighOverflow: true }, "IAS does not treat catalog TAS VNE as an IAS redline");
        Check(Near(GaugeScales.Calculate("ias", 150 / 3.6, prefs, null)?.Position, .5), "unknown aircraft still gets neutral IAS bar");
        Check(GaugeScales.Calculate("netto", null, prefs, null) == null, "unavailable netto does not draw a reading");
        var nettoPrefs = new FeederPreferences { VerticalSpeed = "m/s", TextChannels = ["netto"], IncludeUnits = true };
        var nettoSample = d; nettoSample.WindY = -1.5; nettoSample.VerticalSpeed = 3;
        var nettoReadings = Readings.Create(nettoSample, nettoPrefs);
        Check(Near(nettoReadings["netto"].Value, -1.5) && Near(nettoReadings["vspeed"].Value, 3), "netto follows NB21 vertical wind independently of glider vertical speed");
        Check(Readings.Files(nettoReadings, nettoPrefs)["netto.txt"] == "-1.5 m/s" && PanelPresentation.Label("netto") == "NETTO", "netto export and panel share signed value and explicit label");
        foreach (var (unit, expected) in new[] { ("ft/min", "-295 ft/min"), ("kt", "-2.9 kt") })
        {
            nettoPrefs.VerticalSpeed = unit;
            Check(Readings.Files(Readings.Create(nettoSample, nettoPrefs), nettoPrefs)["netto.txt"] == expected, "netto respects selected vertical-rate unit " + unit);
        }
        nettoPrefs.VerticalSpeed = "m/s"; nettoSample.WindY = 6;
        Check(Readings.Create(nettoSample, nettoPrefs)["netto"].Display == "+6.0 m/s", "netto numeric reading remains unclamped above gauge limit");
        nettoSample.WindY = 0;
        Check(Readings.Create(nettoSample, nettoPrefs)["netto"].Text == "0.0", "zero netto remains a valid neutral reading");
        nettoSample.WindY = double.NaN;
        Check(Readings.Create(nettoSample, nettoPrefs)["netto"].Text == "N/A", "invalid netto is unavailable rather than false zero");
        var nettoMigration = new FeederPreferences { PanelChannels = ["vspeed", "maccready", "tas", "wind_vertical"], TextChannels = ["wind_vertical", "maccready", "heading"] };
        nettoMigration.Normalize();
        Check(nettoMigration.PanelChannels.SequenceEqual(new[] { "vspeed", "netto", "tas" }) && nettoMigration.TextChannels.SequenceEqual(new[] { "netto", "heading" }), "MC and vertical-air-mass choices migrate to one NETTO channel preserving order and independent outputs");
        Check(PanelPresentation.ArrowBearing("heading", 90) == 90 && PanelPresentation.ArrowBearing("track", 270) == 270 && PanelPresentation.ArrowBearing("wind", 90) == 270, "HDG/TRK arrows point toward bearing, wind arrow remains downwind");
        Check(PanelPresentation.ArrowBearing("heading", null) == null && PanelPresentation.ArrowBearing("track", double.NaN) == null, "missing bearings never display invented arrows");
        var retired = new FeederPreferences { TextChannels = ["wingspan", "tas"], PanelChannels = ["wingspan", "ias"] };
        retired.Normalize();
        Check(retired.TextChannels.SequenceEqual(new[] { "tas" }) && retired.PanelChannels.SequenceEqual(new[] { "ias" }) && Channels.All.All(c => c.Id != "wingspan"), "old SPAN selections migrate away while aircraft identity still receives wingspan");
        var model = new FlightModel(); model.Accept(d);
        Check(Near(Readings.Create(model.Latest, prefs)["vspeed"].Value, -2), "V/SPD gauge reads the simulator value directly on the first sample");
        var next = d; next.Altitude += 100; next.SimTime += 1; next.VerticalSpeed = -1.25;
        model.Accept(next);
        var liveValues = Readings.Create(model.Latest, prefs);
        Check(Near(liveValues["vspeed"].Value, -1.25) && Near(GaugeScales.Calculate("vspeed", liveValues["vspeed"].Value, prefs, null)?.Position, .25), "V/SPD gauge follows simulator descent even when geometric altitude increases");
        model.Reset(); Check(model.Latest == null, "aircraft change clears measurements");
        var migration = new FeederPreferences { TextChannels = ["altitude", "altitude_asl", "climb", "vspeed", "ground_asl"], PanelChannels = ["ground_asl", "ground_native", "altitude", "altitude_agl", "climb"] };
        migration.Normalize();
        Check(migration.TextChannels.SequenceEqual(new[] { "altitude_asl", "vspeed", "ground_native" }) && migration.PanelChannels.SequenceEqual(new[] { "ground_native", "altitude_asl", "altitude_agl", "vspeed" }), "retired channels migrate in place and duplicate choices merge");
        Check(Channels.All.All(c => c.Id is not ("altitude" or "ground_asl" or "climb")) && Channels.DefaultPanel.Contains("vspeed"), "removed channels are absent and default panel has vertical-speed gauge");
        var migratedFiles = Readings.Files(Readings.Create(d, migration), migration);
        Check(migratedFiles.ContainsKey("altitude_asl.txt") && migratedFiles.ContainsKey("ground_altitude.txt") && migratedFiles.ContainsKey("vspeed.txt") && !migratedFiles.ContainsKey("altitude.txt"), "migrated exports use retained channel filenames");
        var root = Path.Combine(Path.GetTempPath(), "WsgGliderChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "preferences.json");
            prefs.PanelChannels = ["weight", "tas"]; prefs.Save(path);
            var loaded = FeederPreferences.Load(path, out var error);
            Check(error == null && loaded.PanelChannels.SequenceEqual(prefs.PanelChannels) && loaded.Altitude == "m" && loaded.TextChannels.SequenceEqual(prefs.TextChannels), "settings roundtrip preserves independent selections, order and units");
            Check(!loaded.StartMainMinimized, "startup minimization defaults to off");
            prefs.StartMainMinimized = true; prefs.Save(path);
            Check(FeederPreferences.Load(path, out error).StartMainMinimized && error == null, "startup minimization preference survives saving and reopening");
            prefs.StartMainMinimized = false; prefs.Save(path);
            Check(!FeederPreferences.Load(path, out error).StartMainMinimized, "startup minimization can be disabled again");
            File.WriteAllText(path, "{broken");
            Check(FeederPreferences.Load(path, out error).TextChannels.Length == 4 && error != null, "corrupt settings recover to defaults with visible error");
            File.WriteAllText(path, "{\"Altitude\":\"invalid\",\"PanelChannels\":[\"tas\",\"tas\",\"../bad\"],\"TextChannels\":[]}");
            loaded = FeederPreferences.Load(path, out error);
            Check(loaded.Altitude == "ft" && loaded.PanelChannels.SequenceEqual(new[] { "tas" }) && loaded.TextChannels.Length == 0, "invalid preferences normalized while explicit no-file selection remains empty");
            var writer = new OutputWriter(Path.Combine(root, "Output"));
            Check(writer.Write(files) == null && Directory.GetFiles(writer.DirectoryPath).Length == 3, "selected-file writer writes exactly requested channels");
        }
        finally { Directory.Delete(root, true); }
        const string match = """{"ok":true,"schemaVersion":1,"status":"matched","glider":{"gliderKey":"EXAMPLE","displayName":"Example","vneTasKph":null,"maxWeightKg":600,"hasFlaps":true,"flapCount":6,"flapLabels":{"1":"N"}}}""";
        const string unknown = """{"ok":true,"schemaVersion":1,"status":"unknown","glider":{"gliderKey":null}}""";
        Check(GliderReferenceClient.Parse(Encoding.UTF8.GetBytes(match)).Metadata is { VneTasKph: null, MaxWeightKg: 600 }, "partial API response preserves null limits");
        Check(GliderReferenceClient.Parse(Encoding.UTF8.GetBytes(unknown)).Metadata == null, "unknown response yields no stale metadata");
        var uri = GliderReferenceClient.BuildUri("glider & type", 18, true);
        Check(uri.Query.Contains("type=glider%20%26%20type") && uri.Query.Contains("competitionClass=18m%20flapped"), "aircraft query safely encodes identity and variant");
        var js3Uri = GliderReferenceClient.BuildAircraftUri("BC_JS3", identity.Title, 18, true);
        Check(js3Uri.Query == "?type=BC_JS3_18m&wingMeters=18&competitionClass=18m%20flapped", "installed JS3 folder matches NB21 IGC identity without livery title");
        Check(GliderReferenceClient.BuildAircraftUri(" gotfriends_schleicher_as_33_me ", "AS33 livery", 14.999, true).Query.Contains("type=gotfriends_schleicher_as_33_me_15m"), "NB21 wingspan suffix applies generically and rounds fractional span");
        Check(GliderReferenceClient.BuildAircraftUri("UNKNOWN_GLIDER", "JS3 livery", 18, true).Query.Contains("type=UNKNOWN_GLIDER_18m"), "unknown folder is preserved instead of guessing from livery title");
        Check(GliderReferenceClient.BuildAircraftUri(null, "fallback title", 18, true) == GliderReferenceClient.BuildUri("fallback title", 18, true), "missing folder retains existing title fallback");
        foreach (var span in new double?[] { null, 0, -1, double.NaN, double.PositiveInfinity, 101 })
            Check(GliderReferenceClient.BuildAircraftUri("BC_JS3", identity.Title, span, true).Query == "?type=BC_JS3", "unavailable or invalid wingspan does not invent an IGC suffix: " + span);
        var handler = new FixtureHandler(match);
        using (var client = new GliderReferenceClient(handler))
        {
            await client.GetAsync(uri, default); await client.GetAsync(uri, default);
            Check(handler.Calls == 1, "repeated identity lookup reuses response cache");
            handler.Status = HttpStatusCode.ServiceUnavailable;
            try { await client.GetAsync(GliderReferenceClient.BuildUri("other", 15, false), default); throw new Exception("Expected unavailable"); }
            catch (HttpRequestException) { Check(true, "maintenance status does not parse into false metadata"); }
        }
        handler = new FixtureHandler(new string('x', 65537));
        using (var client = new GliderReferenceClient(handler))
        {
            try { await client.GetAsync(uri, default); throw new Exception("Expected oversize rejection"); }
            catch (InvalidDataException) { Check(true, "oversized metadata responses are bounded"); }
        }
        if (Environment.GetCommandLineArgs().Contains("--live-reference-check"))
        {
            using var live = new GliderReferenceClient();
            foreach (var (type, wing, flapped, expected, weight, vne) in new (string, double?, bool?, string?, double?, double?)[]
            {
                ("AS33-18-GF", null, null, "AS33-18-GF", 600, 282),
                ("LS4-MD", null, null, "LS4-MD", 525, 282),
                ("bc_js3_18m", null, null, "JS3-18-BC", 600, 292),
                ("gotfriends_schleicher_as_33_me", 15, true, "AS33-15-GF", 550, 282),
                ("gotfriends_schleicher_as_33_me", 18, true, "AS33-18-GF", 600, 282),
                ("WSG_UNKNOWN_REFERENCE_TEST_20261016", null, null, null, null, null)
            })
            {
                var reference = await live.GetAsync(GliderReferenceClient.BuildUri(type, wing, flapped), default);
                Check(reference.Metadata?.GliderKey == expected && reference.Metadata?.MaxWeightKg == weight && reference.Metadata?.VneTasKph == vne, "production API / actual client: " + type + " " + wing);
                if (type == "LS4-MD") Check(reference.Metadata?.HasFlaps == false && reference.Metadata.FlapCount == null, "production unflapped metadata is respected");
            }
            foreach (var (folder, wing, flapped, expected, weight, vne) in new (string, double, bool, string?, double?, double?)[]
            {
                ("BC_JS3", 18, true, "JS3-18-BC", 600, 292),
                ("gotfriends_schleicher_as_33_me", 15, true, "AS33-15-GF", 550, 282),
                ("gotfriends_schleicher_as_33_me", 18, true, "AS33-18-GF", 600, 282),
                ("LS4-MD", 15, false, "LS4-MD", 525, 282),
                ("WSG_UNKNOWN_REFERENCE_TEST_20261016", 18, true, null, null, null)
            })
            {
                var reference = await live.GetAsync(GliderReferenceClient.BuildAircraftUri(folder, "Unused livery title", wing, flapped), default);
                Check(reference.Metadata?.GliderKey == expected && reference.Metadata?.MaxWeightKg == weight && reference.Metadata?.VneTasKph == vne, "production API / NB21-style folder identity: " + folder + " " + wing);
                if (folder == "BC_JS3") Check(reference.Metadata is { HasFlaps: true, FlapCount: 6 } && reference.Metadata.FlapLabels?[3] == "3", "installed B21-CalVR JS3 receives catalog flap detents and labels");
            }
        }
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++) Readings.Files(Readings.Create(d, prefs, metadata), prefs);
        watch.Stop();
        Console.WriteLine($"Formatting benchmark: {watch.Elapsed.TotalMilliseconds / 10000:0.000} ms/sample (10,000 iterations; no simulator or UI).");
        return count;
    }
    private sealed class FixtureHandler(string body) : HttpMessageHandler
    {
        public int Calls; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
