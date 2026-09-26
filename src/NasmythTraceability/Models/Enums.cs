namespace NasmythTraceability.Models;

/// <summary>Final quality result stored against a trace event.</summary>
public enum ScanResult
{
    OK,
    NG
}

/// <summary>Outcome of pushing a raw scan through the processing pipeline.</summary>
public enum ScanOutcome
{
    /// <summary>Recorded in trace_history as OK.</summary>
    Accepted,

    /// <summary>Recorded in trace_history as NG (rejected by a business rule).</summary>
    Rejected,

    /// <summary>Ignored as a repeat of a very recent scan (scan_logs only).</summary>
    Duplicate,

    /// <summary>Could not be processed - unmapped device, bad data (scan_logs only).</summary>
    Error
}

/// <summary>Category for a row in scan_logs.</summary>
public enum ScanLogType
{
    Raw,
    Duplicate,
    Rejected,
    Error,
    Info
}

/// <summary>Lifecycle of a barcode as tracked by current_trace.</summary>
public enum TraceStatus
{
    InProgress,
    Completed,
    Rejected
}
