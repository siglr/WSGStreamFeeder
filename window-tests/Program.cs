using WsgStreamFeeder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAILED: " + label);
            count++;
            Console.WriteLine("PASS: " + label);
        }
        var closingApp = false;
        var path = Path.Combine(Path.GetDirectoryName(Log.FilePath)!, "capture.json");
        WindowPlacement Saved() => WindowPlacement.Load(path) ?? throw new Exception("Missing placement");
        TestForm Create(bool restore = false)
        {
            var form = new TestForm();
            WindowPlacementBinding.Attach(form, "capture.json", appIsClosing: () => closingApp, restoreMinimized: restore);
            form.Show();
            Application.DoEvents();
            return form;
        }
        try
        {
            using (var first = Create())
            {
                Check(Saved().IsOpen, "opening capture persists open state without a resize");
                first.Bounds = new Rectangle(Screen.PrimaryScreen!.WorkingArea.Location + new Size(60, 70), new Size(480, 360));
                closingApp = true;
                first.Close();
            }
            var initial = Saved();
            Check(initial.IsOpen && !initial.Minimized && initial.Width == 480, "app shutdown retains open state and saved size");
            closingApp = false;
            using (var reopened = Create(true))
            {
                Check(reopened.Bounds == new Rectangle(initial.X, initial.Y, initial.Width, initial.Height), "capture reopens at saved bounds");
                reopened.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                closingApp = true;
                reopened.Close();
            }
            Check(Saved().IsOpen && Saved().Minimized && Saved().Width == initial.Width, "shutdown while minimized retains open flag and normal bounds");
            closingApp = false;
            using (var minimized = Create(true))
            {
                Check(minimized.WindowState == FormWindowState.Minimized, "automatic reopening restores minimized state");
                minimized.Close();
            }
            Check(!Saved().IsOpen, "explicitly closing capture disables automatic reopening");
            using (var manual = Create())
            {
                Check(manual.WindowState == FormWindowState.Normal, "manual opening shows capture normally after a minimized close");
                manual.Close();
            }
            File.WriteAllText(path, "{\"X\":60,\"Y\":70,\"Width\":480,\"Height\":360,\"Maximized\":false}");
            Check(!Saved().IsOpen && !Saved().Minimized, "v1.3.1 placement migrates with capture closed by default");
            File.WriteAllText(path, "{broken");
            using (var recovered = Create())
            {
                Check(Saved().IsOpen, "corrupt saved state recovers and can be saved again");
                recovered.Close();
            }
            Console.WriteLine($"All {count} window lifecycle checks passed.");
        }
        finally
        {
            var directory = Path.GetDirectoryName(Log.FilePath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

internal sealed class TestForm : Form
{
    protected override bool ShowWithoutActivation => true;
    public TestForm()
    {
        Opacity = 0;
        ShowInTaskbar = false;
        Size = new Size(480, 360);
        StartPosition = FormStartPosition.Manual;
    }
}

namespace WsgStreamFeeder
{
    // Isolate tests from the user's real window preferences and logs.
    internal static class Log
    {
        public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "WSG-window-tests-" + Guid.NewGuid().ToString("N"), "test.log");
        public static void Write(string message) => Console.WriteLine(message);
    }
}
