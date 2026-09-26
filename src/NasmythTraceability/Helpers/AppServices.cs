using NasmythTraceability.Data;
using NasmythTraceability.Services;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.Helpers;

/// <summary>
/// Tiny composition root. Creates every service once and hands them to the view models.
/// Kept deliberately simple - no external DI container.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppServices(AppConfig config, bool useSimulatedScanner)
    {
        Config = config;
        Database = new DatabaseService(config.ResolveDatabasePath());
        Database.Initialize();

        Settings = new SettingsService(Database);
        Stations = new StationService(Database);
        Trace = new TraceService(Database);
        Reports = new ReportService(Database, Settings);
        // RouteService / routes tables are retained per the design spec but are not used:
        // there is no fixed routing, only a fixed final station.
        Routes = new RouteService(Database, Settings);

        if (useSimulatedScanner)
        {
            var sim = new SimulatedScannerService();
            sim.Start();
            Simulated = sim;
            Scanner = sim;
        }
        else
        {
            var raw = new RawInputScannerService();
            RawInput = raw;
            Scanner = raw;
        }

        Coordinator = new ScanCoordinator(Scanner, Stations, Trace, Settings);

        if (RawInput is not null)
        {
            RefreshScannerAllowList();
            Stations.Changed += OnStationsChanged;
        }
    }

    private void OnStationsChanged(object? sender, EventArgs e) => RefreshScannerAllowList();

    /// <summary>Tells the raw-input reader which HID devices are mapped, enabled scanners.</summary>
    private void RefreshScannerAllowList()
        => RawInput?.SetAllowedDevices(
            Stations.GetDevices().Where(d => d.IsEnabled).Select(d => d.DeviceKey));

    public AppConfig Config { get; }
    public DatabaseService Database { get; }
    public SettingsService Settings { get; }
    public StationService Stations { get; }
    public RouteService Routes { get; }
    public TraceService Trace { get; }
    public ReportService Reports { get; }

    public IScannerService Scanner { get; }
    public RawInputScannerService? RawInput { get; }
    public SimulatedScannerService? Simulated { get; }
    public ScanCoordinator Coordinator { get; }

    public void Dispose()
    {
        Stations.Changed -= OnStationsChanged;
        Coordinator.Dispose();
        RawInput?.Dispose();
        Simulated?.Dispose();
    }
}
