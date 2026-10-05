namespace NasmythTraceability.Models;

/// <summary>Headline KPIs for a date range.</summary>
public sealed class ReportSummary
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalScans { get; set; }
    public int OkScans { get; set; }
    public int NgScans { get; set; }
    public int UniqueBarcodes { get; set; }

    public double OkRate => TotalScans == 0 ? 0 : (double)OkScans / TotalScans;
}

/// <summary>Aggregated scan count for one station.</summary>
public sealed class StationCount
{
    public int StationId { get; set; }
    public string StationCode { get; set; } = "";
    public string StationName { get; set; } = "";
    public int Ok { get; set; }
    public int Ng { get; set; }
    public int Total => Ok + Ng;
}

/// <summary>
/// Scans at one station split by what they came to (Reports chart): valid (recorded as a
/// station visit), invalid (unassigned tag, out of sequence, blocked...) and repeat valid scans.
/// </summary>
public sealed class StationScanBreakdown
{
    public int StationId { get; set; }
    public string StationCode { get; set; } = "";
    public string StationName { get; set; } = "";
    public int Valid { get; set; }
    public int Invalid { get; set; }
    public int Repeat { get; set; }
    public int Total => Valid + Invalid + Repeat;
}

/// <summary>Everything a report export needs, gathered once.</summary>
public sealed class ReportBundle
{
    /// <summary>Printed on every exported report.</summary>
    public const string ScopeNote = "Valid scans only. Invalid and repeat scans are not included.";

    public string CompanyName { get; set; } = "Nasmyth Asia (IN) Pvt Ltd.";
    public string Title { get; set; } = "Work Order Traceability Report";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public ReportSummary Summary { get; set; } = new();
    public IReadOnlyList<StationCount> ByStation { get; set; } = Array.Empty<StationCount>();
    public IReadOnlyList<TraceHistory> Scans { get; set; } = Array.Empty<TraceHistory>();
}

/// <summary>Generic (label, value) pair for the lightweight chart controls.</summary>
public sealed class SeriesPoint
{
    public SeriesPoint() { }
    public SeriesPoint(string label, double value) { Label = label; Value = value; }

    public string Label { get; set; } = "";
    public double Value { get; set; }
}
