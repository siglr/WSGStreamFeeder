using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WsgStreamFeeder;

internal sealed record Settings(string OutputDirectory);
internal sealed record Options(string OutputDirectory, bool Minimized, int DiagnosticSeconds)
{
    public const string CompatibilityDirectory = @"C:\MSFS2024\Apps and Mods\StreamFlight\Output";
    public static Options Load(string[] args)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) : null;
        var output = settings?.OutputDirectory ?? "Output";
        var minimized = false;
        var seconds = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length: output = args[++i]; break;
                case "--minimized": minimized = true; break;
                case "--diagnostic-seconds" when i + 1 < args.Length:
                    seconds = int.Parse(args[++i]);
                    if (seconds < 1 || seconds > 3600) throw new ArgumentException("Diagnostic duration must be 1–3600 seconds.");
                    break;
                default: throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
            }
        }
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("OutputDirectory cannot be empty.");
        return new(Path.GetFullPath(output, AppContext.BaseDirectory), minimized, seconds);
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var options = Options.Load(args);
            using var packageGate = AppUpdate.Enter(AppContext.BaseDirectory);
            if (packageGate == null)
            {
                if (options.DiagnosticSeconds == 0)
                    MessageBox.Show("WSG Stream Feeder is already running or finishing an update.", "WSG Stream Feeder");
                return 2;
            }
            if (options.DiagnosticSeconds == 0 && AppUpdate.Startup(AppContext.BaseDirectory, options.OutputDirectory, options.Minimized)) return 0;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.TrimEndingDirectorySeparator(options.OutputDirectory).ToUpperInvariant())));
            using var instance = new Mutex(true, @"Local\WsgStreamFeeder-" + hash, out var first);
            if (!first)
            {
                if (options.DiagnosticSeconds == 0)
                    MessageBox.Show("WSG Stream Feeder is already running for this output folder. Restore its window from the taskbar.", "WSG Stream Feeder");
                return 2;
            }
            using var form = new FeederForm(options);
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            MessageBox.Show(ex.Message, "WSG Stream Feeder could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class Log
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG Stream Feeder", "feeder.log");
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                File.Move(FilePath, FilePath + ".old", true);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
