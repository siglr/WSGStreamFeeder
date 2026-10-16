namespace WsgStreamFeeder;

internal sealed partial class FeederForm
{
    private static string PreferencesPath => Path.Combine(Path.GetDirectoryName(Log.FilePath)!, "telemetry.json");
    private FeederPreferences preferences = new();
    private readonly TelemetryPanel capturePanel = new();
    private readonly Panel livePage = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel settingsPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly DataGridView channelGrid = new();
    private readonly Dictionary<string, ComboBox> unitSelectors = new();
    private readonly CheckBox unitsInFiles = new() { Text = "Units in text files", AutoSize = true };
    private readonly CheckBox previewToggle = new() { Text = "Preview with sample data (panel only)", AutoSize = true, Dock = DockStyle.Fill };
    private readonly NumericUpDown panelScale = new() { Minimum = 75, Maximum = 175, Increment = 25, Value = 100, Dock = DockStyle.Fill };
    private readonly Label settingsMessage = LabelFor("Selections and units apply when saved.", 9, false, "muted");
    private readonly Label referenceStatus = LabelFor("Aircraft reference: waiting for aircraft", 9, false, "muted");
    private WsgButton telemetryTab = null!, settingsTab = null!;
    private CaptureForm? captureWindow;
    private readonly Panel captureSlot = new() { Margin = new Padding(0, 8, 0, 8) };
    private string aircraftTitle = "Your glider";
    private AircraftMetadata? aircraftMetadata;
    private bool preview;
    private string? lastPanelSignature;

    private Control BuildTelemetryWorkspace()
    {
        preferences = FeederPreferences.Load(PreferencesPath, out var error);
        if (error != null) { Log.Write(error); settingsMessage.Text = error; }
        var workspace = Grid();
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tabs = Grid(2);
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        telemetryTab = ActionButton("Telemetry", (_, _) => SelectPage(false));
        settingsTab = ActionButton("Settings", (_, _) => SelectPage(true));
        telemetryTab.Selected = true;
        tabs.Controls.Add(telemetryTab, 0, 0); tabs.Controls.Add(settingsTab, 1, 0);
        workspace.Controls.Add(tabs, 0, 0);
        var pages = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        pages.Controls.Add(settingsPage); pages.Controls.Add(livePage);
        workspace.Controls.Add(pages, 0, 1);
        var live = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(6, 8, 6, 16) };
        live.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        live.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        live.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        live.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var toolbar = Grid(2);
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        previewToggle.CheckedChanged += (_, _) => { preview = previewToggle.Checked; RenderReadings(); };
        toolbar.Controls.Add(previewToggle, 0, 0);
        toolbar.Controls.Add(ActionButton("Open capture window", (_, _) => OpenCaptureWindow()), 1, 0);
        live.Controls.Add(toolbar, 0, 0);
        captureSlot.Controls.Add(capturePanel);
        live.Controls.Add(captureSlot, 0, 1);
        live.Controls.Add(referenceStatus, 0, 2);
        livePage.Controls.Add(live);
        BuildSettings();
        RenderReadings();
        return workspace;
    }
    private void SelectPage(bool settings)
    {
        settingsPage.Visible = settings; livePage.Visible = !settings;
        if (settings) settingsPage.BringToFront(); else { livePage.BringToFront(); RenderReadings(); }
        telemetryTab.Selected = !settings; settingsTab.Selected = settings;
        telemetryTab.Invalidate(); settingsTab.Invalidate();
    }
    private void BuildSettings()
    {
        var layout = Grid(2);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var heading = LabelFor("Choose values for files, the panel, or both. Hover a name for details.", 9, false, "muted");
        layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
        channelGrid.Dock = DockStyle.Fill;
        channelGrid.AllowUserToAddRows = channelGrid.AllowUserToDeleteRows = false;
        channelGrid.AllowUserToResizeRows = false;
        channelGrid.RowHeadersVisible = false;
        channelGrid.AutoGenerateColumns = false;
        channelGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        channelGrid.MultiSelect = false;
        channelGrid.BorderStyle = BorderStyle.None;
        channelGrid.RowTemplate.Height = 30;
        channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Telemetry", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 125 });
        channelGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "File", HeaderText = "File", Width = 48 });
        channelGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Panel", HeaderText = "Panel", Width = 52 });
        foreach (DataGridViewColumn column in channelGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        channelGrid.CurrentCellDirtyStateChanged += (_, _) => { if (channelGrid.IsCurrentCellDirty) channelGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        layout.Controls.Add(channelGrid, 0, 1);
        var units = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12, 0, 0, 0) };
        units.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46)); units.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        var row = 0;
        foreach (var (name, choices) in FeederPreferences.Units)
        {
            units.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            units.Controls.Add(LabelFor(name == "VerticalSpeed" ? "Vertical rate" : name, 9), 0, row);
            var selector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = name + " units" };
            selector.Items.AddRange(choices); unitSelectors[name] = selector;
            units.Controls.Add(selector, 1, row++);
        }
        units.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        units.Controls.Add(LabelFor("Panel size %", 9), 0, row); units.Controls.Add(panelScale, 1, row++);
        units.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        unitsInFiles.MaximumSize = new Size(220, 0);
        units.Controls.Add(unitsInFiles, 0, row); units.SetColumnSpan(unitsInFiles, 2); row++;
        var note = LabelFor("Units apply to files and panel. Missing values remain N/A. Aircraft limits appear when catalog data is available.", 9, false, "muted");
        units.RowStyles.Add(new RowStyle(SizeType.Absolute, 105)); units.Controls.Add(note, 0, row); units.SetColumnSpan(note, 2);
        layout.Controls.Add(units, 1, 1);
        var order = Grid(2);
        order.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); order.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        order.Controls.Add(ActionButton("Move up", (_, _) => MoveChannel(-1)), 0, 0);
        order.Controls.Add(ActionButton("Move down", (_, _) => MoveChannel(1)), 1, 0);
        layout.Controls.Add(order, 0, 2);
        layout.Controls.Add(ActionButton("Restore defaults", (_, _) => PopulateSettings(new())), 1, 2);
        layout.Controls.Add(settingsMessage, 0, 3);
        layout.Controls.Add(ActionButton("Save settings", (_, _) => SavePreferences(), true), 1, 3);
        settingsPage.Controls.Add(layout);
        PopulateSettings(preferences);
    }
    private void PopulateSettings(FeederPreferences p)
    {
        channelGrid.Rows.Clear();
        var ordered = p.PanelChannels.Concat(Channels.All.Select(c => c.Id)).Distinct();
        foreach (var id in ordered)
        {
            var c = Channels.All.Single(c => c.Id == id);
            var index = channelGrid.Rows.Add(c.Name, p.TextChannels.Contains(id), p.PanelChannels.Contains(id));
            channelGrid.Rows[index].Tag = id;
            channelGrid.Rows[index].Cells[0].ToolTipText = c.Description + " • " + c.File + (id is "wind_speed" or "wind_direction" ? " • Either wind selection displays one combined WIND row on the panel." : "");
        }
        foreach (var (name, selector) in unitSelectors) selector.SelectedItem = typeof(FeederPreferences).GetProperty(name)!.GetValue(p);
        unitsInFiles.Checked = p.IncludeUnits; panelScale.Value = p.PanelScale;
    }
    private void MoveChannel(int delta)
    {
        if (channelGrid.CurrentRow is not { } row) return;
        channelGrid.EndEdit();
        var index = row.Index; var target = index + delta;
        if (target < 0 || target >= channelGrid.Rows.Count) return;
        channelGrid.Rows.RemoveAt(index); channelGrid.Rows.Insert(target, row);
        channelGrid.CurrentCell = row.Cells[0];
    }
    private void SavePreferences()
    {
        channelGrid.EndEdit();
        var updated = new FeederPreferences { IncludeUnits = unitsInFiles.Checked, PanelScale = (int)panelScale.Value };
        foreach (var (name, selector) in unitSelectors) typeof(FeederPreferences).GetProperty(name)!.SetValue(updated, selector.SelectedItem as string);
        updated.TextChannels = channelGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[1].Value is true).Select(r => (string)r.Tag!).ToArray();
        updated.PanelChannels = channelGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[2].Value is true).Select(r => (string)r.Tag!).ToArray();
        try
        {
            updated.Save(PreferencesPath);
            preferences = updated;
            settingsMessage.Text = "Saved. Units and selections applied to both outputs.";
            lastPanelSignature = null;
            Tick();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { settingsMessage.Text = "Could not save: " + ex.Message; }
    }
    private void OpenCaptureWindow(bool restoreState = false)
    {
        if (captureWindow is { IsDisposed: false }) { captureWindow.WindowState = FormWindowState.Normal; captureWindow.Activate(); return; }
        captureWindow = new CaptureForm(capturePanel, () => shuttingDown, restoreState) { Icon = appIcon };
        captureWindow.FormClosed += (_, _) =>
        {
            captureWindow = null;
            if (shuttingDown) return;
            capturePanel.Dock = DockStyle.None;
            captureSlot.Controls.Add(capturePanel);
            UpdatePanel(capturePanel);
        };
        captureWindow.Show();
        ApplyCaptureTitleBar();
    }
    private void ApplyWorkspaceColors()
    {
        if (channelGrid.Columns.Count == 0) return;
        channelGrid.EnableHeadersVisualStyles = false;
        channelGrid.BackgroundColor = palette.Background;
        channelGrid.GridColor = palette.Line;
        channelGrid.DefaultCellStyle.BackColor = palette.Panel;
        channelGrid.DefaultCellStyle.ForeColor = palette.Text;
        channelGrid.DefaultCellStyle.SelectionBackColor = palette.Button;
        channelGrid.DefaultCellStyle.SelectionForeColor = palette.Text;
        channelGrid.ColumnHeadersDefaultCellStyle.BackColor = palette.Sidebar;
        channelGrid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        channelGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Sidebar;
        channelGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        foreach (var selector in unitSelectors.Values) { selector.BackColor = palette.Panel; selector.ForeColor = palette.Text; }
        lastPanelSignature = null;
        RenderReadings();
        ApplyCaptureTitleBar();
    }
    private void ApplyCaptureTitleBar()
    {
        if (captureWindow is not { IsHandleCreated: true }) return;
        var dark = palette.Dark ? 1 : 0;
        DwmSetWindowAttribute(captureWindow.Handle, 20, ref dark, sizeof(int));
        var color = ColorTranslator.ToWin32(palette.Background);
        DwmSetWindowAttribute(captureWindow.Handle, 35, ref color, sizeof(int));
        var text = ColorTranslator.ToWin32(palette.Text);
        DwmSetWindowAttribute(captureWindow.Handle, 36, ref text, sizeof(int));
        captureWindow.BackColor = palette.Background;
    }
    private Dictionary<string, Reading> CurrentReadings => Readings.Create(model.Latest, preferences, aircraftMetadata);
    private static FlightData PreviewData => new()
    {
        Altitude = 1800, IndicatedAltitude = 1810, Heading = 274, GroundSpeed = 32,
        VerticalSpeed = 1.6, Agl = 650, Tas = 35, Ias = 32, WindX = 0, WindZ = 6,
        WindY = 2.4, Weight = 540, FlapIndex = 3, FlapCount = 7, GroundAltitude = 1150,
        MacCready = 1.5, Temperature = 12, Pressure = 820, Track = 280, Wingspan = 18,
        OnGround = 0, Gear = 0, SimRate = 1
    };
    private void UpdatePanel(TelemetryPanel panel)
    {
        panel.Preferences = preferences; panel.Palette = palette;
        panel.Preview = preview;
        panel.Metadata = preview ? new AircraftMetadata("Preview glider", 270, 600, new Dictionary<int, string> { [1] = "−2", [2] = "−1", [3] = "0", [4] = "N", [5] = "+1", [6] = "+2", [7] = "L" }, true, 7) : aircraftMetadata;
        panel.Readings = preview ? Readings.Create(PreviewData, preferences, panel.Metadata) : CurrentReadings;
        panel.Aircraft = aircraftTitle;
        panel.FlapCount = panel.Metadata?.FlapCount ?? 0;
        panel.State = model.Latest == null ? "WAITING FOR SIMULATOR" : paused ? "PAUSED" : model.Latest?.Slew != 0 ? "SLEW" : "LIVE • MSFS 2024";
        panel.AccessibleDescription = string.Join("; ", preferences.PanelChannels.Select(id => Channels.All.Single(c => c.Id == id).Name + ": " + panel.Readings[id].Display));
        if (captureWindow is { IsDisposed: false }) captureWindow.FitContent();
        else { panel.RefreshSize(); captureSlot.Size = panel.Size; }
        panel.Invalidate();
    }
    private void RenderReadings()
    {
        if (capturePanel.IsDisposed) return;
        var readings = CurrentReadings;
        var signature = string.Join("|", preferences.PanelChannels.Concat(preferences.PanelChannels.Any(id => id is "wind_speed" or "wind_direction") ? new[] { "wind_speed", "wind_direction" } : Array.Empty<string>()).Distinct().Select(id => readings[id].Display + ":" + readings[id].Value)) + "|" + preview + "|" + paused + "|" + aircraftTitle + "|" + model.Latest?.Slew;
        if (signature == lastPanelSignature) return;
        lastPanelSignature = signature;
        UpdatePanel(capturePanel);
    }
}
