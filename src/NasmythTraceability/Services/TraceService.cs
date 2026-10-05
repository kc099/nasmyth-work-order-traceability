using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

/// <summary>Payload for <see cref="TraceService.Changed"/>.</summary>
public sealed record TraceChangedEventArgs(bool Cleared);

/// <summary>Writes trace events and current position; serves history and barcode search.</summary>
public sealed class TraceService
{
    private readonly DatabaseService _db;

    public TraceService(DatabaseService db) => _db = db;

    /// <summary>
    /// Raised after trace data is changed <b>outside</b> the normal scan flow
    /// (a delete, clear or purge). <c>Cleared</c> is true for a bulk wipe.
    /// The live scan path does not raise this - views listen to ScanProcessed for that.
    /// </summary>
    public event EventHandler<TraceChangedEventArgs>? Changed;

    private void RaiseChanged(bool cleared) => Changed?.Invoke(this, new TraceChangedEventArgs(cleared));

    // ---------------------------------------------------------------- write

    /// <summary>
    /// Records one trace event in <c>trace_history</c> and upserts <c>current_trace</c>.
    /// An OK scan also logs the exit from the station the work order was at before.
    /// Pass <paramref name="at"/> to back-date the event (used by the demo data generator).
    /// </summary>
    public TraceHistory Record(string barcode, Station station, ScanResult result, string message,
        int? routeId, string deviceKey, bool isFinalStation, DateTime? at = null)
    {
        var now = at ?? DateTime.Now;
        long id = 0;

        _db.InTransaction((cn, tx) =>
        {
            // Arriving here means it has left wherever it was.
            if (result == ScanResult.OK)
            {
                DatabaseService.Exec(cn, tx,
                    "UPDATE trace_history SET exited_at = $t " +
                    "WHERE barcode = $b AND exited_at IS NULL AND station_id <> $sid;",
                    ("$t", Db.ToDb(now)), ("$b", barcode), ("$sid", station.Id));
            }

            DatabaseService.Exec(cn, tx,
                "INSERT INTO trace_history (barcode, station_id, station_code, result, message, route_id, device_key, scanned_at) " +
                "VALUES ($b, $sid, $sc, $r, $m, $rt, $dk, $t);",
                ("$b", barcode), ("$sid", station.Id), ("$sc", station.Code),
                ("$r", result.ToString()), ("$m", message), ("$rt", routeId),
                ("$dk", deviceKey), ("$t", Db.ToDb(now)));

            id = DatabaseService.ScalarLong(cn, tx, "SELECT last_insert_rowid();", 0);

            var status = result == ScanResult.NG
                ? TraceStatus.Rejected
                : isFinalStation ? TraceStatus.Completed : TraceStatus.InProgress;

            DatabaseService.Exec(cn, tx,
                "INSERT INTO current_trace (barcode, station_id, station_code, result, status, route_id, scan_count, first_scan_at, last_scan_at) " +
                "VALUES ($b, $sid, $sc, $r, $st, $rt, 1, $t, $t) " +
                "ON CONFLICT(barcode) DO UPDATE SET " +
                "  station_id = excluded.station_id, station_code = excluded.station_code, result = excluded.result, " +
                "  status = excluded.status, route_id = COALESCE(excluded.route_id, current_trace.route_id), " +
                "  scan_count = current_trace.scan_count + 1, last_scan_at = excluded.last_scan_at;",
                ("$b", barcode), ("$sid", station.Id), ("$sc", station.Code),
                ("$r", result.ToString()), ("$st", status.ToString()), ("$rt", routeId),
                ("$t", Db.ToDb(now)));
        });

        return new TraceHistory
        {
            Id = id,
            Barcode = barcode,
            StationId = station.Id,
            StationCode = station.Code,
            Result = result,
            Message = message,
            RouteId = routeId,
            DeviceKey = deviceKey,
            ScannedAt = now,
        };
    }

    /// <summary>
    /// Writes one scan_logs line. For a scan at a station pass <paramref name="outcome"/> (what it
    /// came to) and the tag; lines without an outcome (reader status, exits) are not counted as scans.
    /// </summary>
    public void LogScan(string rawData, int? stationId, string stationCode, string deviceKey,
        string deviceName, ScanLogType type, string message, DateTime? at = null,
        ScanOutcome? outcome = null, string tagId = "")
    {
        _db.Execute(
            "INSERT INTO scan_logs (raw_data, station_id, station_code, device_key, device_name, log_type, message, " +
            "  outcome, tag_id, created_at) " +
            "VALUES ($raw, $sid, $sc, $dk, $dn, $lt, $m, $o, $tag, $t);",
            ("$raw", rawData), ("$sid", stationId), ("$sc", stationCode), ("$dk", deviceKey),
            ("$dn", deviceName), ("$lt", type.ToString()), ("$m", message), ("$o", outcome?.ToString()),
            ("$tag", tagId), ("$t", Db.ToDb(at ?? DateTime.Now)));
    }

    /// <summary>
    /// Station scans that were not recorded as a station visit - invalid ones (unassigned tag,
    /// out of sequence, blocked, already completed) and repeats - newest first. Filter by
    /// station code, part of a work order or tag, and kind ("Invalid" / "Repeat"; null = both).
    /// </summary>
    public List<ScanLog> GetScanExceptions(string? stationCode = null, string? search = null,
        string? kind = null, int limit = 500)
    {
        var like = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
        var outcomes = kind switch
        {
            "Invalid" => "('Rejected','Error')",
            "Repeat" => "('Duplicate')",
            _ => "('Rejected','Error','Duplicate')",
        };
        return _db.Query(
            "SELECT * FROM scan_logs WHERE outcome IN " + outcomes +
            "  AND ($sc IS NULL OR station_code = $sc) " +
            "  AND ($q IS NULL OR raw_data LIKE $q OR tag_id LIKE $q) " +
            "ORDER BY created_at DESC, id DESC LIMIT $n;",
            MapLog,
            ("$sc", string.IsNullOrWhiteSpace(stationCode) ? null : stationCode), ("$q", like), ("$n", limit));
    }

    // ---------------------------------------------------------------- read

    public CurrentTrace? GetCurrent(string barcode)
        => _db.QuerySingle("SELECT * FROM current_trace WHERE barcode = $b;", MapCurrent, ("$b", barcode));

    public List<TraceHistory> GetHistory(string barcode)
        => _db.Query("SELECT * FROM trace_history WHERE barcode = $b ORDER BY scanned_at, id;",
            MapHistory, ("$b", barcode));

    public List<TraceHistory> GetRecent(int count = 100)
        => _db.Query("SELECT * FROM trace_history ORDER BY scanned_at DESC, id DESC LIMIT $n;",
            MapHistory, ("$n", count));

    public List<ScanLog> GetScanLogs(int count = 200, ScanLogType? type = null)
    {
        var sql = "SELECT * FROM scan_logs" +
                  (type is null ? "" : " WHERE log_type = $t") +
                  " ORDER BY created_at DESC, id DESC LIMIT $n;";
        return type is null
            ? _db.Query(sql, MapLog, ("$n", count))
            : _db.Query(sql, MapLog, ("$t", type.ToString()), ("$n", count));
    }

    /// <summary>Deletes one trace_history row and repairs current_trace for its barcode.</summary>
    public void DeleteTrace(long id)
    {
        _db.InTransaction((cn, tx) =>
        {
            var barcode = DatabaseService.ScalarString(cn, tx,
                "SELECT barcode FROM trace_history WHERE id = $id;", ("$id", id));
            DatabaseService.Exec(cn, tx, "DELETE FROM trace_history WHERE id = $id;", ("$id", id));
            if (!string.IsNullOrEmpty(barcode))
                RepairCurrentTrace(cn, tx, barcode);
        });
        RaiseChanged(cleared: false);
    }

    /// <summary>
    /// Deletes the most recent scan of a work order, putting it back at the station before.
    /// Returns false when the work order has no scans.
    /// </summary>
    public bool DeleteLastScan(string barcode)
    {
        var last = _db.ScalarOrDefault<long>(
            "SELECT id FROM trace_history WHERE barcode = $b ORDER BY scanned_at DESC, id DESC LIMIT 1;",
            ("$b", barcode));
        if (last == 0)
            return false;

        DeleteTrace(last);
        return true;
    }

    /// <summary>Deletes every scan of a work order so it can be scanned again from the start. Returns rows removed.</summary>
    public int DeleteWorkOrder(string barcode)
    {
        var removed = 0;
        _db.InTransaction((cn, tx) =>
        {
            removed = DatabaseService.Exec(cn, tx, "DELETE FROM trace_history WHERE barcode = $b;", ("$b", barcode));
            DatabaseService.Exec(cn, tx, "DELETE FROM current_trace WHERE barcode = $b;", ("$b", barcode));
        });
        RaiseChanged(cleared: false);
        return removed;
    }

    /// <summary>Wipes all traceability data (history, current position and raw logs). Keeps stations/settings.</summary>
    public void PurgeAll()
    {
        _db.InTransaction((cn, tx) =>
        {
            DatabaseService.Exec(cn, tx, "DELETE FROM scan_logs;");
            DatabaseService.Exec(cn, tx, "DELETE FROM trace_history;");
            DatabaseService.Exec(cn, tx, "DELETE FROM current_trace;");
        });
        RaiseChanged(cleared: true);
    }

    /// <summary>Rebuilds every <c>current_trace</c> row from the latest history row per barcode.</summary>
    public int RebuildAllCurrentTraces()
    {
        var n = 0;
        _db.InTransaction((cn, tx) =>
        {
            DatabaseService.Exec(cn, tx, "DELETE FROM current_trace;");
            n = DatabaseService.Exec(cn, tx,
                "INSERT INTO current_trace (barcode, station_id, station_code, result, status, route_id, scan_count, first_scan_at, last_scan_at) " +
                "SELECT h.barcode, h.station_id, h.station_code, h.result, " +
                "  CASE WHEN h.result = 'NG' THEN 'Rejected' " +
                "       WHEN EXISTS (SELECT 1 FROM stations s WHERE s.id = h.station_id AND s.is_final = 1) THEN 'Completed' " +
                "       ELSE 'InProgress' END, " +
                "  h.route_id, (SELECT COUNT(*) FROM trace_history t2 WHERE t2.barcode = h.barcode), " +
                "  (SELECT MIN(scanned_at) FROM trace_history t3 WHERE t3.barcode = h.barcode), h.scanned_at " +
                "FROM trace_history h " +
                "WHERE h.id = (SELECT id FROM trace_history t WHERE t.barcode = h.barcode " +
                "              ORDER BY scanned_at DESC, id DESC LIMIT 1);");
        });
        RaiseChanged(cleared: false);
        return n;
    }

    private static void RepairCurrentTrace(Microsoft.Data.Sqlite.SqliteConnection cn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, string barcode)
    {
        // The latest surviving visit is where the work order is again, so it has not left it.
        DatabaseService.Exec(cn, tx,
            "UPDATE trace_history SET exited_at = NULL WHERE id = (" +
            "  SELECT id FROM trace_history WHERE barcode = $b ORDER BY scanned_at DESC, id DESC LIMIT 1);",
            ("$b", barcode));

        // Rebuild the current_trace row from the latest surviving history row, or drop it.
        DatabaseService.Exec(cn, tx, "DELETE FROM current_trace WHERE barcode = $b;", ("$b", barcode));
        DatabaseService.Exec(cn, tx,
            "INSERT INTO current_trace (barcode, station_id, station_code, result, status, route_id, scan_count, first_scan_at, last_scan_at) " +
            "SELECT h.barcode, h.station_id, h.station_code, h.result, " +
            "  CASE WHEN h.result = 'NG' THEN 'Rejected' " +
            "       WHEN EXISTS (SELECT 1 FROM stations s WHERE s.id = h.station_id AND s.is_final = 1) THEN 'Completed' " +
            "       ELSE 'InProgress' END, " +
            "  h.route_id, (SELECT COUNT(*) FROM trace_history t2 WHERE t2.barcode = h.barcode), " +
            "  (SELECT MIN(scanned_at) FROM trace_history t3 WHERE t3.barcode = h.barcode), h.scanned_at " +
            "FROM trace_history h " +
            "WHERE h.barcode = $b " +
            "ORDER BY h.scanned_at DESC, h.id DESC LIMIT 1;",
            ("$b", barcode));
    }

    private static ScanLog MapLog(IDataRecord r) => new()
    {
        Id = r.GetLong("id"),
        RawData = r.GetString("raw_data"),
        StationId = r.GetIntOrNull("station_id"),
        StationCode = r.GetString("station_code"),
        DeviceKey = r.GetString("device_key"),
        DeviceName = r.GetString("device_name"),
        LogType = Enum.TryParse<ScanLogType>(r.GetString("log_type"), out var t) ? t : ScanLogType.Info,
        Message = r.GetString("message"),
        CreatedAt = r.GetDate("created_at"),
        Outcome = Enum.TryParse<ScanOutcome>(r.GetString("outcome"), out var o) ? o : null,
        TagId = r.GetString("tag_id"),
    };

    /// <summary>
    /// Work orders for the search-as-you-type list: every one that has been scanned or has a
    /// tag assigned, matched on part of its number or of its tag id, most recent first.
    /// </summary>
    public List<WorkOrderSuggestion> SearchWorkOrders(string term, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(term))
            return new List<WorkOrderSuggestion>();

        return _db.Query(
            "SELECT w.work_order, a.tag_id, c.station_code, c.status " +
            "FROM (SELECT barcode AS work_order FROM current_trace " +
            "      UNION SELECT work_order FROM tag_assignments) w " +
            "LEFT JOIN current_trace c ON c.barcode = w.work_order " +
            "LEFT JOIN tag_assignments a ON a.work_order = w.work_order " +
            "WHERE w.work_order LIKE $q OR a.tag_id LIKE $q " +
            "ORDER BY COALESCE(c.last_scan_at, a.assigned_at) DESC, w.work_order LIMIT $n;",
            r => new WorkOrderSuggestion
            {
                WorkOrder = r.GetString("work_order"),
                TagId = r.GetString("tag_id"),
                StationCode = r.GetString("station_code"),
                Status = Enum.TryParse<TraceStatus>(r.GetString("status"), out var st) ? st : null,
            },
            ("$q", "%" + term.Trim() + "%"), ("$n", limit));
    }

    /// <summary>
    /// Every barcode's current station - the core "what is where right now" view.
    /// Optionally filtered by station code and/or a barcode search term.
    /// </summary>
    public List<CurrentTrace> GetCurrentPositions(string? stationCode = null, string? search = null, int limit = 1000)
    {
        var like = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
        return _db.Query(
            "SELECT * FROM current_trace " +
            "WHERE ($sc IS NULL OR station_code = $sc) " +
            "  AND ($q IS NULL OR barcode LIKE $q) " +
            "ORDER BY last_scan_at DESC LIMIT $n;",
            MapCurrent,
            ("$sc", string.IsNullOrWhiteSpace(stationCode) ? null : stationCode),
            ("$q", like), ("$n", limit));
    }

    // ---------------------------------------------------------------- mapping

    private static TraceHistory MapHistory(IDataRecord r) => new()
    {
        Id = r.GetLong("id"),
        Barcode = r.GetString("barcode"),
        StationId = r.GetInt("station_id"),
        StationCode = r.GetString("station_code"),
        Result = ParseResult(r.GetString("result")),
        Message = r.GetString("message"),
        RouteId = r.GetIntOrNull("route_id"),
        DeviceKey = r.GetString("device_key"),
        ScannedAt = r.GetDate("scanned_at"),
        ExitedAt = r.GetDateOrNull("exited_at"),
    };

    private static CurrentTrace MapCurrent(IDataRecord r) => new()
    {
        Barcode = r.GetString("barcode"),
        StationId = r.GetInt("station_id"),
        StationCode = r.GetString("station_code"),
        Result = ParseResult(r.GetString("result")),
        Status = Enum.TryParse<TraceStatus>(r.GetString("status"), out var st) ? st : TraceStatus.InProgress,
        RouteId = r.GetIntOrNull("route_id"),
        ScanCount = r.GetInt("scan_count"),
        FirstScanAt = r.GetDate("first_scan_at"),
        LastScanAt = r.GetDate("last_scan_at"),
    };

    private static ScanResult ParseResult(string s)
        => string.Equals(s, "NG", StringComparison.OrdinalIgnoreCase) ? ScanResult.NG : ScanResult.OK;
}
