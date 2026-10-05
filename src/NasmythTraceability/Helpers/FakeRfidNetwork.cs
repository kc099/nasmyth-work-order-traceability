using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NasmythTraceability.Services.Rfid;

namespace NasmythTraceability.Helpers;

/// <summary>
/// In-process stand-in for a network of ESP32 + RC522 readers, used by the self-test. It
/// answers the reader HTTP API (README section 3) the way the firmware does, so the real
/// polling, mode and tag-writing code runs against it unchanged.
/// </summary>
public sealed class FakeRfidNetwork : HttpMessageHandler
{
    private readonly Dictionary<string, FakeRfidReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Content-Type header of the last request that had a body.</summary>
    public string LastContentType { get; private set; } = "";

    public FakeRfidReader Add(string host, string mac, string device)
    {
        var reader = new FakeRfidReader(mac, device);
        Put(host, reader);
        return reader;
    }

    /// <summary>Puts a reader at an address (replacing whatever answered there before).</summary>
    public void Put(string host, FakeRfidReader reader)
    {
        lock (_lock)
            _readers[host] = reader;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Off the caller's thread, like a real socket, without capturing its synchronization context.
        await Task.Delay(1, ct).ConfigureAwait(false);

        var uri = request.RequestUri!;
        FakeRfidReader? reader;
        lock (_lock)
            _readers.TryGetValue(uri.IsDefaultPort ? uri.Host : uri.Authority, out reader);

        if (reader is null || reader.Offline)
            throw new HttpRequestException("No route to host",
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostUnreachable));

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (request.Content is not null)
            LastContentType = request.Content.Headers.ContentType?.ToString() ?? "";
        var (status, json) = reader.Handle(request.Method, uri.AbsolutePath, uri.Query, body);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

/// <summary>One fake reader: mode, write data, cards it has seen, the last 16 events.</summary>
public sealed class FakeRfidReader
{
    private const int MaxEvents = 16;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _lock = new();
    private readonly List<RfidEventDto> _events = new();
    private readonly Dictionary<string, string> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private long _lastEventId;
    private string? _failNextWrite;

    public FakeRfidReader(string mac, string device)
    {
        Mac = mac;
        Device = device;
        BootId = Random.Shared.NextInt64(1, uint.MaxValue);
    }

    public string Mac { get; }
    public string Device { get; }
    public long BootId { get; private set; }
    public string Mode { get; set; } = "read";
    public string Data { get; set; } = "";
    public bool Once { get; set; }

    /// <summary>True: the reader does not answer (switched off, out of Wi-Fi range).</summary>
    public bool Offline { get; set; }

    /// <summary>Number of POST /api/mode requests received.</summary>
    public int ModeRequests { get; private set; }

    /// <summary>What a card holds, as written by this reader.</summary>
    public string CardData(string uid)
    {
        lock (_lock)
            return _cards.TryGetValue(uid, out var d) ? d : "";
    }

    /// <summary>Puts text on a card as if it had been written elsewhere.</summary>
    public void SetCard(string uid, string data)
    {
        lock (_lock)
            _cards[uid] = data;
    }

    /// <summary>The next write attempt fails with this error; the reader stays armed.</summary>
    public void FailNextWrite(string error)
    {
        lock (_lock)
            _failNextWrite = error;
    }

    /// <summary>A card is presented to the reader. Works whether or not anyone is polling.</summary>
    public RfidEventDto Tap(string uid)
    {
        lock (_lock)
        {
            var e = new RfidEventDto
            {
                Id = ++_lastEventId,
                Ms = _uptime.ElapsedMilliseconds,
                Type = Mode,
                Uid = uid,
                CardType = "MIFARE 1KB",
            };

            if (Mode == "write")
            {
                if (_failNextWrite is { } error)
                {
                    _failNextWrite = null;
                    e.Ok = false;
                    e.Error = error;
                }
                else
                {
                    _cards[uid] = Data;
                    e.Ok = true;
                    e.Data = Data;
                    if (Once)
                    {
                        Mode = "read";
                        Once = false;
                    }
                }
            }
            else
            {
                e.Ok = true;
                e.Data = _cards.TryGetValue(uid, out var d) ? d : "";
            }

            e.Hex = Convert.ToHexString(Encoding.ASCII.GetBytes(e.Data.PadRight(16, '\0')));
            _events.Add(e);
            while (_events.Count > MaxEvents)
                _events.RemoveAt(0);
            return e;
        }
    }

    /// <summary>Power cycle: new boot id, events and mode lost, back in read mode.</summary>
    public void Reboot()
    {
        lock (_lock)
        {
            BootId = Random.Shared.NextInt64(1, uint.MaxValue);
            _events.Clear();
            _lastEventId = 0;
            Mode = "read";
            Data = "";
            Once = false;
            _uptime.Restart();
        }
    }

    internal (HttpStatusCode, string) Handle(HttpMethod method, string path, string query, string body)
    {
        lock (_lock)
        {
            if (method == HttpMethod.Get && path == "/api/status")
                return (HttpStatusCode.OK, JsonSerializer.Serialize(Status(), Json));

            if (method == HttpMethod.Get && path == "/api/events")
            {
                var since = 0L;
                foreach (var part in query.TrimStart('?').Split('&'))
                    if (part.StartsWith("since=") && long.TryParse(part[6..], out var s))
                        since = s;

                return (HttpStatusCode.OK, JsonSerializer.Serialize(new RfidEventsDto
                {
                    Device = Device,
                    BootId = BootId,
                    UptimeMs = _uptime.ElapsedMilliseconds,
                    LastEventId = _lastEventId,
                    Events = _events.Where(e => e.Id > since).ToList(),
                }, Json));
            }

            if (method == HttpMethod.Post && path == "/api/mode")
                return SetMode(body);

            return (HttpStatusCode.NotFound, "{\"error\":\"not found\"}");
        }
    }

    private (HttpStatusCode, string) SetMode(string body)
    {
        ModeRequests++;
        RfidModeRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<RfidModeRequest>(body, Json);
        }
        catch (JsonException)
        {
            return (HttpStatusCode.BadRequest, "{\"error\":\"bad json\"}");
        }

        if (req is null || (req.Mode is null && req.Data is null && req.Once is null))
            return (HttpStatusCode.BadRequest, "{\"error\":\"nothing to change\"}");

        if (req.Data is not null)
        {
            if (req.Data.Length is < 1 or > 16 || req.Data.Any(c => c is < (char)0x20 or > (char)0x7E))
                return (HttpStatusCode.BadRequest, "{\"error\":\"data must be 1-16 printable ASCII characters\"}");
            Data = req.Data;
        }

        if (req.Mode == "write")
        {
            if (Data.Length == 0)
                return (HttpStatusCode.BadRequest, "{\"error\":\"no data to write\"}");
            Mode = "write";
            Once = req.Once == true;
        }
        else if (req.Mode == "read")
        {
            Mode = "read";
            Once = false;
        }

        return (HttpStatusCode.OK, JsonSerializer.Serialize(Status(), Json));
    }

    private RfidStatusDto Status() => new()
    {
        Mode = Mode,
        Data = Data,
        Once = Once,
        Device = Device,
        Firmware = "1.2.0",
        Mac = Mac,
        BootId = BootId,
        ReaderOk = true,
        UptimeMs = _uptime.ElapsedMilliseconds,
        LastEventId = _lastEventId,
    };
}
