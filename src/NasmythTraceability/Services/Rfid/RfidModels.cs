using System.Text.Json.Serialization;

namespace NasmythTraceability.Services.Rfid;

// ---------------------------------------------------------------- reader API (JSON)
// Shapes of the ESP32 + RC522 reader's HTTP API, see README.md section 3.

/// <summary>GET /api/status, and the reply to POST /api/mode.</summary>
public sealed class RfidStatusDto
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("data")] public string Data { get; set; } = "";
    [JsonPropertyName("once")] public bool Once { get; set; }
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("firmware")] public string Firmware { get; set; } = "";
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("bootId")] public long BootId { get; set; }
    [JsonPropertyName("readerOk")] public bool ReaderOk { get; set; } = true;
    [JsonPropertyName("uptimeMs")] public long UptimeMs { get; set; }
    [JsonPropertyName("lastEventId")] public long LastEventId { get; set; }
}

/// <summary>GET /api/events?since=ID.</summary>
public sealed class RfidEventsDto
{
    [JsonPropertyName("device")] public string Device { get; set; } = "";
    [JsonPropertyName("bootId")] public long BootId { get; set; }
    [JsonPropertyName("uptimeMs")] public long UptimeMs { get; set; }
    [JsonPropertyName("lastEventId")] public long LastEventId { get; set; }
    [JsonPropertyName("events")] public List<RfidEventDto> Events { get; set; } = new();
}

/// <summary>One card presented to a reader.</summary>
public sealed class RfidEventDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("ms")] public long Ms { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("uid")] public string Uid { get; set; } = "";
    [JsonPropertyName("cardType")] public string CardType { get; set; } = "";
    [JsonPropertyName("data")] public string Data { get; set; } = "";
    [JsonPropertyName("hex")] public string Hex { get; set; } = "";
    [JsonPropertyName("error")] public string Error { get; set; } = "";
}

/// <summary>Body of POST /api/mode. Null fields are left out of the request.</summary>
public sealed class RfidModeRequest
{
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }

    [JsonPropertyName("once")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Once { get; set; }

    public static RfidModeRequest Read() => new() { Mode = "read" };

    public static RfidModeRequest WriteOnce(string data) => new() { Mode = "write", Data = data, Once = true };
}

// ---------------------------------------------------------------- app side

/// <summary>What a reader is used for. A reader has exactly one role.</summary>
public enum ReaderRole
{
    /// <summary>Production station: always in read mode, its reads are tracked.</summary>
    Station,

    /// <summary>Work order assigning station: writes work orders to tags, never tracked.</summary>
    Assignment,
}

public enum ReaderState
{
    NotConfigured,
    Connecting,
    Online,
    Offline,
}

/// <summary>One reader the app polls: where it is and what it is for.</summary>
public sealed record ReaderEndpoint(string Key, ReaderRole Role, int? StationId, string StationCode, string Host)
{
    public const string AssignmentKey = "assignment";

    public static string StationKey(int stationId) => "station:" + stationId;
}

/// <summary>A card event from a reader, with the reader it came from and a PC timestamp.</summary>
public sealed class ReaderCardEvent
{
    public required ReaderEndpoint Endpoint { get; init; }
    public required long Id { get; init; }

    /// <summary>"read" or "write" - the mode the reader was in.</summary>
    public required string Type { get; init; }

    public bool Ok { get; init; }

    /// <summary>Card serial number (upper-case hex). The trustworthy identifier of a tag.</summary>
    public string Uid { get; init; } = "";

    public string CardType { get; init; } = "";

    /// <summary>Text stored on the card (for a write, what was read back).</summary>
    public string Data { get; init; } = "";

    public string Error { get; init; } = "";

    public string Device { get; init; } = "";
    /// <summary>Reader MAC; empty when the firmware reports none.</summary>
    public string Mac { get; init; } = "";

    /// <summary>Catch-up cursor key of the reader: its MAC, or its address.</summary>
    public string ReaderKey { get; init; } = "";

    /// <summary>When the card was presented, worked out from the reader's uptime.</summary>
    public DateTime Timestamp { get; init; }

    public bool IsWrite => string.Equals(Type, "write", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Snapshot of one reader's connection, for status displays.</summary>
public sealed class ReaderStatusInfo
{
    public required ReaderEndpoint Endpoint { get; init; }
    public ReaderState State { get; init; }
    public string Device { get; init; } = "";
    public string Mac { get; init; } = "";
    public string Firmware { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool ReaderOk { get; init; } = true;
    public string LastError { get; init; } = "";
    public DateTime? LastSeen { get; init; }

    public string StateText => State switch
    {
        ReaderState.Online when !ReaderOk => "Online - RC522 module not detected",
        ReaderState.Online => string.IsNullOrEmpty(Device) ? "Online" : $"Online - {Device}",
        ReaderState.Offline => string.IsNullOrEmpty(LastError) ? "Offline" : $"Offline - {LastError}",
        ReaderState.Connecting => "Connecting...",
        _ => "No reader IP",
    };

    /// <summary>"ONLINE" / "OFFLINE" / "WARN" / "" - keys for the status brush converter.</summary>
    public string StateKey => State switch
    {
        ReaderState.Online when !ReaderOk => "WARN",
        ReaderState.Online => "ONLINE",
        ReaderState.Offline => "OFFLINE",
        ReaderState.Connecting => "WARN",
        _ => "",
    };
}

/// <summary>A reader at a known address answered with a different MAC than the one on record.</summary>
public sealed record ReaderIdentityEventArgs(ReaderEndpoint Endpoint, string Mac, string Device, string PreviousMac);

/// <summary>A reader went online or offline.</summary>
public sealed record ReaderConnectionEventArgs(ReaderEndpoint Endpoint, bool Online, string Detail);
