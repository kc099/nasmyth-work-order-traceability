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
    private readonly IScannerService[] _scanners;
    private readonly StationService _stations;
    private readonly TraceService _trace;
    private readonly TagService _tags;
    private readonly SettingsService _settings;
    private readonly object _gate = new();

    public ScanCoordinator(StationService stations, TraceService trace, TagService tags, SettingsService settings,
        params IScannerService[] scanners)
    {
        _scanners = scanners;
        _stations = stations;
        _trace = trace;
        _tags = tags;
        _settings = settings;
        foreach (var s in _scanners)
            s.BarcodeScanned += OnBarcodeScanned;
    }

    public event EventHandler<ScanProcessedEventArgs>? ScanProcessed;

    /// <summary>Feeds a work order straight in, without a tag. There is no screen for this; the self-test uses it.</summary>
    public ScanProcessedEventArgs SubmitManualScan(int stationId, string workOrder, string deviceName = "Manual entry")
    {
        var station = _stations.GetStation(stationId);
        return Process(TagService.NormalizeWorkOrder(workOrder), "", station, "", deviceName, DateTime.Now);
    }

    private void OnBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        // A reader that belongs to a station that has been switched off is not tracked.
        var station = e.StationId is int id && _stations.GetStation(id) is { IsEnabled: true } s ? s : null;
        var tagId = TagService.NormalizeTag(e.Barcode);

        // The reader only knows the tag; the work order comes from the assignment, keyed on the
        // uid. The text on the card is a convenience label that anyone with a writer can change.
        var assignment = tagId.Length == 0 ? null : _tags.GetByTag(tagId);
        if (tagId.Length > 0 && assignment is null)
        {
            var msg = "Tag is not assigned to a work order" +
                      (string.IsNullOrWhiteSpace(e.CardData) ? "" : $" (card reads '{e.CardData.Trim()}')");
            _trace.LogScan("", station?.Id, station?.Code ?? "", e.DeviceKey, e.DeviceName, ScanLogType.Error, msg,
                e.Timestamp, ScanOutcome.Error, tagId);
            Raise(Make("", tagId, station, ScanResult.NG, ScanOutcome.Error, msg, e.DeviceName, null, e.Timestamp));
            return;
        }

        Process(assignment?.WorkOrder ?? "", tagId, station, e.DeviceKey, e.DeviceName, e.Timestamp);
    }

    private ScanProcessedEventArgs Process(string workOrder, string tagId, Station? station,
        string deviceKey, string deviceName, DateTime at)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(workOrder))
            {
                _trace.LogScan(workOrder, station?.Id, station?.Code ?? "", deviceKey, deviceName,
                    ScanLogType.Error, "Empty read", at, ScanOutcome.Error, tagId);
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Error,
                    "Empty read", deviceName, null, at));
            }

            if (station is null)
            {
                // Happens for a direct scan with a bad station id, or a tap at a reader whose
                // station has been switched off.
                const string msg = "Station is disabled or no longer exists";
                _trace.LogScan(workOrder, null, "", deviceKey, deviceName, ScanLogType.Info, msg, at, ScanOutcome.Error, tagId);
                return Raise(Make(workOrder, tagId, null, ScanResult.NG, ScanOutcome.Error, msg, deviceName, null, at));
            }

            var minLen = _settings.GetInt(SettingsService.MinBarcodeLength, 4);
            if (workOrder.Length < minLen)
            {
                _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName,
                    ScanLogType.Rejected, $"Code shorter than {minLen} characters", at, ScanOutcome.Rejected, tagId);
                var t0 = _trace.Record(workOrder, station, ScanResult.NG,
                    $"Too short (min {minLen})", null, deviceKey, station.IsFinal, at);
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Rejected,
                    $"Too short (min {minLen})", deviceName, t0, at));
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
                _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName, ScanLogType.Rejected, msg, at, ScanOutcome.Rejected, tagId);
                var ng = _trace.Record(workOrder, station, ScanResult.NG, msg, null, deviceKey, isFinalStation: false, at: at);
                return Raise(Make(workOrder, tagId, station, ScanResult.NG, ScanOutcome.Rejected, msg, deviceName, ng, at));
            }

            // "Completed" is set by TraceService when the station is the final one, and the
            // exit from the previous station is stamped in the same transaction.
            var trace = _trace.Record(workOrder, station, ScanResult.OK,
                station.IsFinal ? "Reached final station" : "", null, deviceKey, station.IsFinal, at);

            var message = station.IsFinal ? "Completed" : $"Entered {station.Code}";
            _trace.LogScan(workOrder, station.Id, station.Code, deviceKey, deviceName, ScanLogType.Raw, message, at, ScanOutcome.Accepted, tagId);

            if (current is not null)
            {
                message += $" - exit from {current.StationCode} logged";
                _trace.LogScan(workOrder, current.StationId, current.StationCode, deviceKey, deviceName,
                    ScanLogType.Info, $"Exit from {current.StationCode}", at);
            }

            return Raise(Make(workOrder, tagId, station, ScanResult.OK, ScanOutcome.Accepted, message, deviceName, trace, at));

            ScanProcessedEventArgs Ignore(Station st, ScanLogType logType, ScanOutcome outcome, string msg)
            {
                _trace.LogScan(workOrder, st.Id, st.Code, deviceKey, deviceName, logType, msg, at, outcome, tagId);
                return Raise(Make(workOrder, tagId, st,
                    outcome == ScanOutcome.Duplicate ? ScanResult.OK : ScanResult.NG, outcome, msg, deviceName, null, at));
            }
        }
    }

    /// <summary>True when there is an enabled station other than the final one to be scanned at first.</summary>
    private bool HasEarlierStation() => _stations.GetStations(includeDisabled: false).Any(s => !s.IsFinal);

    private static ScanProcessedEventArgs Make(string workOrder, string tagId, Station? station, ScanResult result,
        ScanOutcome outcome, string message, string deviceName, TraceHistory? trace, DateTime at) => new()
    {
        Barcode = workOrder,
        TagId = tagId,
        Station = station,
        Result = result,
        Outcome = outcome,
        Message = message,
        DeviceName = deviceName,
        Timestamp = at,
        Trace = trace,
    };

    private ScanProcessedEventArgs Raise(ScanProcessedEventArgs args)
    {
        ScanProcessed?.Invoke(this, args);
        return args;
    }

    public void Dispose()
    {
        foreach (var s in _scanners)
            s.BarcodeScanned -= OnBarcodeScanned;
    }
}
