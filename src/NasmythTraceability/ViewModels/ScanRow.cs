using NasmythTraceability.Models;
using NasmythTraceability.Services;

namespace NasmythTraceability.ViewModels;

/// <summary>Flat row for the live scan log and recent-scans lists.</summary>
public sealed class ScanRow
{
    /// <summary>trace_history id when this row came from history; 0 otherwise.</summary>
    public long Id { get; init; }

    public required DateTime Time { get; init; }
    public required string Station { get; init; }
    public required string Barcode { get; init; }
    public required string Result { get; init; }
    public required string Message { get; init; }

    public string TimeText => Time.ToString("HH:mm:ss");
    public string DateTimeText => Time.ToString("dd/MM/yyyy HH:mm:ss");

    public static ScanRow From(ScanProcessedEventArgs e)
    {
        var result = e.Outcome switch
        {
            ScanOutcome.Accepted => "OK",
            ScanOutcome.Rejected => "NG",
            ScanOutcome.Duplicate => "DUP",
            _ => "ERR",
        };

        return new ScanRow
        {
            Id = e.Trace?.Id ?? 0,
            Time = e.Timestamp,
            Station = e.StationCode,
            Barcode = e.Barcode,
            Result = result,
            Message = e.Message,
        };
    }

    public static ScanRow From(TraceHistory h) => new()
    {
        Id = h.Id,
        Time = h.ScannedAt,
        Station = h.StationCode,
        Barcode = h.Barcode,
        Result = h.Result.ToString(),
        Message = h.Message,
    };
}
