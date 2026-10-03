namespace NasmythTraceability.Data;

/// <summary>DDL and seed data for the traceability database.</summary>
internal static class SchemaSql
{
    // 2: trace_history.exited_at, tag_assignments
    public const int SchemaVersion = 2;

    public const string CreateSchema = """
        CREATE TABLE IF NOT EXISTS schema_info (
            id            INTEGER PRIMARY KEY CHECK (id = 1),
            version       INTEGER NOT NULL,
            applied_at    TEXT    NOT NULL
        );

        -- Production stations and the fixed final station.
        CREATE TABLE IF NOT EXISTS stations (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            code          TEXT    NOT NULL UNIQUE,
            name          TEXT    NOT NULL,
            sequence      INTEGER NOT NULL DEFAULT 0,
            is_enabled    INTEGER NOT NULL DEFAULT 1,
            is_final      INTEGER NOT NULL DEFAULT 0,
            created_at    TEXT    NOT NULL
        );

        -- USB reader (HID device) -> station mapping. One reader per station.
        CREATE TABLE IF NOT EXISTS station_devices (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            station_id    INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
            device_key    TEXT    NOT NULL UNIQUE,
            device_name   TEXT    NOT NULL DEFAULT '',
            is_enabled    INTEGER NOT NULL DEFAULT 1,
            created_at    TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_station_devices_station ON station_devices(station_id);

        -- Available barcode routes.
        CREATE TABLE IF NOT EXISTS routes (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            code          TEXT    NOT NULL UNIQUE,
            name          TEXT    NOT NULL,
            description   TEXT    NOT NULL DEFAULT '',
            is_active     INTEGER NOT NULL DEFAULT 1,
            created_at    TEXT    NOT NULL
        );

        -- Stations and sequence per route.
        CREATE TABLE IF NOT EXISTS route_stations (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            route_id      INTEGER NOT NULL REFERENCES routes(id) ON DELETE CASCADE,
            station_id    INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
            sequence      INTEGER NOT NULL,
            UNIQUE(route_id, station_id),
            UNIQUE(route_id, sequence)
        );
        CREATE INDEX IF NOT EXISTS ix_route_stations_route ON route_stations(route_id);

        -- Complete barcode station history.
        CREATE TABLE IF NOT EXISTS trace_history (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            barcode       TEXT    NOT NULL,
            station_id    INTEGER NOT NULL REFERENCES stations(id),
            station_code  TEXT    NOT NULL,
            result        TEXT    NOT NULL CHECK (result IN ('OK','NG')),
            message       TEXT    NOT NULL DEFAULT '',
            route_id      INTEGER REFERENCES routes(id),
            device_key    TEXT    NOT NULL DEFAULT '',
            scanned_at    TEXT    NOT NULL,
            exited_at     TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_trace_history_barcode ON trace_history(barcode);
        CREATE INDEX IF NOT EXISTS ix_trace_history_scanned_at ON trace_history(scanned_at);
        CREATE INDEX IF NOT EXISTS ix_trace_history_station ON trace_history(station_id);

        -- Latest / current station per barcode.
        CREATE TABLE IF NOT EXISTS current_trace (
            barcode       TEXT    PRIMARY KEY,
            station_id    INTEGER NOT NULL REFERENCES stations(id),
            station_code  TEXT    NOT NULL,
            result        TEXT    NOT NULL CHECK (result IN ('OK','NG')),
            status        TEXT    NOT NULL DEFAULT 'InProgress',
            route_id      INTEGER REFERENCES routes(id),
            scan_count    INTEGER NOT NULL DEFAULT 1,
            first_scan_at TEXT    NOT NULL,
            last_scan_at  TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_current_trace_last_scan ON current_trace(last_scan_at);

        -- Raw, duplicate, rejected and error scans.
        CREATE TABLE IF NOT EXISTS scan_logs (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            raw_data      TEXT    NOT NULL DEFAULT '',
            station_id    INTEGER,
            station_code  TEXT    NOT NULL DEFAULT '',
            device_key    TEXT    NOT NULL DEFAULT '',
            device_name   TEXT    NOT NULL DEFAULT '',
            log_type      TEXT    NOT NULL,
            message       TEXT    NOT NULL DEFAULT '',
            created_at    TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_scan_logs_created_at ON scan_logs(created_at);
        CREATE INDEX IF NOT EXISTS ix_scan_logs_type ON scan_logs(log_type);

        -- RFID tag -> work order. A tag carries one work order and a work order has one tag.
        CREATE TABLE IF NOT EXISTS tag_assignments (
            tag_id        TEXT    PRIMARY KEY,
            work_order    TEXT    NOT NULL UNIQUE,
            assigned_at   TEXT    NOT NULL
        );

        -- Key/value application settings (Settings screen).
        CREATE TABLE IF NOT EXISTS settings (
            key           TEXT    PRIMARY KEY,
            value         TEXT    NOT NULL
        );
        """;
}
