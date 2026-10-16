namespace WsgStreamFeeder;

internal sealed partial class FeederForm
{
    private readonly Label updateStatus = LabelFor("", 8, false, "muted");
    private readonly WsgButton updateButton = new() { Text = "Check for updates", Centered = true, Dock = DockStyle.Fill, Margin = new Padding(12, 2, 0, 2) };
    private bool checkingForUpdates;
    private bool updateReady;

    private async Task CheckForUpdatesAsync()
    {
        if (checkingForUpdates || shuttingDown || options.DiagnosticSeconds > 0) return;
        if (updateReady)
        {
            try { AppUpdate.Launch(AppContext.BaseDirectory, false, options.OutputDirectory, !Visible); Close(); }
            catch (Exception ex) { Log.Write("Update restart failed: " + ex); updateStatus.Text = "Unable to restart for update. See updater log."; }
            return;
        }
        checkingForUpdates = true;
        updateButton.Enabled = false;
        try
        {
            var restart = await AppUpdate.CheckAsync(AppContext.BaseDirectory, message =>
            {
                if (shuttingDown || IsDisposed) return;
                updateStatus.Text = message;
                Log.Write(message);
                if (message.StartsWith("Update ready", StringComparison.Ordinal))
                {
                    updateReady = true;
                    updateButton.Text = "Restart to update";
                }
            }, () => !shuttingDown && connection == null, options.OutputDirectory, () => !Visible);
            if (restart && !shuttingDown) Close();
        }
        catch (Exception ex) { Log.Write("Update check failed: " + ex); }
        finally
        {
            checkingForUpdates = false;
            if (!IsDisposed && !shuttingDown) updateButton.Enabled = AppUpdate.Enabled;
        }
    }
}
