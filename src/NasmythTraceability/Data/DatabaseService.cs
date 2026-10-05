using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NasmythTraceability.Data;

/// <summary>
/// SQLite3 access layer: owns the connection string, creates/seeds the schema,
/// and exposes small parameterised query helpers used by the services.
/// </summary>
public sealed class DatabaseService : IDisposable
{
    private readonly object _writeLock = new();

    // Held open for as long as the app runs. While it is open Windows refuses to delete the
    // database file, so it cannot be removed by mistake underneath a running app.
    private SqliteConnection? _keepAlive;

    public DatabaseService(string databasePath)
    {
        DatabasePath = databasePath;
        ConnectionString = BuildConnectionString(databasePath);
    }

    private static string BuildConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Default,
        ForeignKeys = true,
        Pooling = true,
    }.ToString();

    public string DatabasePath { get; private set; }

    public string ConnectionString { get; private set; }

    public SqliteConnection Open()
    {
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        return cn;
    }

    /// <summary>
    /// Creates the database file, schema and first-run seed data if needed. A database file
    /// that has been deleted is simply created again, empty.
    /// </summary>
    public void Initialize()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Journal files left behind by a deleted database belong to the old file and must
        // not be replayed into the new one.
        if (!File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath + "-wal");
            File.Delete(DatabasePath + "-shm");
        }

        _keepAlive ??= Open();

        using var cn = Open();

        // journal_mode is persistent and must be set outside any transaction.
        ExecScript(cn, null, "PRAGMA journal_mode = WAL;");

        using (var tx = cn.BeginTransaction())
        {
            ExecScript(cn, tx, SchemaSql.CreateSchema);

            var version = ScalarInt(cn, tx, "SELECT version FROM schema_info WHERE id = 1;", -1);
            if (version < 0)
            {
                Exec(cn, tx,
                    "INSERT INTO schema_info (id, version, applied_at) VALUES (1, $v, $t);",
                    ("$v", SchemaSql.SchemaVersion), ("$t", Db.ToDb(DateTime.Now)));
            }
            else if (version < SchemaSql.SchemaVersion)
            {
                // Databases created before version 2 have no exit time on a station visit.
                if (ScalarInt(cn, tx,
                        "SELECT COUNT(*) FROM pragma_table_info('trace_history') WHERE name = 'exited_at';", 0) == 0)
                    ExecScript(cn, tx, "ALTER TABLE trace_history ADD COLUMN exited_at TEXT;");

                // Version 3: stations carry the address and identity of their network reader.
                foreach (var column in new[] { "reader_ip", "reader_mac" })
                    if (ScalarInt(cn, tx,
                            $"SELECT COUNT(*) FROM pragma_table_info('stations') WHERE name = '{column}';", 0) == 0)
                        ExecScript(cn, tx, $"ALTER TABLE stations ADD COLUMN {column} TEXT NOT NULL DEFAULT '';");

                // Version 4: each station scan in scan_logs says what it came to, and which tag it was.
                if (ScalarInt(cn, tx,
                        "SELECT COUNT(*) FROM pragma_table_info('scan_logs') WHERE name = 'outcome';", 0) == 0)
                {
                    ExecScript(cn, tx, "ALTER TABLE scan_logs ADD COLUMN outcome TEXT;");
                    ExecScript(cn, tx, "ALTER TABLE scan_logs ADD COLUMN tag_id TEXT NOT NULL DEFAULT '';");

                    // Older rows: work it out from the log type. Reader status lines have no
                    // scanned data, and "Exit from" lines are Info, so neither is counted.
                    ExecScript(cn, tx,
                        "UPDATE scan_logs SET outcome = CASE log_type " +
                        "  WHEN 'Raw' THEN 'Accepted' WHEN 'Duplicate' THEN 'Duplicate' " +
                        "  WHEN 'Rejected' THEN 'Rejected' WHEN 'Error' THEN 'Error' END " +
                        "WHERE station_id IS NOT NULL AND raw_data <> '' AND log_type <> 'Info';");
                    ExecScript(cn, tx,
                        "UPDATE scan_logs SET tag_id = raw_data, raw_data = '' " +
                        "WHERE message LIKE 'Tag is not assigned%';");
                }

                Exec(cn, tx, "UPDATE schema_info SET version = $v, applied_at = $t WHERE id = 1;",
                    ("$v", SchemaSql.SchemaVersion), ("$t", Db.ToDb(DateTime.Now)));
            }

            tx.Commit();
        }

        Seed.Run(this);
    }

    // ---------------------------------------------------------------- helpers

    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_writeLock)
        {
            using var cn = Open();
            return Exec(cn, null, sql, parameters);
        }
    }

    public long ExecuteReturningId(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_writeLock)
        {
            using var cn = Open();
            using var tx = cn.BeginTransaction();
            Exec(cn, tx, sql, parameters);
            var id = ScalarLong(cn, tx, "SELECT last_insert_rowid();", 0);
            tx.Commit();
            return id;
        }
    }

    /// <summary>Runs several write statements in a single transaction.</summary>
    public void InTransaction(Action<SqliteConnection, SqliteTransaction> work)
    {
        lock (_writeLock)
        {
            using var cn = Open();
            using var tx = cn.BeginTransaction();
            work(cn, tx);
            tx.Commit();
        }
    }

    public T? ScalarOrDefault<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, parameters);
        var value = cmd.ExecuteScalar();
        if (value is null || value is DBNull)
            return default;
        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public int ScalarInt(string sql, params (string Name, object? Value)[] parameters)
        => ScalarOrDefault<int>(sql, parameters);

    public List<T> Query<T>(string sql, Func<IDataRecord, T> map,
        params (string Name, object? Value)[] parameters)
    {
        var list = new List<T>();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, parameters);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(map(reader));
        return list;
    }

    public T? QuerySingle<T>(string sql, Func<IDataRecord, T> map,
        params (string Name, object? Value)[] parameters) where T : class
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        AddParams(cmd, parameters);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    // ---------------------------------------------------------------- backup / restore

    public void Backup(string destinationPath)
    {
        lock (_writeLock)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var source = Open();
            using var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString());
            destination.Open();
            source.BackupDatabase(destination);
        }
    }

    /// <summary>Replaces the live database with the contents of <paramref name="sourcePath"/>.</summary>
    public void Restore(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Backup file not found.", sourcePath);

        lock (_writeLock)
        {
            ReleaseFile();
            SqliteConnection.ClearAllPools();

            using (var source = new SqliteConnection(
                       new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString()))
            using (var destination = Open())
            {
                source.Open();
                // wipe target then copy in
                using (var wipe = destination.CreateCommand())
                {
                    wipe.CommandText = "PRAGMA writable_schema = 1; DELETE FROM sqlite_master; PRAGMA writable_schema = 0; VACUUM;";
                    wipe.ExecuteNonQuery();
                }
                source.BackupDatabase(destination);
            }

            SqliteConnection.ClearAllPools();
        }

        Initialize();
    }

    public void ChangeDatabasePath(string newPath)
    {
        ReleaseFile();
        DatabasePath = newPath;
        ConnectionString = BuildConnectionString(newPath);
    }

    /// <summary>Closes every connection this service holds on the database file.</summary>
    private void ReleaseFile()
    {
        if (_keepAlive is null)
            return;

        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
        _keepAlive = null;
    }

    public void Dispose() => ReleaseFile();

    // ---------------------------------------------------------------- internals

    internal static int Exec(SqliteConnection cn, SqliteTransaction? tx, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        AddParams(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    internal static void ExecScript(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal static int ScalarInt(SqliteConnection cn, SqliteTransaction? tx, string sql, int fallback)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? fallback : Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static long ScalarLong(SqliteConnection cn, SqliteTransaction? tx, string sql, long fallback)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? fallback : Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string ScalarString(SqliteConnection cn, SqliteTransaction? tx, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        AddParams(cmd, parameters);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? "" : v.ToString() ?? "";
    }

    internal static void AddParams(SqliteCommand cmd, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
