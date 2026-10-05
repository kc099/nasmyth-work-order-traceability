using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

/// <summary>KPIs, chart series and export bundles for the Reports &amp; Analytics screen.</summary>
public sealed class ReportService
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;

    public ReportService(DatabaseService db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private static (string From, string To) Bounds(DateTime from, DateTime to)
        => (Db.ToDb(from.Date), Db.ToDb(to.Date.AddDays(1)));

    /// <param name="validOnly">Count only valid (OK) station visits, as in the exported report.</param>
    public ReportSummary GetSummary(DateTime from, DateTime to, bool validOnly = false)
    {
        var (f, t) = Bounds(from, to);
        return _db.QuerySingle(
            "SELECT " +
            "  COUNT(*) AS total, " +
            "  SUM(CASE WHEN result = 'OK' THEN 1 ELSE 0 END) AS ok, " +
            "  SUM(CASE WHEN result = 'NG' THEN 1 ELSE 0 END) AS ng, " +
            "  COUNT(DISTINCT barcode) AS uniq " +
            "FROM trace_history WHERE scanned_at >= $f AND scanned_at < $t" + (validOnly ? " AND result = 'OK';" : ";"),
            r => new ReportSummary
            {
                From = from.Date,
                To = to.Date,
                TotalScans = r.GetInt("total"),
                OkScans = r.GetInt("ok"),
                NgScans = r.GetInt("ng"),
                UniqueBarcodes = r.GetInt("uniq"),
            },
            ("$f", f), ("$t", t)) ?? new ReportSummary { From = from.Date, To = to.Date };
    }

    public List<StationCount> GetByStation(DateTime from, DateTime to, bool validOnly = false)
    {
        var (f, t) = Bounds(from, to);

        // Left join so stations with zero scans still appear (Scans by Station chart).
        return _db.Query(
            "SELECT s.id AS sid, s.code AS code, s.name AS name, " +
            "  SUM(CASE WHEN h.result = 'OK' THEN 1 ELSE 0 END) AS ok, " +
            "  SUM(CASE WHEN h.result = 'NG' THEN 1 ELSE 0 END) AS ng " +
            "FROM stations s " +
            "LEFT JOIN trace_history h ON h.station_id = s.id AND h.scanned_at >= $f AND h.scanned_at < $t " +
            (validOnly ? "AND h.result = 'OK' " : "") +
            "GROUP BY s.id, s.code, s.name ORDER BY s.sequence, s.code;",
            r => new StationCount
            {
                StationId = r.GetInt("sid"),
                StationCode = r.GetString("code"),
                StationName = r.GetString("name"),
                Ok = r.GetInt("ok"),
                Ng = r.GetInt("ng"),
            },
            ("$f", f), ("$t", t));
    }

    public List<TraceHistory> GetScans(DateTime from, DateTime to, int limit = 500, bool validOnly = false)
    {
        var (f, t) = Bounds(from, to);
        return _db.Query(
            "SELECT * FROM trace_history WHERE scanned_at >= $f AND scanned_at < $t " +
            (validOnly ? "AND result = 'OK' " : "") +
            "ORDER BY scanned_at DESC, id DESC LIMIT $n;",
            MapHistory, ("$f", f), ("$t", t), ("$n", limit));
    }

    /// <summary>
    /// Per station: valid scans (station visits recorded OK - so an administrator's corrections
    /// are reflected), invalid scans and repeat scans (from the scan log). Stations with no scans
    /// are included.
    /// </summary>
    public List<StationScanBreakdown> GetScanBreakdown(DateTime from, DateTime to)
    {
        var (f, t) = Bounds(from, to);
        return _db.Query(
            "SELECT s.id AS sid, s.code AS code, s.name AS name, " +
            "  (SELECT COUNT(*) FROM trace_history h WHERE h.station_id = s.id AND h.result = 'OK' " +
            "     AND h.scanned_at >= $f AND h.scanned_at < $t) AS valid, " +
            "  (SELECT COUNT(*) FROM scan_logs l WHERE l.station_id = s.id AND l.outcome IN ('Rejected','Error') " +
            "     AND l.created_at >= $f AND l.created_at < $t) AS invalid, " +
            "  (SELECT COUNT(*) FROM scan_logs l WHERE l.station_id = s.id AND l.outcome = 'Duplicate' " +
            "     AND l.created_at >= $f AND l.created_at < $t) AS rpt " +
            "FROM stations s ORDER BY s.sequence, s.code;",
            r => new StationScanBreakdown
            {
                StationId = r.GetInt("sid"),
                StationCode = r.GetString("code"),
                StationName = r.GetString("name"),
                Valid = r.GetInt("valid"),
                Invalid = r.GetInt("invalid"),
                Repeat = r.GetInt("rpt"),
            },
            ("$f", f), ("$t", t));
    }

    /// <summary>The exported report: valid station visits only - no invalid or repeat scans.</summary>
    public ReportBundle BuildBundle(DateTime from, DateTime to)
    {
        return new ReportBundle
        {
            CompanyName = _settings.Get(SettingsService.CompanyName, "Nasmyth Asia (IN) Pvt Ltd."),
            Title = "Work Order Traceability Report",
            GeneratedAt = DateTime.Now,
            Summary = GetSummary(from, to, validOnly: true),
            ByStation = GetByStation(from, to, validOnly: true),
            Scans = GetScans(from, to, 5000, validOnly: true),
        };
    }

    private static TraceHistory MapHistory(IDataRecord r) => new()
    {
        Id = r.GetLong("id"),
        Barcode = r.GetString("barcode"),
        StationId = r.GetInt("station_id"),
        StationCode = r.GetString("station_code"),
        Result = string.Equals(r.GetString("result"), "NG", StringComparison.OrdinalIgnoreCase)
            ? ScanResult.NG : ScanResult.OK,
        Message = r.GetString("message"),
        RouteId = r.GetIntOrNull("route_id"),
        DeviceKey = r.GetString("device_key"),
        ScannedAt = r.GetDate("scanned_at"),
        ExitedAt = r.GetDateOrNull("exited_at"),
    };
}
