namespace NasmythTraceability.Data;

/// <summary>First-run seed data: four production stations, ST04 fixed as the final station.</summary>
internal static class Seed
{
    public static void Run(DatabaseService db)
    {
        var stationCount = db.ScalarInt("SELECT COUNT(*) FROM stations;");
        if (stationCount == 0)
        {
            db.InTransaction((cn, tx) =>
            {
                var now = Db.ToDb(DateTime.Now);
                (string code, string name, int seq, int isFinal)[] stations =
                {
                    ("ST01", "Station 1", 1, 0),
                    ("ST02", "Station 2", 2, 0),
                    ("ST03", "Station 3", 3, 0),
                    ("ST04", "Station 4", 4, 1),
                };

                foreach (var s in stations)
                {
                    DatabaseService.Exec(cn, tx,
                        "INSERT INTO stations (code, name, sequence, is_enabled, is_final, created_at) " +
                        "VALUES ($c, $n, $s, 1, $f, $t);",
                        ("$c", s.code), ("$n", s.name), ("$s", s.seq), ("$f", s.isFinal), ("$t", now));
                }
            });
        }

        // Final status is a flag (is_final), not part of the name - strip any legacy "(Final)" suffix.
        db.Execute(
            "UPDATE stations SET name = TRIM(REPLACE(REPLACE(name, '(Final)', ''), '( Final )', '')) " +
            "WHERE name LIKE '%(Final)%';");

        var settings = new SettingsService(db);
        settings.EnsureDefaults();
    }
}
