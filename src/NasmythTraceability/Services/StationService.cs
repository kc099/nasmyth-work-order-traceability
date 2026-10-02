using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

/// <summary>Stations and the USB-reader-to-station mapping.</summary>
public sealed class StationService
{
    private readonly DatabaseService _db;

    public StationService(DatabaseService db) => _db = db;

    /// <summary>Raised after any station or device add / update / delete so views can refresh.</summary>
    public event EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- stations

    public List<Station> GetStations(bool includeDisabled = true)
    {
        var sql = "SELECT * FROM stations" +
                  (includeDisabled ? "" : " WHERE is_enabled = 1") +
                  " ORDER BY sequence, code;";
        return _db.Query(sql, MapStation);
    }

    public Station? GetStation(int id)
        => _db.QuerySingle("SELECT * FROM stations WHERE id = $id;", MapStation, ("$id", id));

    public Station? GetStationByCode(string code)
        => _db.QuerySingle("SELECT * FROM stations WHERE code = $c;", MapStation, ("$c", code));

    public Station? GetFinalStation()
        => _db.QuerySingle("SELECT * FROM stations WHERE is_final = 1 ORDER BY sequence LIMIT 1;", MapStation);

    public int AddStation(Station s)
    {
        var id = (int)_db.ExecuteReturningId(
            "INSERT INTO stations (code, name, sequence, is_enabled, is_final, created_at) " +
            "VALUES ($c, $n, $s, $e, $f, $t);",
            ("$c", s.Code.Trim()), ("$n", s.Name.Trim()), ("$s", s.Sequence),
            ("$e", s.IsEnabled ? 1 : 0), ("$f", s.IsFinal ? 1 : 0), ("$t", Db.ToDb(DateTime.Now)));
        RaiseChanged();
        return id;
    }

    public void UpdateStation(Station s)
    {
        _db.Execute(
            "UPDATE stations SET code = $c, name = $n, sequence = $s, is_enabled = $e, is_final = $f WHERE id = $id;",
            ("$c", s.Code.Trim()), ("$n", s.Name.Trim()), ("$s", s.Sequence),
            ("$e", s.IsEnabled ? 1 : 0), ("$f", s.IsFinal ? 1 : 0), ("$id", s.Id));
        RaiseChanged();
    }

    /// <summary>Number of trace_history + current_trace rows that reference this station.</summary>
    public int CountStationTraceRefs(int id)
        => _db.ScalarInt("SELECT (SELECT COUNT(*) FROM trace_history WHERE station_id = $id) " +
                         "     + (SELECT COUNT(*) FROM current_trace WHERE station_id = $id);", ("$id", id));

    /// <summary>
    /// Deletes a station. Device and route-step rows cascade automatically; trace data does not,
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

    // ---------------------------------------------------------------- devices

    public List<StationDevice> GetDevices()
        => _db.Query(
            "SELECT d.*, s.code AS station_code FROM station_devices d " +
            "JOIN stations s ON s.id = d.station_id ORDER BY s.sequence, d.device_name;",
            MapDevice);

    public StationDevice? GetDeviceByKey(string deviceKey)
        => _db.QuerySingle(
            "SELECT d.*, s.code AS station_code FROM station_devices d " +
            "JOIN stations s ON s.id = d.station_id WHERE d.device_key = $k;",
            MapDevice, ("$k", deviceKey));

    /// <summary>Resolves the station a scanner belongs to, or null if the device is not mapped/enabled.</summary>
    public Station? ResolveStationForDevice(string deviceKey)
    {
        var device = GetDeviceByKey(deviceKey);
        if (device is null || !device.IsEnabled)
            return null;
        var station = GetStation(device.StationId);
        return station is { IsEnabled: true } ? station : null;
    }

    public int MapDeviceToStation(string deviceKey, string deviceName, int stationId)
    {
        var id = (int)_db.ExecuteReturningId(
            "INSERT INTO station_devices (station_id, device_key, device_name, is_enabled, created_at) " +
            "VALUES ($s, $k, $n, 1, $t) " +
            "ON CONFLICT(device_key) DO UPDATE SET station_id = excluded.station_id, device_name = excluded.device_name;",
            ("$s", stationId), ("$k", deviceKey), ("$n", deviceName), ("$t", Db.ToDb(DateTime.Now)));
        RaiseChanged();
        return id;
    }

    /// <summary>
    /// Station a newly detected reader is linked to: the first enabled station that has no
    /// reader yet, otherwise the first enabled station. Null when no station is enabled.
    /// </summary>
    public Station? PickStationForNewReader()
    {
        var stations = GetStations(includeDisabled: false);
        var taken = GetDevices().Select(d => d.StationId).ToHashSet();
        return stations.FirstOrDefault(s => !taken.Contains(s.Id)) ?? stations.FirstOrDefault();
    }

    public void MoveDeviceToStation(int deviceId, int stationId)
    {
        _db.Execute("UPDATE station_devices SET station_id = $s WHERE id = $id;",
            ("$s", stationId), ("$id", deviceId));
        RaiseChanged();
    }

    public void SetDeviceEnabled(int deviceId, bool enabled)
    {
        _db.Execute("UPDATE station_devices SET is_enabled = $e WHERE id = $id;",
            ("$e", enabled ? 1 : 0), ("$id", deviceId));
        RaiseChanged();
    }

    public void DeleteDevice(int deviceId)
    {
        _db.Execute("DELETE FROM station_devices WHERE id = $id;", ("$id", deviceId));
        RaiseChanged();
    }

    /// <summary>Removes every scanner-to-station mapping. Returns rows removed.</summary>
    public int DeleteAllDevices()
    {
        var n = _db.Execute("DELETE FROM station_devices;");
        RaiseChanged();
        return n;
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
    };

    private static StationDevice MapDevice(IDataRecord r) => new()
    {
        Id = r.GetInt("id"),
        StationId = r.GetInt("station_id"),
        DeviceKey = r.GetString("device_key"),
        DeviceName = r.GetString("device_name"),
        IsEnabled = r.GetBool("is_enabled"),
        CreatedAt = r.GetDate("created_at"),
        StationCode = r.GetString("station_code"),
    };
}
