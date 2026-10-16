namespace WsgStreamFeeder;

internal sealed partial class FeederForm
{
    private readonly GliderReferenceClient referenceClient = new();
    private readonly CancellationTokenSource referenceCancellation = new();
    private string? aircraftType;
    private bool identityReady;
    private bool aircraftPathReady;
    private string? referenceKey;
    private int referenceGeneration;
    private long nextReferenceAttempt;
    private bool referenceBusy;
    private double? referenceWingspan;

    private void SetAircraftPath(string? path)
    {
        aircraftPathReady = true;
        if (string.IsNullOrWhiteSpace(path)) return;
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var marker = Array.FindIndex(parts, p => p.Equals("airplanes", StringComparison.OrdinalIgnoreCase));
        if (marker >= 0 && marker + 1 < parts.Length) aircraftType = parts[marker + 1];
        // Only the aircraft folder identity is sent; never the source path or config contents.
    }
    private void ResetAircraftReference()
    {
        referenceGeneration++;
        referenceKey = null; aircraftType = null; identityReady = false; aircraftPathReady = false; referenceWingspan = null;
        aircraftMetadata = null; aircraftTitle = "Your glider";
        aircraftModel = "";
        referenceBusy = false; nextReferenceAttempt = 0;
        referenceStatus.Text = "Aircraft reference: waiting for aircraft";
        lastPanelSignature = null;
    }
    private void QueueReferenceLookup(FlightData sample)
    {
        if (!identityReady || !aircraftPathReady || options.DiagnosticSeconds > 0) return;
        if (double.IsFinite(sample.Wingspan) && sample.Wingspan > 0 &&
            (!referenceWingspan.HasValue || Math.Abs(sample.Wingspan - referenceWingspan.Value) > .1)) referenceWingspan = sample.Wingspan;
        Uri uri;
        try { uri = GliderReferenceClient.BuildAircraftUri(aircraftType, aircraftTitle, referenceWingspan, sample.FlapCount > 1); }
        catch (ArgumentException) { referenceStatus.Text = "Aircraft reference unavailable — live values remain available"; return; }
        if (referenceKey != uri.AbsoluteUri)
        {
            referenceGeneration++;
            referenceKey = uri.AbsoluteUri; aircraftMetadata = null;
            referenceBusy = false; nextReferenceAttempt = 0;
            lastPanelSignature = null;
        }
        if (referenceBusy || Environment.TickCount64 < nextReferenceAttempt) return;
        referenceBusy = true;
        var generation = referenceGeneration;
        referenceStatus.Text = "Looking up aircraft reference…";
        _ = LoadReferenceAsync(uri, generation);
    }
    private async Task LoadReferenceAsync(Uri uri, int generation)
    {
        try
        {
            var result = await referenceClient.GetAsync(uri, referenceCancellation.Token);
            if (shuttingDown || generation != referenceGeneration) return;
            aircraftMetadata = result.Metadata;
            Log.Write("Aircraft reference lookup " + uri.Query + ": " + (result.Metadata?.GliderKey ?? "unknown"));
            // One successful lookup per aircraft/variant, not a periodic network poll.
            nextReferenceAttempt = long.MaxValue;
            referenceStatus.Text = result.Metadata == null ? "Aircraft not cataloged — live values remain available" : "Aircraft reference: " + result.Metadata.DisplayName;
            lastPanelSignature = null;
            RenderReadings();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (shuttingDown || generation != referenceGeneration) return;
            referenceStatus.Text = "Aircraft reference unavailable — live values remain available";
            nextReferenceAttempt = Environment.TickCount64 + 300000;
            Log.Write("Aircraft reference lookup " + uri.Query + ": " + ex.Message);
        }
        finally { if (generation == referenceGeneration) referenceBusy = false; }
    }
}
