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

    /// <summary>When the work order left this station, i.e. was scanned at the next one. Null while it is still here.</summary>
    public DateTime? ExitedAt { get; set; }
}

/// <summary>An RFID tag and the work order it currently carries.</summary>
public sealed class TagAssignment
{
    public string TagId { get; set; } = "";

    public string WorkOrder { get; set; } = "";

    public DateTime AssignedAt { get; set; }

    /// <summary>Tracking status of the work order; null when it has not been scanned yet.</summary>
    public TraceStatus? Status { get; set; }

    public string StationCode { get; set; } = "";

    public string StatusText => Status switch
    {
        TraceStatus.Completed => "Completed",
        TraceStatus.Rejected => "Rejected",
        TraceStatus.InProgress => "In progress",
        _ => "Not scanned yet",
    };

    public string AssignedText => AssignedAt.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>One row of the search-as-you-type list: a work order with where it is and the tag it carries.</summary>
public sealed class WorkOrderSuggestion
{
    public string WorkOrder { get; set; } = "";

    /// <summary>Tag assigned to the work order; empty when it has none.</summary>
    public string TagId { get; set; } = "";

    public string StationCode { get; set; } = "";

    /// <summary>Tracking status; null when the work order has a tag but was never scanned.</summary>
    public TraceStatus? Status { get; set; }

    public string StatusText => Status switch
    {
        TraceStatus.Completed => "Completed",
        TraceStatus.Rejected => "Rejected",
        TraceStatus.InProgress => "In progress",
        _ => "Not scanned yet",
    };

    /// <summary>"ST01 - In progress", or just the status when it is not at a station.</summary>
    public string Detail => string.IsNullOrEmpty(StationCode) ? StatusText : $"{StationCode} - {StatusText}";

    public string TagText => string.IsNullOrEmpty(TagId) ? "" : $"Tag {TagId}";
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
