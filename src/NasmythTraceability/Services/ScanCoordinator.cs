using NasmythTraceability.Data;
using NasmythTraceability.Models;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.Services;

/// <summary>Result of pushing one raw read through the pipeline - what the UI listens for.</summary>
public sealed class ScanProcessedEventArgs : EventArgs
{
    public required string Barcode { get; init; }
    public Station? Station { get; init; }
    public ScanResult Result { get; init; }
    public ScanOutcome Outcome { get; init; }
    public string Message { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public TraceHistory? Trace { get; init; }

    public string StationCode => Station?.Code ?? "-";
    public bool IsAccepted => Outcome == ScanOutcome.Accepted;
}

/// <summary>
/// The heart of the scanning flow: takes reads from a scanner source, works out the
/// station, applies duplicate / length checks, records the trace, and raises
/// <see cref="ScanProcessed"/> for the view models. There is no fixed routing - a
/// barcode may visit stations in any order; only the final station is fixed.
/// </summary>
public sealed class ScanCoordinator : IDisposable
{
    private readonly IScannerService _scanner;
    private readonly StationService _stations;
    private readonly TraceService _trace;
    private readonly SettingsService _settings;
    private readonly object _gate = new();

    public ScanCoordinator(IScannerService scanner, StationService stations,
        TraceService trace, SettingsService settings)
    {
        _scanner = scanner;
        _stations = stations;
        _trace = trace;
        _settings = settings;
        _scanner.BarcodeScanned += OnBarcodeScanned;
    }

    public event EventHandler<ScanProcessedEventArgs>? ScanProcessed;

    /// <summary>Feed a scan directly (dashboard "simulate", Settings "test scanner", self-test).</summary>
    public ScanProcessedEventArgs SubmitManualScan(int stationId, string barcode, string deviceName = "Manual entry")
    {
        var station = _stations.GetStation(stationId);
        return Process(barcode?.Trim() ?? "", station, "", deviceName, manualStation: true);
    }

    private void OnBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        // The reader only forwards reads from mapped scanners, so the station resolves here.
        var station = _stations.ResolveStationForDevice(e.DeviceKey);
        Process(e.Barcode.Trim(), station, e.DeviceKey, e.DeviceName, manualStation: false);
    }

    private ScanProcessedEventArgs Process(string barcode, Station? station, string deviceKey,
        string deviceName, bool manualStation)
    {
        lock (_gate)
        {
            ScanProcessedEventArgs result;

            if (string.IsNullOrWhiteSpace(barcode))
            {
                _trace.LogScan(barcode, station?.Id, station?.Code ?? "", deviceKey, deviceName,
                    ScanLogType.Error, "Empty barcode");
                result = Make(barcode, station, ScanResult.NG, ScanOutcome.Error, "Empty barcode", deviceName, null);
                Raise(result);
                return result;
            }

            if (station is null)
            {
                // Reader normally filters these out; this only happens for a manual scan with a
                // bad station id, or the brief window just after a scanner is mapped.
                const string msg = "Scanner is not mapped to a station";
                _trace.LogScan(barcode, null, "", deviceKey, deviceName, ScanLogType.Info, msg);
                result = Make(barcode, null, ScanResult.NG, ScanOutcome.Error, msg, deviceName, null);
                Raise(result);
                return result;
            }

            var minLen = _settings.GetInt(SettingsService.MinBarcodeLength, 4);
            if (barcode.Length < minLen)
            {
                _trace.LogScan(barcode, station.Id, station.Code, deviceKey, deviceName,
                    ScanLogType.Rejected, $"Barcode shorter than {minLen} characters");
                var t0 = _trace.Record(barcode, station, ScanResult.NG,
                    $"Too short (min {minLen})", null, deviceKey, station.IsFinal);
                result = Make(barcode, station, ScanResult.NG, ScanOutcome.Rejected,
                    $"Too short (min {minLen})", deviceName, t0);
                Raise(result);
                return result;
            }

            var window = TimeSpan.FromSeconds(Math.Max(0, _settings.GetInt(SettingsService.DuplicateWindowSeconds, 5)));
            if (window > TimeSpan.Zero && _trace.IsDuplicate(barcode, station.Id, window))
            {
                _trace.LogScan(barcode, station.Id, station.Code, deviceKey, deviceName,
                    ScanLogType.Duplicate, $"Repeat within {window.TotalSeconds:0}s at {station.Code}");
                result = Make(barcode, station, ScanResult.OK, ScanOutcome.Duplicate,
                    "Duplicate scan ignored", deviceName, null);
                Raise(result);
                return result;
            }

            // No route validation - every valid, non-duplicate read is recorded as OK.
            // "Completed" is set by TraceService when the station is the fixed final one.
            var message = station.IsFinal ? "Reached final station" : "";
            var trace = _trace.Record(barcode, station, ScanResult.OK, message,
                null, deviceKey, station.IsFinal);

            _trace.LogScan(barcode, station.Id, station.Code, deviceKey, deviceName,
                ScanLogType.Raw, string.IsNullOrEmpty(message) ? "Accepted" : message);

            result = Make(barcode, station, ScanResult.OK, ScanOutcome.Accepted, message, deviceName, trace);
            Raise(result);
            return result;
        }
    }

    private static ScanProcessedEventArgs Make(string barcode, Station? station, ScanResult result,
        ScanOutcome outcome, string message, string deviceName, TraceHistory? trace) => new()
    {
        Barcode = barcode,
        Station = station,
        Result = result,
        Outcome = outcome,
        Message = message,
        DeviceName = deviceName,
        Timestamp = DateTime.Now,
        Trace = trace,
    };

    private void Raise(ScanProcessedEventArgs args) => ScanProcessed?.Invoke(this, args);

    public void Dispose() => _scanner.BarcodeScanned -= OnBarcodeScanned;
}
