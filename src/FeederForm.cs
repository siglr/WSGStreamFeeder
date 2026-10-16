using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace WsgStreamFeeder;

internal sealed partial class FeederForm : Form
{
    private enum Definition { Aircraft }
    private enum Request { Aircraft, Heartbeat }
    private const int SimMessage = 0x0402;
    private readonly Options options;
    private readonly OutputWriter writer;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private SimConnect? connection;
    private OverlayText latest = OverlayText.Unavailable;
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
        if (options.Minimized) WindowState = FormWindowState.Minimized;
        timer.Tick += (_, _) => Tick();
        Shown += (_, _) =>
        {
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
        var error = writer.Write(latest);
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
            connection.OnRecvSystemState += (_, _) => lastContact = Environment.TickCount64;
            connection.OnRecvSimobjectData += (_, data) =>
            {
                if (data.dwRequestID != (uint)Request.Aircraft || pendingDisconnect != null) return;
                lastContact = Environment.TickCount64;
                latest = TelemetryFormatter.Format((Telemetry)data.dwData[0]);
                connectionStatus = latest == OverlayText.Unavailable ? "Waiting for valid aircraft data…" : "Connected — updating OBS once per second";
            };
            Define("PLANE ALTITUDE", "feet");
            Define("INDICATED ALTITUDE", "feet");
            Define("PLANE HEADING DEGREES TRUE", "degrees");
            Define("GROUND VELOCITY", "knots");
            Define("VERTICAL SPEED", "feet per minute");
            connection.RegisterDataDefineStruct<Telemetry>(Definition.Aircraft);
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
        latest = OverlayText.Unavailable;
        connectionStatus = reason;
        nextConnect = Environment.TickCount64 + 5000;
    }

    private void ShowState(string message)
    {
        status.Text = message;
        altitudeValue.Text = latest.Altitude;
        headingValue.Text = latest.Heading;
        speedValue.Text = latest.Speed;
        verticalSpeedValue.Text = latest.VerticalSpeed;
        var live = latest != OverlayText.Unavailable && message == connectionStatus;
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
            var error = writer.Write(OverlayText.Unavailable);
            if (error != null) Log.Write("Final output write failed: " + error);
        }
        timer.Dispose();
        logo.Image?.Dispose();
        appIcon.Dispose();
        Log.Write("Stopped.");
        base.OnFormClosed(e);
    }
}
