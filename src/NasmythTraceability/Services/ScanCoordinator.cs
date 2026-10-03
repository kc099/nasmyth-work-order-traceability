using NasmythTraceability.Data;
using NasmythTraceability.Models;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.Services;

/// <summary>Result of pushing one raw read through the pipeline - what the UI listens for.</summary>
public sealed class ScanProcessedEventArgs : EventArgs
{
    /// <summary>The work order. Empty when the tag that was read is not assigned to one.</summary>
    public required string Barcode { get; init; }

    /// <summary>The RFID tag that was read; empty when a work order was fed in directly.</summary>
    public string TagId { get; init; } = "";
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
/// The heart of the scanning flow: takes tag reads from a reader, works out the station and
/// the work order the tag carries, applies the sequence rules, records the trace, and raises
/// <see cref="ScanProcessed"/> for the view models.
/// <para>
/// Sequence rules: a work order must be scanned at an earlier station before the final one.
/// A scan at the final station completes it and logs the exit from the station it came from.
/// A scan at the final station first is recorded as NG, and the work order then stays blocked
/// until that scan is deleted.
/// </para>
/// </summary>
public sealed class ScanCoordinator : IDisposable
{
    private readonly IScannerService _scanner;
    private readonly StationService _stations;
    private readonly TraceService _trace;
    private readonly TagService _tags;
    private readonly SettingsService _settings;
    private readonly object _gate = new();

    public ScanCoordinator(IScannerService scanner, StationService stations,
        TraceService trace, TagService tags, SettingsService settings)
    {
        _scanner = scanner;
        _stations = stations;
        _trace = trace;
        _tags = tags;
        _settings = settings;
        _scanner.BarcodeScanned += OnBarcodeScanned;
    }

    public event EventHandler<ScanProcessedEventArgs>? ScanProcessed;

    /// <summary>
    /// When set, every reader read is handed to this instead of being tracked. Used while a
    /// tag is being assigned to a work order, or a reader to a station.
    /// </summary>
    public Action<BarcodeScannedEventArgs>? ReadInterceptor { get; set; }

    /// <summary>Feeds a work order straight in, without a tag. There is no screen for this; the self-test uses it.</summary>
    public ScanProcessedEventArgs SubmitManualScan(int stationId, string workOrder, string deviceName = "Manual entry")
    {
        var station = _stations.GetStation(stationId);
        return Process(TagService.NormalizeWorkOrder(workOrder), "", station, "", deviceName);
    }

    private void OnBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        if (ReadInterceptor is { } intercept)
        {
            intercept(e);
            return;
        }

        if (e.IsNewDevice && !TryLinkNewReader(e))
            return;

        var station = _stations.ResolveStationForDevice(e.DeviceKey);
        var tagId = TagService.NormalizeTag(e.Barcode);

        // The reader only knows the tag; the work order comes from the assignment.
        var assignment = tagId.Length == 0 ? null : _tags.GetByTag(tagId);
        if (tagId.Length > 0 && assignment is null)
        {
            const string msg = "Tag is not assigned to a work order";
            _trace.LogScan(tagId, station?.Id, station?.Code ?? "", e.DeviceKey, e.DeviceName, ScanLogType.Error, msg);
            Raise(Make("", tagId, station, ScanResult.NG, ScanOutcome.Error, msg, e.DeviceName, null));
            return;
        }

        Process(assignment?.WorkOrder ?? "", tagId, station, e.DeviceKey, e.DeviceName);
    }

    /// <summary>
    /// First tap on a reader that is not linked yet: link it to a station so this read and
    /// every later one is recorded. Returns false when the read must be ignored.
    /// </summary>
    private bool TryLinkNewReader(BarcodeScannedEventArgs e)
    {
        if (!_settings.GetBool(SettingsService.AutoDetectReaders, true))
            return false;

        // Already linked: carry on if it is enabled, stay silent if it was switched off.
        var existing = _stations.GetDeviceByKey(e.DeviceKey);
        if (existing is not null)
            return existing.IsEnabled;

        var station = _stations.PickStationForNewReader();
        if (station is null)
        {
            _trace.LogScan(e.Barcode, null, "", e.DeviceKey, e.DeviceName, ScanLogType.Info,
                "New reader detected but every station already has a reader");
            return false;
        }

        _stations.MapDeviceToStation(e.DeviceKey, e.DeviceName, station.Id);
        _trace.LogScan("", station.Id, station.Code, e.DeviceKey, e.DeviceName, ScanLogType.Info,
            $"New reader detected and linked to {station.Code}");
        return true;
    }

    private ScanProcessedEventArgs Process(string workOrder, string tagId, Station? station,
        string deviceKey, string deviceName)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(workOrder))
            {
                _trace.LogScan(workOrder, station?.Id, station?.Code ?? "", deviceKey, deviceName,
                    ScanLogType.Error, "Empty read");
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Error,
                    "Empty read", deviceName, null));
            }

            if (station is null)
            {
                // Happens for a direct scan with a bad station id, or a reader whose station
                // has been switched off.
                const string msg = "Reader is not linked to an enabled station";
                _trace.LogScan(workOrder, null, "", deviceKey, deviceName, ScanLogType.Info, msg);
                return Raise(Make(workOrder, tagId, null, ScanResult.NG, ScanOutcome.Error, msg, deviceName, null));
            }

            var minLen = _settings.GetInt(SettingsService.MinBarcodeLength, 4);
            if (workOrder.Length < minLen)
            {
                _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName,
                    ScanLogType.Rejected, $"Code shorter than {minLen} characters");
                var t0 = _trace.Record(workOrder, station, ScanResult.NG,
                    $"Too short (min {minLen})", null, deviceKey, station.IsFinal);
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Rejected,
                    $"Too short (min {minLen})", deviceName, t0));
            }

            var current = _trace.GetCurrent(workOrder);

            // A rejected scan stays on record until someone deletes it; nothing is added on top.
            if (current is { Status: TraceStatus.Rejected })
                return Ignore(station, ScanLogType.Error, ScanOutcome.Error,
                    "Blocked - delete the rejected scan in Scan Information first");

            if (current is { Status: TraceStatus.Completed })
                return Ignore(station, ScanLogType.Duplicate,
                    current.StationId == station.Id ? ScanOutcome.Duplicate : ScanOutcome.Error,
                    "Work order is already completed");

            if (current is not null && current.StationId == station.Id)
                return Ignore(station, ScanLogType.Duplicate, ScanOutcome.Duplicate, $"Already at {station.Code}");

            // The final station comes last: a work order that was never scanned anywhere else
            // has skipped a station. Recorded as NG so the mistake is visible and can be deleted.
            if (station.IsFinal && current is null && HasEarlierStation())
            {
                const string msg = "Out of sequence - not scanned at an earlier station";
                _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName, ScanLogType.Rejected, msg);
                var ng = _trace.Record(workOrder, station, ScanResult.NG, msg, null, deviceKey, isFinalStation: false);
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Rejected, msg, deviceName, ng));
            }

            // "Completed" is set by TraceService when the station is the final one, and the
            // exit from the previous station is stamped in the same transaction.
            var trace = _trace.Record(workOrder, station, ScanResult.OK,
                station.IsFinal ? "Reached final station" : "", null, deviceKey, station.IsFinal);

            var message = station.IsFinal ? "Completed" : $"Entered {station.Code}";
            _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName, ScanLogType.Raw, message);

            if (current is not null)
            {
                message += $" - exit from {current.StationCode} logged";
                _trace.LogScan(workOrder, current.StationId, current.StationCode, deviceKey, deviceName,
                    ScanLogType.Info, $"Exit from {current.StationCode}");
            }

            return Raise(Make(workOrder, tagId, station, ScanResult.OK, ScanOutcome.Accepted, message, deviceName, trace));

            ScanProcessedEventArgs Ignore(Station at, ScanLogType logType, ScanOutcome outcome, string msg)
            {
                _trace.LogScan(workOrder, at.Id, at.Code, deviceKey, deviceName, logType, msg);
                return Raise(Make(workOrder, tagId, at,
                    outcome == ScanOutcome.Duplicate ? ScanResult.OK : ScanResult.NG, outcome, msg, deviceName, null));
            }
        }
    }

    /// <summary>True when there is an enabled station other than the final one to be scanned at first.</summary>
    private bool HasEarlierStation() => _stations.GetStations(includeDisabled: false).Any(s => !s.IsFinal);

    private static ScanProcessedEventArgs Make(string workOrder, string tagId, Station? station, ScanResult result,
        ScanOutcome outcome, string message, string deviceName, TraceHistory? trace) => new()
    {
        Barcode = workOrder,
        TagId = tagId,
        Station = station,
        Result = result,
        Outcome = outcome,
        Message = message,
        DeviceName = deviceName,
        Timestamp = DateTime.Now,
        Trace = trace,
    };

    private ScanProcessedEventArgs Raise(ScanProcessedEventArgs args)
    {
        ScanProcessed?.Invoke(this, args);
        return args;
    }

    public void Dispose() => _scanner.BarcodeScanned -= OnBarcodeScanned;
}
