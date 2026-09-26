using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

public readonly record struct RouteValidation(bool Ok, int? RouteId, string Message);

/// <summary>Variable-route definitions and sequence validation for a scan.</summary>
public sealed class RouteService
{
    private readonly DatabaseService _db;
    private readonly SettingsService _settings;

    public RouteService(DatabaseService db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ---------------------------------------------------------------- routes

    public List<Route> GetRoutes(bool includeInactive = true)
    {
        var sql = "SELECT * FROM routes" + (includeInactive ? "" : " WHERE is_active = 1") + " ORDER BY code;";
        return _db.Query(sql, MapRoute);
    }

    public Route? GetRoute(int id) => _db.QuerySingle("SELECT * FROM routes WHERE id = $id;", MapRoute, ("$id", id));

    public List<RouteStation> GetRouteStations(int routeId)
        => _db.Query(
            "SELECT rs.*, s.code AS station_code, s.name AS station_name FROM route_stations rs " +
            "JOIN stations s ON s.id = rs.station_id WHERE rs.route_id = $r ORDER BY rs.sequence;",
            MapRouteStation, ("$r", routeId));

    public int AddRoute(Route r)
        => (int)_db.ExecuteReturningId(
            "INSERT INTO routes (code, name, description, is_active, created_at) VALUES ($c, $n, $d, $a, $t);",
            ("$c", r.Code.Trim()), ("$n", r.Name.Trim()), ("$d", r.Description.Trim()),
            ("$a", r.IsActive ? 1 : 0), ("$t", Db.ToDb(DateTime.Now)));

    public void UpdateRoute(Route r)
        => _db.Execute(
            "UPDATE routes SET code = $c, name = $n, description = $d, is_active = $a WHERE id = $id;",
            ("$c", r.Code.Trim()), ("$n", r.Name.Trim()), ("$d", r.Description.Trim()),
            ("$a", r.IsActive ? 1 : 0), ("$id", r.Id));

    public void DeleteRoute(int id) => _db.Execute("DELETE FROM routes WHERE id = $id;", ("$id", id));

    /// <summary>Replaces the ordered station list for a route.</summary>
    public void SetRouteStations(int routeId, IEnumerable<int> stationIdsInOrder)
    {
        _db.InTransaction((cn, tx) =>
        {
            DatabaseService.Exec(cn, tx, "DELETE FROM route_stations WHERE route_id = $r;", ("$r", routeId));
            var seq = 1;
            foreach (var stationId in stationIdsInOrder)
            {
                DatabaseService.Exec(cn, tx,
                    "INSERT INTO route_stations (route_id, station_id, sequence) VALUES ($r, $s, $q);",
                    ("$r", routeId), ("$s", stationId), ("$q", seq++));
            }
        });
    }

    // ---------------------------------------------------------------- validation

    /// <summary>
    /// Checks a scan against route sequence rules.
    /// When <c>scanning.enforceRouteSequence</c> is off it only resolves the route id (never blocks).
    /// </summary>
    public RouteValidation Validate(string barcode, int stationId, int? currentRouteId, string? lastStationCode)
    {
        var enforce = _settings.GetBool(SettingsService.EnforceRouteSequence, false);

        // Routes that are active and include this station.
        var candidateRoutes = _db.Query(
            "SELECT r.id AS route_id, rs.sequence AS seq FROM routes r " +
            "JOIN route_stations rs ON rs.route_id = r.id " +
            "WHERE r.is_active = 1 AND rs.station_id = $s;",
            r => (RouteId: r.GetInt("route_id"), Seq: r.GetInt("seq")),
            ("$s", stationId));

        if (candidateRoutes.Count == 0)
        {
            // No route constrains this station.
            return new RouteValidation(true, currentRouteId, enforce ? "No route defined for this station" : "");
        }

        // Barcode already has a route: must follow that route's order.
        if (currentRouteId is int routeId)
        {
            var steps = GetRouteStations(routeId);
            var idx = steps.FindIndex(x => x.StationId == stationId);
            if (idx < 0)
            {
                return enforce
                    ? new RouteValidation(false, routeId, "Station is not part of the assigned route")
                    : new RouteValidation(true, routeId, "Station not on assigned route");
            }

            var lastIdx = string.IsNullOrEmpty(lastStationCode)
                ? -1
                : steps.FindIndex(x => x.StationCode == lastStationCode);

            var expectedNext = lastIdx + 1;
            if (idx == expectedNext || idx == lastIdx) // next step, or a re-scan of the same step
                return new RouteValidation(true, routeId, "");

            var msg = idx < expectedNext
                ? $"Out of sequence: {steps[expectedNext].StationCode} expected, went backwards"
                : $"Out of sequence: {steps[expectedNext].StationCode} expected, skipped ahead";
            return enforce ? new RouteValidation(false, routeId, msg) : new RouteValidation(true, routeId, msg);
        }

        // First scan for this barcode: prefer a route where this station is step 1.
        var startRoute = candidateRoutes.FirstOrDefault(c => c.Seq == 1);
        if (startRoute.RouteId != 0)
            return new RouteValidation(true, startRoute.RouteId, "");

        // Station exists on a route but is not a start station.
        var onlyRoute = candidateRoutes[0].RouteId;
        return enforce
            ? new RouteValidation(false, onlyRoute, "First scan must be at the route start station")
            : new RouteValidation(true, onlyRoute, "First scan not at route start");
    }

    // ---------------------------------------------------------------- mapping

    private static Route MapRoute(IDataRecord r) => new()
    {
        Id = r.GetInt("id"),
        Code = r.GetString("code"),
        Name = r.GetString("name"),
        Description = r.GetString("description"),
        IsActive = r.GetBool("is_active"),
        CreatedAt = r.GetDate("created_at"),
    };

    private static RouteStation MapRouteStation(IDataRecord r) => new()
    {
        Id = r.GetInt("id"),
        RouteId = r.GetInt("route_id"),
        StationId = r.GetInt("station_id"),
        Sequence = r.GetInt("sequence"),
        StationCode = r.GetString("station_code"),
        StationName = r.GetString("station_name"),
    };
}
