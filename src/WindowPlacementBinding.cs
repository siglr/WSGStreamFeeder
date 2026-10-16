using System.Text.Json;

namespace WsgStreamFeeder;

internal static class WindowPlacementBinding
{
    public static void Attach(Form form, string fileName, bool startMinimized = false, Action? fitContent = null)
    {
        var path = Path.Combine(Path.GetDirectoryName(Log.FilePath)!, fileName);
        WindowPlacement? lastSaved = null;
        try { lastSaved = WindowPlacement.Load(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Log.Write("Could not restore window placement: " + ex.Message); }

        var ready = false;
        var maximized = lastSaved?.Maximized == true && form.MaximizeBox;
        if (lastSaved != null) form.StartPosition = FormStartPosition.Manual;
        form.Load += (_, _) =>
        {
            fitContent?.Invoke();
            if (lastSaved != null)
            {
                var primary = (Screen.PrimaryScreen ?? Screen.FromControl(form)).WorkingArea;
                form.Bounds = lastSaved.Fit(form.MinimumSize, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(),
                    primary, fitContent == null ? null : form.Height);
                fitContent?.Invoke();
            }
            if (maximized) form.WindowState = FormWindowState.Maximized;
            if (startMinimized) form.WindowState = FormWindowState.Minimized;
            ready = true;
        };
        form.Resize += (_, _) =>
        {
            if (ready && form.WindowState != FormWindowState.Minimized)
                maximized = form.WindowState == FormWindowState.Maximized;
        };
        void Save()
        {
            if (!ready) return;
            var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            var placement = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);
            if (!placement.IsValid || placement == lastSaved) return;
            try { placement.Save(path); lastSaved = placement; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Log.Write("Could not save window placement: " + ex.Message); }
        }
        // No disk writes in telemetry ticks or while the user drags the window.
        form.ResizeEnd += (_, _) => Save();
        form.FormClosing += (_, e) => { if (!e.Cancel) Save(); };
    }
}
