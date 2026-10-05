using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

/// <summary>Stations and the network RFID reader each one is polled through.</summary>
public sealed class StationService
{
    private readonly DatabaseService _db;

    public StationService(DatabaseService db) => _db = db;

    /// <summary>Raised after any station add / update / delete so views can refresh.</summary>
    public event EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- stations

    private const string SelectStations = "SELECT s.* FROM stations s";

    public List<Station> GetStations(bool includeDisabled = true)
    {
        var sql = SelectStations +
                  (includeDisabled ? "" : " WHERE s.is_enabled = 1") +
                  " ORDER BY s.sequence, s.code;";
        return _db.Query(sql, MapStation);
    }

    public Station? GetStation(int id)
        => _db.QuerySingle(SelectStations + " WHERE s.id = $id;", MapStation, ("$id", id));

    public Station? GetStationByCode(string code)
        => _db.QuerySingle(SelectStations + " WHERE s.code = $c;", MapStation, ("$c", code));

    public Station? GetFinalStation()
        => _db.QuerySingle(SelectStations + " WHERE s.is_final = 1 ORDER BY s.sequence LIMIT 1;", MapStation);

    public int AddStation(Station s)
    {
        var id = (int)_db.ExecuteReturningId(
            "INSERT INTO stations (code, name, sequence, is_enabled, is_final, reader_ip, created_at) " +
            "VALUES ($c, $n, $s, $e, $f, $ip, $t);",
            ("$c", s.Code.Trim()), ("$n", s.Name.Trim()), ("$s", s.Sequence),
            ("$e", s.IsEnabled ? 1 : 0), ("$f", s.IsFinal ? 1 : 0), ("$ip", s.ReaderIp.Trim()),
            ("$t", Db.ToDb(DateTime.Now)));
        RaiseChanged();
        return id;
    }

    /// <summary>Saves a station. A new reader address forgets the identity (MAC) of the old reader.</summary>
    public void UpdateStation(Station s)
    {
        _db.Execute(
            "UPDATE stations SET code = $c, name = $n, sequence = $s, is_enabled = $e, is_final = $f, " +
            "  reader_mac = CASE WHEN reader_ip = $ip THEN reader_mac ELSE '' END, reader_ip = $ip " +
            "WHERE id = $id;",
            ("$c", s.Code.Trim()), ("$n", s.Name.Trim()), ("$s", s.Sequence),
            ("$e", s.IsEnabled ? 1 : 0), ("$f", s.IsFinal ? 1 : 0), ("$ip", s.ReaderIp.Trim()), ("$id", s.Id));
        RaiseChanged();
    }

    /// <summary>
    /// Records the MAC the station's reader reported. Not a configuration change, so
    /// <see cref="Changed"/> is not raised (the readers keep polling undisturbed).
    /// </summary>
    public void SetReaderMac(int stationId, string mac)
        => _db.Execute("UPDATE stations SET reader_mac = $m WHERE id = $id;", ("$m", mac), ("$id", stationId));

    /// <summary>Number of trace_history + current_trace rows that reference this station.</summary>
    public int CountStationTraceRefs(int id)
        => _db.ScalarInt("SELECT (SELECT COUNT(*) FROM trace_history WHERE station_id = $id) " +
                         "     + (SELECT COUNT(*) FROM current_trace WHERE station_id = $id);", ("$id", id));

    /// <summary>
    /// Deletes a station. Route-step rows cascade automatically; trace data does not,
    /// so pass <paramref name="cascadeTraceData"/> to also remove its history / current / log rows.
    /// </summary>
    public void DeleteStation(int id, bool cascadeTraceData = false)
    {
        _db.InTransaction((cn, tx) =>
        {
            if (cascadeTraceData)
            {
                DatabaseService.Exec(cn, tx, "DELETE FROM trace_history WHERE station_id = $id;", ("$id", id));
                DatabaseService.Exec(cn, tx, "DELETE FROM current_trace WHERE station_id = $id;", ("$id", id));
                DatabaseService.Exec(cn, tx, "DELETE FROM scan_logs WHERE station_id = $id;", ("$id", id));
            }

            DatabaseService.Exec(cn, tx, "DELETE FROM station_devices WHERE station_id = $id;", ("$id", id));
            DatabaseService.Exec(cn, tx, "DELETE FROM stations WHERE id = $id;", ("$id", id));
        });
        RaiseChanged();
    }

    /// <summary>Makes exactly one station the final station.</summary>
    public void SetFinalStation(int stationId)
    {
        _db.InTransaction((cn, tx) =>
        {
            DatabaseService.Exec(cn, tx, "UPDATE stations SET is_final = 0;");
            DatabaseService.Exec(cn, tx, "UPDATE stations SET is_final = 1 WHERE id = $id;", ("$id", stationId));
        });
        RaiseChanged();
    }

    // ---------------------------------------------------------------- mapping

    private static Station MapStation(IDataRecord r) => new()
    {
        Id = r.GetInt("id"),
        Code = r.GetString("code"),
        Name = r.GetString("name"),
        Sequence = r.GetInt("sequence"),
        IsEnabled = r.GetBool("is_enabled"),
        IsFinal = r.GetBool("is_final"),
        CreatedAt = r.GetDate("created_at"),
        ReaderIp = r.GetString("reader_ip"),
        ReaderMac = r.GetString("reader_mac"),
    };
}
