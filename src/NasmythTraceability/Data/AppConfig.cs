using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NasmythTraceability.Data;

/// <summary>
/// Bootstrap configuration read from config.json next to the executable.
/// Only holds values needed before the database is open (the rest live in the settings table).
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = "Work Order Traceability System";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "Nasmyth Asia (IN) Pvt Ltd.";

    /// <summary>Absolute path to the SQLite file. Empty => default under %ProgramData%.</summary>
    [JsonPropertyName("databasePath")]
    public string DatabasePath { get; set; } = "";

    public static string DefaultDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Nasmyth", "Traceability", "traceability.db");

    public string ResolveDatabasePath()
        => string.IsNullOrWhiteSpace(DatabasePath) ? DefaultDatabasePath : DatabasePath;

    public static AppConfig Load()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg is not null)
                    return cfg;
            }
        }
        catch
        {
            // fall through to defaults - a missing/broken config must not stop start-up
        }

        return new AppConfig();
    }

    public void Save()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "config.json");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json);
    }
}
