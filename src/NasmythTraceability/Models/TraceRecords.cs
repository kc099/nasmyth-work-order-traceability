namespace NasmythTraceability.Models;

/// <summary>One accepted/rejected trace event for a barcode at a station (complete history).</summary>
public sealed class TraceHistory
{
    public long Id { get; set; }

    public string Barcode { get; set; } = "";

    public int StationId { get; set; }

    public string StationCode { get; set; } = "";

    public ScanResult Result { get; set; }

    public string Message { get; set; } = "";

    public int? RouteId { get; set; }

    public string DeviceKey { get; set; } = "";

    public DateTime ScannedAt { get; set; }
}

/// <summary>Latest known position of a barcode (one row per barcode).</summary>
public sealed class CurrentTrace
{
    public string Barcode { get; set; } = "";

    public int StationId { get; set; }

    public string StationCode { get; set; } = "";

    public ScanResult Result { get; set; }

    public TraceStatus Status { get; set; }

    public int? RouteId { get; set; }

    public int ScanCount { get; set; }

    public DateTime FirstScanAt { get; set; }

    public DateTime LastScanAt { get; set; }

    // --- display helpers (for the "current positions" grid) ---
    public string StatusText => Status switch
    {
        TraceStatus.Completed => "Completed",
        TraceStatus.Rejected => "Rejected",
        _ => "In progress",
    };

    public string ResultText => Result.ToString();
    public string FirstScanText => FirstScanAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string LastScanText => LastScanAt.ToString("dd/MM/yyyy HH:mm:ss");
}

/// <summary>Raw / duplicate / rejected / error scan capture, independent of trace_history.</summary>
public sealed class ScanLog
{
    public long Id { get; set; }

    public string RawData { get; set; } = "";

    public int? StationId { get; set; }

    public string StationCode { get; set; } = "";

    public string DeviceKey { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public ScanLogType LogType { get; set; }

    public string Message { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}
