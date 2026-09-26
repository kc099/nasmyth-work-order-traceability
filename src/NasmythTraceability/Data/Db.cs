using System.Data;
using System.Globalization;

namespace NasmythTraceability.Data;

/// <summary>Formatting helpers and safe readers shared by the data layer.</summary>
public static class Db
{
    /// <summary>Sortable text date format used for every TEXT date column.</summary>
    public const string DateFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public static string ToDb(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateTime FromDb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;

        if (DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var loose) ? loose : default;
    }

    public static string GetString(this IDataRecord r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? "";
    }

    public static int GetInt(this IDataRecord r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    }

    public static int? GetIntOrNull(this IDataRecord r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    }

    public static long GetLong(this IDataRecord r, string name)
    {
        var i = r.GetOrdinal(name);
        return r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);
    }

    public static bool GetBool(this IDataRecord r, string name) => r.GetInt(name) != 0;

    public static DateTime GetDate(this IDataRecord r, string name) => FromDb(r.GetString(name));
}
