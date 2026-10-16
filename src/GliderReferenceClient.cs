using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace WsgStreamFeeder;

public sealed record ReferenceResult(AircraftMetadata? Metadata, string Status, TimeSpan Ttl);
public sealed class GliderReferenceClient : IDisposable
{
    private readonly HttpClient http;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset Expires, ReferenceResult Result)> cache = new();
    public GliderReferenceClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
    public static Uri BuildUri(string type, double? wingspan, bool? flapped)
    {
        type = type.Trim();
        if (string.IsNullOrEmpty(type) || Encoding.UTF8.GetByteCount(type) > 256 || type.Any(c => c < 32 || c == 127))
            throw new ArgumentException("Invalid aircraft identity.");
        var query = "type=" + Uri.EscapeDataString(type);
        if (wingspan is > 0 and <= 100 && double.IsFinite(wingspan.Value))
        {
            query += "&wingMeters=" + wingspan.Value.ToString("0.###", CultureInfo.InvariantCulture);
            if (flapped.HasValue)
                query += "&competitionClass=" + Uri.EscapeDataString(Math.Round(wingspan.Value).ToString("0", CultureInfo.InvariantCulture) + (flapped.Value ? "m flapped" : "m no flaps"));
        }
        return new Uri("https://wesimglide.org/api/GetGliderReference.php?" + query);
    }
    public async Task<ReferenceResult> GetAsync(Uri uri, CancellationToken token)
    {
        if (cache.TryGetValue(uri.AbsoluteUri, out var entry) && entry.Expires > DateTimeOffset.UtcNow) return entry.Result;
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Reference lookup unavailable (HTTP " + (int)response.StatusCode + ").");
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Reference response is too large.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(bytes, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 65536) throw new InvalidDataException("Reference response is too large.");
            buffer.Write(bytes, 0, read);
        }
        var result = Parse(buffer.ToArray());
        var maximum = result.Status == "matched" ? 300 : 60;
        var age = response.Headers.Age?.TotalSeconds ?? 0;
        var ttl = Math.Clamp((response.Headers.CacheControl?.MaxAge?.TotalSeconds ?? maximum) - age, 0, maximum);
        result = result with { Ttl = TimeSpan.FromSeconds(ttl) };
        if (cache.Count >= 16) cache.Clear();
        if (response.Headers.CacheControl?.NoStore != true && ttl > 0) cache[uri.AbsoluteUri] = (DateTimeOffset.UtcNow + result.Ttl, result);
        return result;
    }
    public static ReferenceResult Parse(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = json.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version) || version != 1 ||
            !root.TryGetProperty("status", out var status)) throw new InvalidDataException("Unrecognized aircraft reference response.");
        if (status.GetString() == "unknown") return new(null, "unknown", TimeSpan.FromSeconds(60));
        if (status.GetString() != "matched" || !root.TryGetProperty("glider", out var glider) || glider.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid matched aircraft reference.");
        string? Text(string key) => glider.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        double? Positive(string key) => glider.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n) && double.IsFinite(n) && n > 0 ? n : null;
        var key = Text("gliderKey");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Matched aircraft has no canonical key.");
        bool? hasFlaps = glider.TryGetProperty("hasFlaps", out var hf) && hf.ValueKind is JsonValueKind.True or JsonValueKind.False ? hf.GetBoolean() : null;
        var countValue = Positive("flapCount");
        int? count = countValue is >= 2 and <= 256 && countValue == Math.Truncate(countValue.Value) ? (int)countValue : null;
        var labels = new Dictionary<int, string>();
        if (glider.TryGetProperty("flapLabels", out var fl) && fl.ValueKind == JsonValueKind.Object)
            foreach (var property in fl.EnumerateObject())
                if (int.TryParse(property.Name, out var position) && position > 0 && position <= (count ?? 256) && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 and <= 64 } label)
                    labels[position] = label;
        return new(new AircraftMetadata(Text("displayName") ?? key, Positive("vneTasKph"), Positive("maxWeightKg"), labels, hasFlaps, hasFlaps == false ? null : count, key), "matched", TimeSpan.FromSeconds(300));
    }
    public void Dispose() => http.Dispose();
}
