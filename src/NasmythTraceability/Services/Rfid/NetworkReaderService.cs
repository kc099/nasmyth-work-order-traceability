using System.Net.Http;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.Services.Rfid;

/// <summary>
/// Runs one <see cref="ReaderPoller"/> per configured network reader, for as long as the app
/// runs - polling never depends on which page is open.
/// <list type="bullet">
/// <item>Production station reads are raised as <see cref="BarcodeScanned"/> for the scan coordinator.</item>
/// <item>The work order assigning station's events are raised as <see cref="AssignmentCardEvent"/>
/// and are never tracked.</item>
/// </list>
/// Events are raised on the thread that created the service (the UI thread in the app), in the
/// order the readers reported them.
/// </summary>
public sealed class NetworkReaderService : IScannerService
{
    private readonly RfidReaderClient _client;
    private readonly ReaderCursorStore _cursors;
    private readonly Func<int> _pollIntervalMs;
    private readonly SynchronizationContext? _context;
    private readonly Dictionary<string, ReaderPoller> _pollers = new();
    private readonly object _lock = new();
    private bool _started;
    private bool _disposed;

    public NetworkReaderService(ReaderCursorStore cursors, Func<int> pollIntervalMs, HttpMessageHandler? handler = null)
    {
        _cursors = cursors;
        _pollIntervalMs = pollIntervalMs;
        _client = new RfidReaderClient(handler);
        _context = SynchronizationContext.Current;
    }

    /// <summary>A card read at a production station.</summary>
    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    /// <summary>A card read or written at the work order assigning station.</summary>
    public event EventHandler<ReaderCardEvent>? AssignmentCardEvent;

    /// <summary>The assignment reader restarted and has lost any write that was armed.</summary>
    public event EventHandler? AssignmentReaderRestarted;

    public event EventHandler<ReaderStatusInfo>? StatusChanged;

    /// <summary>A reader reported its MAC for the first time, or a different one than on record.</summary>
    public event EventHandler<ReaderIdentityEventArgs>? IdentityChanged;

    /// <summary>A reader went online or offline.</summary>
    public event EventHandler<ReaderConnectionEventArgs>? ConnectionChanged;

    /// <summary>Something worth a line in the scan log, e.g. a reader found in write mode.</summary>
    public event EventHandler<ReaderConnectionEventArgs>? Notice;

    /// <summary>Asked when the assignment reader is found in write mode; true while a write is pending.</summary>
    public Func<bool>? AssignmentWriteAllowed { get; set; }

    public bool IsRunning => _started && !_disposed;

    /// <summary>
    /// Sets the readers to poll. Readers whose address or role is unchanged keep polling without
    /// a gap; changed ones are restarted, removed ones stopped.
    /// </summary>
    public void Configure(IEnumerable<ReaderEndpoint> endpoints, IReadOnlyDictionary<string, string> knownMacs)
    {
        var removed = new List<ReaderEndpoint>();
        lock (_lock)
        {
            if (_disposed)
                return;

            var desired = endpoints.Where(e => !string.IsNullOrEmpty(e.Host))
                                   .GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First());

            foreach (var (key, poller) in _pollers.ToList())
            {
                if (desired.TryGetValue(key, out var want) && want == poller.Endpoint)
                    continue;
                poller.Dispose();
                _pollers.Remove(key);
                removed.Add(poller.Endpoint);
            }

            foreach (var (key, endpoint) in desired)
            {
                var mac = knownMacs.TryGetValue(key, out var m) ? m : "";
                if (_pollers.TryGetValue(key, out var existing))
                {
                    if (!string.IsNullOrEmpty(mac))
                        existing.KnownMac = mac;
                    continue;
                }

                var poller = CreatePoller(endpoint, mac);
                _pollers[key] = poller;
                if (_started)
                    poller.Start();
            }
        }

        foreach (var endpoint in removed)
            Post(() => StatusChanged?.Invoke(this,
                new ReaderStatusInfo { Endpoint = endpoint, State = ReaderState.NotConfigured }));
    }

    /// <summary>Starts polling every configured reader (and any configured later).</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
                return;
            _started = true;
            foreach (var p in _pollers.Values)
                p.Start();
        }
    }

    public ReaderStatusInfo GetStatus(string key)
    {
        lock (_lock)
        {
            return _pollers.TryGetValue(key, out var p)
                ? p.Status
                : new ReaderStatusInfo
                {
                    Endpoint = new ReaderEndpoint(key, ReaderRole.Station, null, "", ""),
                    State = ReaderState.NotConfigured,
                };
        }
    }

    public ReaderStatusInfo AssignmentStatus => GetStatus(ReaderEndpoint.AssignmentKey);

    public ReaderStatusInfo StationStatus(int stationId) => GetStatus(ReaderEndpoint.StationKey(stationId));

    /// <summary>Sends a mode change to the assignment reader, in turn with its polls.</summary>
    public Task<RfidStatusDto> SetAssignmentModeAsync(RfidModeRequest request, CancellationToken ct = default)
    {
        ReaderPoller? poller;
        lock (_lock)
            _pollers.TryGetValue(ReaderEndpoint.AssignmentKey, out poller);

        return poller is null
            ? Task.FromException<RfidStatusDto>(new InvalidOperationException(
                "No work order assigning station is set up. Enter its IP address in Settings > Stations."))
            : poller.SetModeAsync(request, ct);
    }

    /// <summary>
    /// GET /api/status on any address (Settings "Test" button). If that address is being
    /// polled the request waits its turn, so it never collides with a poll.
    /// </summary>
    public Task<RfidStatusDto> ProbeAsync(string host, CancellationToken ct = default)
    {
        ReaderPoller? poller;
        lock (_lock)
            poller = _pollers.Values.FirstOrDefault(p => string.Equals(p.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase));

        return poller is not null ? poller.ProbeAsync(ct) : _client.GetStatusAsync(host, ct);
    }

    private ReaderPoller CreatePoller(ReaderEndpoint endpoint, string knownMac)
    {
        var poller = new ReaderPoller(endpoint, knownMac, _client, _cursors, _pollIntervalMs);
        ReaderState? lastConnection = null;

        poller.WriteModeAllowed = endpoint.Role == ReaderRole.Assignment
            ? () => AssignmentWriteAllowed?.Invoke() == true
            : () => false;

        poller.CardEvent = (e, bootId) => Post(() =>
        {
            if (!IsCurrent(poller))
                return;

            if (endpoint.Role == ReaderRole.Assignment)
            {
                AssignmentCardEvent?.Invoke(this, e);
                return;
            }

            if (e.IsWrite)
            {
                // A production reader was put in write mode by someone else and has just
                // overwritten a card. Switch it back; the tap still counts, the uid is unchanged.
                _ = poller.SetModeAsync(RfidModeRequest.Read()).ContinueWith(_ => { }, TaskScheduler.Default);
                Notice?.Invoke(this, new ReaderConnectionEventArgs(endpoint, true,
                    "Reader was in write mode and wrote a card - switched back to read mode"));
            }

            BarcodeScanned?.Invoke(this,
                new BarcodeScannedEventArgs(e.Uid, string.IsNullOrEmpty(e.Mac) ? endpoint.Host : e.Mac,
                    string.IsNullOrEmpty(e.Device) ? endpoint.Host : e.Device, e.Timestamp)
                {
                    StationId = endpoint.StationId,
                    CardData = e.Data,
                    ReadError = e.Ok ? "" : e.Error,
                });

            // Only now is the tap safely recorded; a restart carries on after it.
            _cursors.Save(e.ReaderKey, bootId, e.Id);
        });

        poller.StatusChanged = status => Post(() =>
        {
            if (!IsCurrent(poller))
                return;

            StatusChanged?.Invoke(this, status);

            if (status.State is ReaderState.Online or ReaderState.Offline && status.State != lastConnection)
            {
                lastConnection = status.State;
                var online = status.State == ReaderState.Online;
                ConnectionChanged?.Invoke(this, new ReaderConnectionEventArgs(endpoint, online,
                    online ? $"Reader online ({status.Device}, {endpoint.Host})"
                           : $"Reader offline ({endpoint.Host}): {status.LastError}"));
            }
        });

        poller.Identified = (status, previousMac) => Post(() =>
        {
            if (IsCurrent(poller))
                IdentityChanged?.Invoke(this,
                    new ReaderIdentityEventArgs(endpoint, status.Mac, status.Device, previousMac));
        });

        poller.Restarted = () => Post(() =>
        {
            if (IsCurrent(poller) && endpoint.Role == ReaderRole.Assignment)
                AssignmentReaderRestarted?.Invoke(this, EventArgs.Empty);
        });

        poller.Notice = text => Post(() =>
        {
            if (IsCurrent(poller))
                Notice?.Invoke(this, new ReaderConnectionEventArgs(endpoint, true, text));
        });

        return poller;
    }

    /// <summary>False for a poller that was replaced or stopped while its callback was queued.</summary>
    private bool IsCurrent(ReaderPoller poller)
    {
        lock (_lock)
            return !_disposed && _pollers.TryGetValue(poller.Endpoint.Key, out var p) && ReferenceEquals(p, poller);
    }

    private void Post(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(_ => action(), null);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var p in _pollers.Values)
                p.Dispose();
            _pollers.Clear();
        }

        _client.Dispose();
    }
}
