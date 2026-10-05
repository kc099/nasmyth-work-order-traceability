using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NasmythTraceability.Services.Rfid;

/// <summary>Error reply from a reader: <c>{"error":"reason"}</c> with HTTP 400/404.</summary>
public sealed class RfidReaderException : Exception
{
    public RfidReaderException(string message) : base(message) { }
}

/// <summary>
/// Thin HTTP client for the reader API. Stateless: callers that talk to the same reader must
/// not overlap requests (the reader serves one request at a time), see <see cref="ReaderPoller"/>.
/// </summary>
public sealed class RfidReaderClient : IDisposable
{
    /// <summary>Per-request timeout; the README asks for 3-5 s.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public RfidReaderClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = RequestTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            })
            : new HttpClient(handler, disposeHandler: false);

        // Timeouts are applied per request with a token, so a slow reader never blocks another.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public Task<RfidStatusDto> GetStatusAsync(string host, CancellationToken ct)
        => SendAsync<RfidStatusDto>(host, HttpMethod.Get, "/api/status", null, ct);

    public Task<RfidEventsDto> GetEventsAsync(string host, long since, CancellationToken ct)
        => SendAsync<RfidEventsDto>(host, HttpMethod.Get, $"/api/events?since={since}", null, ct);

    public Task<RfidStatusDto> SetModeAsync(string host, RfidModeRequest body, CancellationToken ct)
        => SendAsync<RfidStatusDto>(host, HttpMethod.Post, "/api/mode", body, ct);

    private async Task<T> SendAsync<T>(string host, HttpMethod method, string path, object? body,
        CancellationToken ct) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(method, ReaderAddress.ToBaseUri(host) + path.TrimStart('/'));
        // The reader handles one connection at a time; do not hold one open between polls.
        request.Headers.ConnectionClose = true;
        if (body is not null)
        {
            // Plain "application/json", without a charset: the firmware is a small HTTP server.
            request.Content = new ByteArrayContent(SerializeBody(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new RfidReaderException(ReadError(text) ?? $"HTTP {(int)response.StatusCode}");

            return Parse<T>(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("No reply within " + RequestTimeout.TotalSeconds + " s");
        }
        catch (JsonException ex)
        {
            throw new RfidReaderException("Reply is not reader JSON: " + ex.Message);
        }
    }

    /// <summary>The exact bytes sent as a request body.</summary>
    public static byte[] SerializeBody(object body) => JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json);

    /// <summary>Reads a reader reply. Throws <see cref="JsonException"/> when it is not one.</summary>
    public static T Parse<T>(string text) where T : class
        => JsonSerializer.Deserialize<T>(text, Json) ?? throw new JsonException("Empty reply from reader");

    private static string? ReadError(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Short operator-facing text for a failed request.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        TimeoutException => "no reply (timeout)",
        HttpRequestException { InnerException: System.Net.Sockets.SocketException se } => se.SocketErrorCode switch
        {
            System.Net.Sockets.SocketError.ConnectionRefused => "connection refused",
            System.Net.Sockets.SocketError.HostNotFound => "host not found",
            System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable
                => "not reachable",
            System.Net.Sockets.SocketError.TimedOut => "no reply (timeout)",
            _ => se.Message,
        },
        HttpRequestException h => h.Message,
        RfidReaderException r => r.Message,
        _ => ex.Message,
    };

    public void Dispose() => _http.Dispose();
}

/// <summary>Parsing and formatting of the reader address typed in Settings.</summary>
public static class ReaderAddress
{
    /// <summary>
    /// Turns what the operator typed ("192.168.1.57", "http://192.168.1.57/", "host:8080")
    /// into the stored form, host[:port]. Empty input is allowed and means "no reader".
    /// Returns null and sets <paramref name="error"/> when it is not a usable address.
    /// </summary>
    public static string? Normalize(string? input, out string? error)
    {
        error = null;
        var text = (input ?? "").Trim();
        if (text.Length == 0)
            return "";

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            text = text[7..];
        else if (text.Contains("://"))
        {
            error = "Use the reader's IP address, e.g. 192.168.1.57 (http only).";
            return null;
        }

        text = text.TrimEnd('/');
        var hostPart = text.Split(':')[0];

        // Uri accepts shorthand such as "192.168.1"; a dotted address must have all four parts.
        var looksNumeric = hostPart.Length > 0 && hostPart.All(c => char.IsDigit(c) || c == '.');
        var badNumeric = looksNumeric
                         && hostPart.Split('.') is var parts
                         && (parts.Length != 4 || parts.Any(p => p.Length is 0 or > 3 || int.Parse(p) > 255));

        if (text.Length == 0 || text.Contains('/') || text.Contains(' ') || badNumeric
            || !Uri.TryCreate("http://" + text + "/", UriKind.Absolute, out var uri)
            || uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.Dns))
        {
            error = $"'{input!.Trim()}' is not a valid IP address.";
            return null;
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    public static string ToBaseUri(string host) => "http://" + host + "/";

    /// <summary>
    /// False for a MAC that cannot identify a reader: empty, all zeros (some firmware builds read
    /// it before Wi-Fi has started) or broadcast. Such a reader is identified by its address.
    /// </summary>
    public static bool IsUsableMac(string? mac)
    {
        var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 && hex.Any(c => c != '0') && !hex.Equals("FFFFFFFFFFFF", StringComparison.OrdinalIgnoreCase);
    }
}
