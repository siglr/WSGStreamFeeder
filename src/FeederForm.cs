using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace WsgStreamFeeder;

internal sealed partial class FeederForm : Form
{
    private enum Definition { Aircraft, Identity }
    private enum Event { Pause, AircraftLoaded, SimStart, SimStop }
    private enum Request { Aircraft, Heartbeat, Identity, AircraftPath }
    private const int SimMessage = 0x0402;
    private readonly Options options;
    private readonly OutputWriter writer;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private SimConnect? connection;
    private readonly FlightModel model = new();
    private long lastSample;
    private bool paused;
    private bool simRunning = true;
    private long nextConnect;
    private long lastContact;
    private long nextHeartbeat;
    private string connectionStatus = "Waiting for MSFS 2024…";
    private string? pendingDisconnect;
    private bool hasOwnedOutput;
    private string lastStatus = "";
    private bool shuttingDown;

    public FeederForm(Options options)
    {
        this.options = options;
        writer = new OutputWriter(options.OutputDirectory);
        InitializeAppearance();
        ApplyAppearance();
        ShowInTaskbar = true;
        WindowPlacementBinding.Attach(this, "main-window.json", options.Minimized || preferences.StartMainMinimized);
        timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
            if (WindowPlacementBinding.Load("capture-window.json")?.IsOpen == true)
                OpenCaptureWindow(restoreState: true);
            Log.Write("Started. Output: " + options.OutputDirectory);
            Tick();
            timer.Start();
            _ = CheckForUpdatesAsync();
        };
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            Process.Start(new ProcessStartInfo(options.OutputDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    private void Tick()
    {
        if (shuttingDown) return;
        if (options.DiagnosticSeconds > 0 && lifetime.Elapsed.TotalSeconds >= options.DiagnosticSeconds)
        {
            Close();
            return;
        }
        if (pendingDisconnect != null)
        {
            var reason = pendingDisconnect;
            pendingDisconnect = null;
            Disconnect(reason);
        }
        var now = Environment.TickCount64;
        if (connection != null && now - lastContact > 20000)
            Disconnect("Simulator stopped responding. Reconnecting…");
        if (connection == null && now >= nextConnect) Connect();
        if (connection != null && now >= nextHeartbeat)
        {
            try
            {
                connection.RequestSystemState(Request.Heartbeat, "Sim");
                nextHeartbeat = now + 5000;
            }
            catch (COMException ex) { Disconnect("Simulator disconnected: " + ex.Message); }
        }
        hasOwnedOutput = true;
        if (model.Latest != null && !paused && now - lastSample > 5000)
        {
            model.Reset();
            connectionStatus = "Aircraft data stale — waiting for a fresh sample…";
        }
        var outputs = Readings.Files(CurrentReadings, preferences);
        // Retire only known, previously generated files. Never delete arbitrary OBS content.
        foreach (var channel in Channels.All)
            if (!outputs.ContainsKey(channel.File) && File.Exists(Path.Combine(options.OutputDirectory, channel.File)))
                outputs[channel.File] = "N/A";
        // Retired preview export: clear a stale value without creating or deleting a file.
        foreach (var retiredFile in new[] { "wingspan.txt", "altitude.txt", "ground_asl.txt", "climb_geometric.txt" })
            if (File.Exists(Path.Combine(options.OutputDirectory, retiredFile))) outputs[retiredFile] = "N/A";
        var error = writer.Write(outputs);
        ShowState(error == null ? connectionStatus : "Cannot update output; retrying. " + error);
    }

    private void Connect()
    {
        nextConnect = Environment.TickCount64 + 5000;
        try
        {
            connection = new SimConnect("WSG Stream Feeder", Handle, SimMessage, null, 0);
            lastContact = Environment.TickCount64;
            nextHeartbeat = 0;
            connection.OnRecvOpen += (_, _) =>
            {
                lastContact = Environment.TickCount64;
                connectionStatus = "Connected — waiting for aircraft data…";
            };
            connection.OnRecvQuit += (_, _) => pendingDisconnect = "MSFS closed. Waiting to reconnect…";
            connection.OnRecvException += (_, data) => pendingDisconnect = $"SimConnect error {data.dwException} (request {data.dwSendID}). Retrying…";
            connection.OnRecvSystemState += (_, data) =>
            {
                lastContact = Environment.TickCount64;
                if (data.dwRequestID == (uint)Request.AircraftPath) SetAircraftPath(data.szString);
                if (data.dwRequestID == (uint)Request.Heartbeat)
                {
                    simRunning = data.dwInteger != 0;
                    if (!simRunning) { model.Reset(); connectionStatus = "Simulator stopped — waiting for flight…"; }
                }
            };
            connection.OnRecvEvent += (_, data) =>
            {
                switch ((Event)data.uEventID)
                {
                    case Event.Pause:
                        paused = data.dwData != 0;
                        connectionStatus = paused ? "Simulator paused" : "Connected — updating once per second";
                        break;
                    case Event.SimStop: simRunning = false; model.Reset(); break;
                    case Event.SimStart: simRunning = true; model.Reset(); break;
                }
            };
            connection.OnRecvEventFilename += (_, data) =>
            {
                if (data.uEventID != (uint)Event.AircraftLoaded) return;
                ResetAircraftReference();
                model.Reset();
                SetAircraftPath(data.szFileName);
                connection?.RequestDataOnSimObject(Request.Identity, Definition.Identity, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            };
            connection.OnRecvSimobjectData += (_, data) =>
            {
                if (pendingDisconnect != null) return;
                lastContact = Environment.TickCount64;
                if (data.dwRequestID == (uint)Request.Identity)
                {
                    var identity = (AircraftIdentity)data.dwData[0];
                    aircraftTitle = identity.Title?.Trim() ?? "Your glider";
                    aircraftModel = identity.Model?.Trim() ?? "";
                    lastPanelSignature = null;
                    identityReady = true;
                    return;
                }
                if (data.dwRequestID != (uint)Request.Aircraft) return;
                var sample = (FlightData)data.dwData[0];
                if (!simRunning || sample.CameraState is 12 or 32 or 35 or 36)
                {
                    model.Reset(); connectionStatus = "Waiting for an active flight…"; return;
                }
                lastSample = Environment.TickCount64;
                model.Accept(sample);
                connectionStatus = paused ? "Simulator paused" : sample.Slew != 0 ? "Slew active" : "Connected — updating once per second";
                QueueReferenceLookup(sample);
            };
            foreach (var (_, name, unit) in FlightData.Definition) Define(name, unit);
            connection.RegisterDataDefineStruct<FlightData>(Definition.Aircraft);
            connection.AddToDataDefinition(Definition.Identity, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            connection.AddToDataDefinition(Definition.Identity, "ATC MODEL", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            connection.RegisterDataDefineStruct<AircraftIdentity>(Definition.Identity);
            connection.SubscribeToSystemEvent(Event.Pause, "Pause_EX1");
            connection.SubscribeToSystemEvent(Event.AircraftLoaded, "AircraftLoaded");
            connection.SubscribeToSystemEvent(Event.SimStart, "SimStart");
            connection.SubscribeToSystemEvent(Event.SimStop, "SimStop");
            connection.RequestSystemState(Request.AircraftPath, "AircraftLoaded");
            connection.RequestDataOnSimObject(Request.Identity, Definition.Identity, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            connection.RequestDataOnSimObject(Request.Aircraft, Definition.Aircraft,
                SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            connectionStatus = "Connecting to MSFS 2024…";
        }
        catch (COMException)
        {
            Disconnect("Waiting for MSFS 2024 — retrying every 5 seconds");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileNotFoundException or TypeInitializationException)
        {
            Disconnect("SimConnect could not load. Keep both SimConnect DLLs beside the EXE. " + ex.Message);
        }
    }

    private void Define(string name, string unit) => connection!.AddToDataDefinition(
        Definition.Aircraft, name, unit, SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == SimMessage && connection != null && pendingDisconnect == null)
        {
            try { connection.ReceiveMessage(); }
            catch (COMException ex) { pendingDisconnect = "Simulator disconnected: " + ex.Message; }
        }
        base.WndProc(ref message);
    }

    private void Disconnect(string reason)
    {
        var previous = connection;
        connection = null;
        try { previous?.Dispose(); }
        catch (COMException ex) { Log.Write("Disconnect: " + ex.Message); }
        model.Reset();
        paused = false; simRunning = true;
        if (identityReady || referenceKey != null || aircraftMetadata != null) ResetAircraftReference();
        connectionStatus = reason;
        nextConnect = Environment.TickCount64 + 5000;
    }

    private void ShowState(string message)
    {
        status.Text = message;
        RenderReadings();
        var live = model.Latest != null && message == connectionStatus;
        connectionBadge.Text = live ? "●  LIVE" : "●  WAITING";
        connectionBadge.ForeColor = live ? palette.Accent : palette.Muted;
        if (lastStatus != message)
        {
            Log.Write(message);
            lastStatus = message;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        shuttingDown = true;
        timer.Stop();
        Disconnect("Stopped");
        if (hasOwnedOutput)
        {
            var error = writer.Write(Readings.Files(Readings.Create(null, preferences), preferences));
            if (error != null) Log.Write("Final output write failed: " + error);
        }
        referenceCancellation.Cancel();
        referenceCancellation.Dispose();
        referenceClient.Dispose();
        captureWindow?.Close();
        timer.Dispose();
        logo.Image?.Dispose();
        appIcon.Dispose();
        Log.Write("Stopped.");
        base.OnFormClosed(e);
    }
}
