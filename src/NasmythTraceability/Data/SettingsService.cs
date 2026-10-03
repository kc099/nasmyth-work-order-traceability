using System.Globalization;

namespace NasmythTraceability.Data;

/// <summary>
/// Typed access to the <c>settings</c> key/value table. Values are cached in memory and
/// written through on change.
/// </summary>
public sealed class SettingsService
{
    private readonly DatabaseService _db;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SettingsService(DatabaseService db)
    {
        _db = db;
        Reload();
    }

    public event EventHandler? Changed;

    // ---- keys ---------------------------------------------------------------
    public const string ApplicationName = "general.applicationName";
    public const string CompanyName = "general.companyName";
    public const string DateTimeFormat = "general.dateTimeFormat";
    public const string AutoStartWithWindows = "general.autoStart";

    public const string EnforceRouteSequence = "scanning.enforceRouteSequence";
    public const string DuplicateWindowSeconds = "scanning.duplicateWindowSeconds";
    public const string MinBarcodeLength = "scanning.minBarcodeLength";
    public const string TreatUnmappedDeviceAsError = "scanning.treatUnmappedAsError";

    /// <summary>Link a reader nobody has set up to a station on its first card tap.</summary>
    public const string AutoDetectReaders = "scanning.autoDetectReaders";

    /// <summary>
    /// Semicolon-separated hardware ids of reader models that are always treated as readers.
    /// The default is the JT308 125 kHz USB card reader.
    /// </summary>
    public const string KnownReaderIds = "scanning.knownReaderIds";
    public const string DefaultKnownReaderIds = "VID_FFFF&PID_0035";

    public const string ReportDefaultRangeDays = "reports.defaultRangeDays";
    public const string ReportExportFolder = "reports.exportFolder";
    public const string ReportDefaultFormat = "reports.defaultFormat";

    public const string BaseFontSize = "appearance.baseFontSize";
    public const string StartFullScreen = "appearance.startFullScreen";

    /// <summary>SHA-256 hex of the password that unlocks editing on the Settings screen.</summary>
    public const string SettingsPasswordHash = "security.settingsPasswordHash";
    public const string DefaultSettingsPassword = "admin";

    /// <summary>
    /// Seconds without a key press or click before a page other than the Dashboard closes.
    /// 0 switches the timeout off.
    /// </summary>
    public const string PageTimeoutSeconds = "security.pageTimeoutSeconds";

    public static string HashPassword(string password)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("nasmyth-traceability:" + password)));

    /// <summary>True when <paramref name="password"/> is the administrator (Settings) password.</summary>
    public bool CheckPassword(string password)
        => HashPassword(password) == Get(SettingsPasswordHash, HashPassword(DefaultSettingsPassword));

    // ---- read -------------------------------------------------------------
    public string Get(string key, string fallback = "")
        => _cache.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback)
        => _cache.TryGetValue(key, out var v) &&
           int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i : fallback;

    public double GetDouble(string key, double fallback)
        => _cache.TryGetValue(key, out var v) &&
           double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : fallback;

    public bool GetBool(string key, bool fallback)
        => _cache.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;

    // ---- write ----------------------------------------------------------
    public void Set(string key, string value)
    {
        _cache[key] = value;
        _db.Execute(
            "INSERT INTO settings (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$k", key), ("$v", value));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string key, double value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    public void Set(string key, bool value) => Set(key, value ? "true" : "false");

    public void SetMany(IEnumerable<KeyValuePair<string, string>> values)
    {
        _db.InTransaction((cn, tx) =>
        {
            foreach (var kv in values)
            {
                _cache[kv.Key] = kv.Value;
                DatabaseService.Exec(cn, tx,
                    "INSERT INTO settings (key, value) VALUES ($k, $v) " +
                    "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                    ("$k", kv.Key), ("$v", kv.Value));
            }
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        _cache.Clear();
        foreach (var row in _db.Query("SELECT key, value FROM settings;",
                     r => (Key: r.GetString("key"), Value: r.GetString("value"))))
        {
            _cache[row.Key] = row.Value;
        }
    }

    /// <summary>Applies defaults for any key not already present.</summary>
    public void EnsureDefaults()
    {
        var defaults = new Dictionary<string, string>
        {
            [ApplicationName] = "Work Order Traceability System",
            [CompanyName] = "Nasmyth Asia (IN) Pvt Ltd.",
            [DateTimeFormat] = "dddd, dd MMMM yyyy",
            [AutoStartWithWindows] = "false",
            [EnforceRouteSequence] = "false",
            [DuplicateWindowSeconds] = "5",
            [MinBarcodeLength] = "4",
            [TreatUnmappedDeviceAsError] = "true",
            [AutoDetectReaders] = "true",
            [KnownReaderIds] = DefaultKnownReaderIds,
            [ReportDefaultRangeDays] = "7",
            [ReportExportFolder] = "",
            [ReportDefaultFormat] = "Excel",
            [BaseFontSize] = "14",
            [StartFullScreen] = "false",
            [SettingsPasswordHash] = HashPassword(DefaultSettingsPassword),
            [PageTimeoutSeconds] = "120",
        };

        var missing = defaults.Where(kv => !_cache.ContainsKey(kv.Key)).ToList();
        if (missing.Count > 0)
            SetMany(missing);

        // Databases created before the rename still carry the old default title.
        if (_cache.TryGetValue(ApplicationName, out var name) && name == "Barcode Traceability System")
            SetMany(new Dictionary<string, string> { [ApplicationName] = defaults[ApplicationName] });
    }
}
