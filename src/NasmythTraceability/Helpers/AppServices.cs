using System.Net.Http;
using NasmythTraceability.Data;
using NasmythTraceability.Models;
using NasmythTraceability.Services;
using NasmythTraceability.Services.Rfid;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.Helpers;

/// <summary>
/// Tiny composition root. Creates every service once and hands them to the view models.
/// Kept deliberately simple - no external DI container.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <param name="useSimulatedScanner">
    /// True for the self-test and the data tools: reads come from <see cref="Simulated"/> and the
    /// network readers are not polled unless <see cref="NetworkReaderService.Start"/> is called.
    /// </param>
    /// <param name="readerHandler">Replaces the network (self-test fake readers).</param>
    public AppServices(AppConfig config, bool useSimulatedScanner, HttpMessageHandler? readerHandler = null)
    {
        Config = config;
        Database = new DatabaseService(config.ResolveDatabasePath());
        Database.Initialize();

        Settings = new SettingsService(Database);
        Stations = new StationService(Database);
        Trace = new TraceService(Database);
        Tags = new TagService(Database);
        Reports = new ReportService(Database, Settings);
        // RouteService / routes tables are retained per the design spec but are not used:
        // there is no fixed routing, only a fixed final station.
        Routes = new RouteService(Database, Settings);

        // Network RFID readers: one polling loop per station reader and one for the work order
        // assigning station. They run for the life of the app, whatever page is open.
        Readers = new NetworkReaderService(new ReaderCursorStore(Database),
            () => Settings.GetInt(SettingsService.ReaderPollIntervalMs, 500), readerHandler);
        TagWriter = new TagWriter(Readers, Tags, Trace);

        if (useSimulatedScanner)
        {
            var sim = new SimulatedScannerService();
            sim.Start();
            Simulated = sim;
            Coordinator = new ScanCoordinator(Stations, Trace, Tags, Settings, Readers, sim);
        }
        else
        {
            Coordinator = new ScanCoordinator(Stations, Trace, Tags, Settings, Readers);
        }

        Readers.IdentityChanged += OnReaderIdentityChanged;
        Readers.ConnectionChanged += OnReaderConnectionChanged;
        Readers.Notice += OnReaderNotice;
        Stations.Changed += OnConfigurationChanged;
        Settings.Changed += OnConfigurationChanged;
        ConfigureReaders();

        if (!useSimulatedScanner)
            Readers.Start();
    }

    private void OnConfigurationChanged(object? sender, EventArgs e) => ConfigureReaders();

    /// <summary>Points the pollers at the readers set up in Settings (enabled stations only).</summary>
    public void ConfigureReaders()
    {
        var endpoints = new List<ReaderEndpoint>();
        var macs = new Dictionary<string, string>();

        foreach (var s in Stations.GetStations(includeDisabled: false).Where(s => s.ReaderIp.Length > 0))
        {
            var key = ReaderEndpoint.StationKey(s.Id);
            endpoints.Add(new ReaderEndpoint(key, ReaderRole.Station, s.Id, s.Code, s.ReaderIp));
            macs[key] = s.ReaderMac;
        }

        var assignIp = Settings.Get(SettingsService.AssignmentReaderIp);
        if (assignIp.Length > 0)
        {
            endpoints.Add(new ReaderEndpoint(ReaderEndpoint.AssignmentKey, ReaderRole.Assignment, null,
                AssignmentStationCode, assignIp));
            macs[ReaderEndpoint.AssignmentKey] = Settings.Get(SettingsService.AssignmentReaderMac);
        }

        Readers.Configure(endpoints, macs);
    }

    /// <summary>Station code used in logs for the work order assigning station.</summary>
    public const string AssignmentStationCode = "ASSIGN";

    // The MAC is a reader's permanent identity: keep it next to the address and warn if a
    // different unit answers there (README section 2).
    private void OnReaderIdentityChanged(object? sender, ReaderIdentityEventArgs e)
    {
        if (e.Endpoint.Role == ReaderRole.Assignment)
        {
            if (Settings.Get(SettingsService.AssignmentReaderIp) == e.Endpoint.Host)
                Settings.Set(SettingsService.AssignmentReaderMac, e.Mac);
        }
        else if (e.Endpoint.StationId is int id)
        {
            Stations.SetReaderMac(id, e.Mac);
        }

        // Only two real MACs prove another unit; firmware that reports 00:00:00:00:00:00 cannot.
        if (ReaderAddress.IsUsableMac(e.PreviousMac) && ReaderAddress.IsUsableMac(e.Mac))
            Log(e.Endpoint, ScanLogType.Error,
                $"A different reader answers at {e.Endpoint.Host}: MAC {e.Mac} ({e.Device}), was {e.PreviousMac}");
    }

    private void OnReaderConnectionChanged(object? sender, ReaderConnectionEventArgs e)
        => Log(e.Endpoint, e.Online ? ScanLogType.Info : ScanLogType.Error, e.Detail);

    private void OnReaderNotice(object? sender, ReaderConnectionEventArgs e)
        => Log(e.Endpoint, ScanLogType.Info, e.Detail);

    private void Log(ReaderEndpoint endpoint, ScanLogType type, string message)
    {
        try
        {
            Trace.LogScan("", endpoint.StationId, endpoint.StationCode, endpoint.Host, "", type, message);
        }
        catch
        {
            // Logging must never stop the readers.
        }
    }

    public AppConfig Config { get; }
    public DatabaseService Database { get; }
    public SettingsService Settings { get; }
    public StationService Stations { get; }
    public RouteService Routes { get; }
    public TraceService Trace { get; }
    public TagService Tags { get; }
    public ReportService Reports { get; }

    /// <summary>Network RFID readers (production stations and the work order assigning station).</summary>
    public NetworkReaderService Readers { get; }

    /// <summary>Writes work orders to tags on the work order assigning station.</summary>
    public TagWriter TagWriter { get; }

    public SimulatedScannerService? Simulated { get; }
    public ScanCoordinator Coordinator { get; }

    public void Dispose()
    {
        Stations.Changed -= OnConfigurationChanged;
        Settings.Changed -= OnConfigurationChanged;
        Readers.IdentityChanged -= OnReaderIdentityChanged;
        Readers.ConnectionChanged -= OnReaderConnectionChanged;
        Readers.Notice -= OnReaderNotice;
        TagWriter.Dispose();
        Coordinator.Dispose();
        Readers.Dispose();
        Simulated?.Dispose();
        Database.Dispose();
    }
}
