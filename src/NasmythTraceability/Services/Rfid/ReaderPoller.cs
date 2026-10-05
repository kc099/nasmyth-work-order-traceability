namespace NasmythTraceability.Services.Rfid;

/// <summary>
/// Polls one reader for card events, forever, on a background task (README section 4).
/// Every request to the reader - polls and mode changes - goes through one gate, because the
/// reader serves one request at a time. Callbacks run on the polling task; the owning
/// <see cref="NetworkReaderService"/> moves them to the UI thread.
/// </summary>
internal sealed class ReaderPoller : IDisposable
{
    private const int OfflineAfterFailures = 2;
    private const int MaxBackoffMs = 3000;

    private readonly RfidReaderClient _client;
    private readonly ReaderCursorStore _cursors;
    private readonly Func<int> _intervalMs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _statusLock = new();

    private Task? _loop;
    private long? _bootId;
    private long _since;
    private bool _identified;
    private bool _macWarned;
    private int _failures;
    private ReaderStatusInfo _status;

    public ReaderPoller(ReaderEndpoint endpoint, string knownMac, RfidReaderClient client,
        ReaderCursorStore cursors, Func<int> intervalMs)
    {
        Endpoint = endpoint;
        KnownMac = knownMac;
        _client = client;
        _cursors = cursors;
        _intervalMs = intervalMs;
        _status = new ReaderStatusInfo { Endpoint = endpoint, State = ReaderState.Connecting };
    }

    public ReaderEndpoint Endpoint { get; }

    /// <summary>MAC on record for this address; a different one is reported through <see cref="Identified"/>.</summary>
    public string KnownMac { get; set; }

    /// <summary>
    /// What identifies this reader for the catch-up cursor: its MAC, or its address when the
    /// firmware reports none (00:00:00:00:00:00).
    /// </summary>
    public string ReaderKey => ReaderAddress.IsUsableMac(KnownMac) ? KnownMac : "ip:" + Endpoint.Host;

    public ReaderStatusInfo Status
    {
        get { lock (_statusLock) return _status; }
    }

    /// <summary>A card event, with the reader's boot id (for the catch-up cursor).</summary>
    public Action<ReaderCardEvent, long>? CardEvent { get; set; }

    public Action<ReaderStatusInfo>? StatusChanged { get; set; }

    /// <summary>The reader answered with a MAC other than <see cref="KnownMac"/> (or the first one).</summary>
    public Action<RfidStatusDto, string>? Identified { get; set; }

    /// <summary>The reader restarted while it was being polled; it is back in read mode with no data.</summary>
    public Action? Restarted { get; set; }

    /// <summary>Something worth logging: a mode correction, missed events.</summary>
    public Action<string>? Notice { get; set; }

    /// <summary>
    /// Asked when the reader is found in write mode. A production reader never may be; the
    /// assignment reader only while a write is pending.
    /// </summary>
    public Func<bool>? WriteModeAllowed { get; set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Changes the reader mode between polls. Throws when the reader cannot be reached.</summary>
    public async Task<RfidStatusDto> SetModeAsync(RfidModeRequest request, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var reply = await WithGateAsync(c => _client.SetModeAsync(Endpoint.Host, request, c), linked.Token)
            .ConfigureAwait(false);
        UpdateStatus(s => Copy(s, mode: reply.Mode));
        return reply;
    }

    /// <summary>One GET /api/status between polls (Settings "Test" button).</summary>
    public Task<RfidStatusDto> ProbeAsync(CancellationToken ct)
        => WithGateAsync(c => _client.GetStatusAsync(Endpoint.Host, c), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_identified)
                    await IdentifyAsync(ct).ConfigureAwait(false);

                var since = _since;
                var reply = await WithGateAsync(c => _client.GetEventsAsync(Endpoint.Host, since, c), ct)
                    .ConfigureAwait(false);

                if (reply.BootId != _bootId)
                {
                    // Restarted since the last poll: every event it holds is new. Check it
                    // again too - it may be another unit at this address.
                    _bootId = reply.BootId;
                    _since = 0;
                    _identified = false;
                    UpdateStatus(s => Copy(s, mode: "read"));
                    Notice?.Invoke("Reader restarted");
                    Restarted?.Invoke();
                    continue;
                }

                var events = reply.Events.Where(e => e.Id > _since).OrderBy(e => e.Id).ToList();
                if (events.Count > 0 && _since > 0 && events[0].Id > _since + 1)
                    Notice?.Invoke($"{events[0].Id - _since - 1} card event(s) were lost - more than 16 taps between two polls");

                foreach (var e in events)
                {
                    CardEvent?.Invoke(ToCardEvent(e, reply), reply.BootId);
                    _since = e.Id;
                }

                // Defensive: an event counter that went backwards without a new boot id.
                if (reply.LastEventId < _since)
                    _since = reply.LastEventId;

                MarkOnline();
                await Task.Delay(Math.Clamp(_intervalMs(), 50, 5000), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Offline or a bad reply: keep polling, a little slower, and re-check the reader
                // (address may now answer with another unit, mode may have changed) when it is back.
                _identified = false;
                _failures++;
                if (_failures >= OfflineAfterFailures)
                    UpdateStatus(s => Copy(s, state: ReaderState.Offline, lastError: RfidReaderClient.Describe(ex)));

                try
                {
                    await Task.Delay(Math.Min(500 * _failures, MaxBackoffMs), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task IdentifyAsync(CancellationToken ct)
    {
        var status = await WithGateAsync(c => _client.GetStatusAsync(Endpoint.Host, c), ct).ConfigureAwait(false);

        if (!string.Equals(status.Mac, KnownMac, StringComparison.OrdinalIgnoreCase))
        {
            var previous = KnownMac;
            KnownMac = status.Mac;
            Identified?.Invoke(status, previous);
        }

        if (!ReaderAddress.IsUsableMac(status.Mac) && !_macWarned)
        {
            _macWarned = true;
            Notice?.Invoke($"Reader reports no usable MAC address ({status.Mac}); it is identified by its IP address. " +
                           "Check the reader firmware");
        }

        if (_bootId is null)
        {
            // First contact since the app started. A production reader that was polled before
            // carries on where it left off (taps made while the app was closed are recorded);
            // one that is new to the app, and the assignment reader, start from now.
            var cursor = Endpoint.Role == ReaderRole.Station ? _cursors.Get(ReaderKey) : null;
            _since = cursor switch
            {
                { } c when c.BootId == status.BootId && c.LastEventId <= status.LastEventId => c.LastEventId,
                { } c when c.BootId != status.BootId => 0, // restarted while the app was closed
                _ => status.LastEventId,
            };
            _bootId = status.BootId;
        }

        var mode = status.Mode;
        if (string.Equals(mode, "write", StringComparison.OrdinalIgnoreCase) && WriteModeAllowed?.Invoke() != true)
        {
            // In write mode every card presented is overwritten - never leave a reader like that.
            var reply = await WithGateAsync(c => _client.SetModeAsync(Endpoint.Host, RfidModeRequest.Read(), c), ct)
                .ConfigureAwait(false);
            mode = reply.Mode;
            Notice?.Invoke("Reader was in write mode - switched back to read mode");
        }

        _identified = true;
        UpdateStatus(s => Copy(s, device: status.Device, mac: status.Mac, firmware: status.Firmware,
            mode: mode, readerOk: status.ReaderOk));
    }

    private void MarkOnline()
    {
        _failures = 0;
        UpdateStatus(s => Copy(s, state: ReaderState.Online, lastError: "", lastSeen: DateTime.Now));
    }

    private ReaderCardEvent ToCardEvent(RfidEventDto e, RfidEventsDto reply)
    {
        // The reader has no clock: the event's age is its uptime now minus its uptime then.
        var ageMs = reply.UptimeMs >= e.Ms ? reply.UptimeMs - e.Ms : 0;
        return new ReaderCardEvent
        {
            Endpoint = Endpoint,
            Id = e.Id,
            Type = e.Type,
            Ok = e.Ok,
            Uid = (e.Uid ?? "").Trim().ToUpperInvariant(),
            CardType = e.CardType ?? "",
            Data = e.Data ?? "",
            Error = e.Error ?? "",
            Device = string.IsNullOrEmpty(reply.Device) ? Status.Device : reply.Device,
            Mac = ReaderAddress.IsUsableMac(KnownMac) ? KnownMac : "",
            ReaderKey = ReaderKey,
            Timestamp = DateTime.Now.AddMilliseconds(-Math.Min(ageMs, TimeSpan.FromDays(1).TotalMilliseconds)),
        };
    }

    private async Task<T> WithGateAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await call(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Applies a change; raises <see cref="StatusChanged"/> when anything shown has changed.</summary>
    private void UpdateStatus(Func<ReaderStatusInfo, ReaderStatusInfo> change)
    {
        ReaderStatusInfo before, after;
        lock (_statusLock)
        {
            before = _status;
            after = _status = change(_status);
        }

        var visible = before.State != after.State || before.Device != after.Device || before.Mac != after.Mac
                      || before.Mode != after.Mode || before.ReaderOk != after.ReaderOk
                      || before.LastError != after.LastError || before.Firmware != after.Firmware;
        if (visible)
            StatusChanged?.Invoke(after);
    }

    private static ReaderStatusInfo Copy(ReaderStatusInfo s, ReaderState? state = null, string? device = null,
        string? mac = null, string? firmware = null, string? mode = null, bool? readerOk = null,
        string? lastError = null, DateTime? lastSeen = null) => new()
    {
        Endpoint = s.Endpoint,
        State = state ?? s.State,
        Device = device ?? s.Device,
        Mac = mac ?? s.Mac,
        Firmware = firmware ?? s.Firmware,
        Mode = mode ?? s.Mode,
        ReaderOk = readerOk ?? s.ReaderOk,
        LastError = lastError ?? s.LastError,
        LastSeen = lastSeen ?? s.LastSeen,
    };

    public void Dispose()
    {
        _cts.Cancel();
        CardEvent = null;
        StatusChanged = null;
        Identified = null;
        Restarted = null;
        Notice = null;
    }
}
