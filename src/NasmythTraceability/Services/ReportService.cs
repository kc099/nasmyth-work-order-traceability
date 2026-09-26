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

    public ReportSummary GetSummary(DateTime from, DateTime to)
    {
        var (f, t) = Bounds(from, to);
        return _db.QuerySingle(
            "SELECT " +
            "  COUNT(*) AS total, " +
            "  SUM(CASE WHEN result = 'OK' THEN 1 ELSE 0 END) AS ok, " +
            "  SUM(CASE WHEN result = 'NG' THEN 1 ELSE 0 END) AS ng, " +
            "  COUNT(DISTINCT barcode) AS uniq " +
            "FROM trace_history WHERE scanned_at >= $f AND scanned_at < $t;",
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

    public List<StationCount> GetByStation(DateTime from, DateTime to)
    {
        var (f, t) = Bounds(from, to);

        // Left join so stations with zero scans still appear (Scans by Station chart).
        return _db.Query(
            "SELECT s.id AS sid, s.code AS code, s.name AS name, " +
            "  SUM(CASE WHEN h.result = 'OK' THEN 1 ELSE 0 END) AS ok, " +
            "  SUM(CASE WHEN h.result = 'NG' THEN 1 ELSE 0 END) AS ng " +
            "FROM stations s " +
            "LEFT JOIN trace_history h ON h.station_id = s.id AND h.scanned_at >= $f AND h.scanned_at < $t " +
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

    /// <summary>Time trend: hourly buckets for ranges up to 2 days, otherwise daily.</summary>
    public List<TrendPoint> GetTrend(DateTime from, DateTime to)
    {
        var (f, t) = Bounds(from, to);
        var span = to.Date - from.Date;
        var hourly = span.TotalDays <= 2;

        var groupExpr = hourly
            ? "substr(scanned_at, 1, 13)"   // yyyy-MM-dd HH
            : "substr(scanned_at, 1, 10)";  // yyyy-MM-dd

        var rows = _db.Query(
            $"SELECT {groupExpr} AS bucket, " +
            "  COUNT(*) AS total, " +
            "  SUM(CASE WHEN result = 'OK' THEN 1 ELSE 0 END) AS ok, " +
            "  SUM(CASE WHEN result = 'NG' THEN 1 ELSE 0 END) AS ng " +
            "FROM trace_history WHERE scanned_at >= $f AND scanned_at < $t " +
            "GROUP BY bucket ORDER BY bucket;",
            r => (Bucket: r.GetString("bucket"), Total: r.GetInt("total"),
                  Ok: r.GetInt("ok"), Ng: r.GetInt("ng")),
            ("$f", f), ("$t", t));

        var list = new List<TrendPoint>();
        foreach (var row in rows)
        {
            DateTime bucket;
            string label;
            if (hourly)
            {
                bucket = DateTime.TryParseExact(row.Bucket + ":00:00.000", Db.DateFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var b) ? b : default;
                label = bucket == default ? row.Bucket : bucket.ToString("dd/MM HH:00");
            }
            else
            {
                bucket = DateTime.TryParse(row.Bucket, out var b) ? b : default;
                label = bucket == default ? row.Bucket : bucket.ToString("dd/MM");
            }

            list.Add(new TrendPoint
            {
                Bucket = bucket,
                Label = label,
                Total = row.Total,
                Ok = row.Ok,
                Ng = row.Ng,
            });
        }

        return list;
    }

    public List<TraceHistory> GetScans(DateTime from, DateTime to, int limit = 500)
    {
        var (f, t) = Bounds(from, to);
        return _db.Query(
            "SELECT * FROM trace_history WHERE scanned_at >= $f AND scanned_at < $t " +
            "ORDER BY scanned_at DESC, id DESC LIMIT $n;",
            MapHistory, ("$f", f), ("$t", t), ("$n", limit));
    }

    public ReportBundle BuildBundle(DateTime from, DateTime to)
    {
        return new ReportBundle
        {
            CompanyName = _settings.Get(SettingsService.CompanyName, "Nasmyth Asia (IN) Pvt Ltd."),
            Title = "Work Order Traceability Report",
            GeneratedAt = DateTime.Now,
            Summary = GetSummary(from, to),
            ByStation = GetByStation(from, to),
            Trend = GetTrend(from, to),
            Scans = GetScans(from, to, 5000),
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
    };
}
