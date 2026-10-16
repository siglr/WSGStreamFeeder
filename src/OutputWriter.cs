using System.Text;

namespace WsgStreamFeeder;

public sealed class OutputWriter(string directory)
{
    private readonly Dictionary<string, string> written = new(StringComparer.Ordinal);
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    // Retry failed writes on the next tick. Never cache a failed write as successful.
    // Replace a complete file so OBS cannot read a partially written number.
    public string? Write(IEnumerable<KeyValuePair<string, string>> values)
    {
        var errors = new List<string>();
        foreach (var (name, content) in values)
        {
            var path = Path.Combine(DirectoryPath, name);
            if (written.TryGetValue(name, out var previous) && previous == content && File.Exists(path))
                continue;
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                temporary = Path.Combine(DirectoryPath, $".{name}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporary, content, Utf8);
                File.Move(temporary, path, overwrite: true);
                written[name] = content;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{name}: {ex.Message}");
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
    }
}
