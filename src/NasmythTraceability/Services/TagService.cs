using System.Data;
using NasmythTraceability.Data;
using NasmythTraceability.Models;

namespace NasmythTraceability.Services;

/// <summary>
/// RFID tag to work order assignments. A reader only ever sees the tag id; this is where it
/// is turned into the work order that is searched and tracked.
/// </summary>
public sealed class TagService
{
    private const string SelectSql =
        "SELECT a.tag_id, a.work_order, a.assigned_at, c.status, c.station_code " +
        "FROM tag_assignments a LEFT JOIN current_trace c ON c.barcode = a.work_order ";

    private readonly DatabaseService _db;

    public TagService(DatabaseService db) => _db = db;

    /// <summary>Raised after an assignment is added, replaced or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Tag ids are compared exactly as the reader sends them, minus surrounding spaces.</summary>
    public static string NormalizeTag(string? tagId) => (tagId ?? "").Trim();

    /// <summary>Work orders are stored in upper case so "wo-12a" and "WO-12A" are the same job.</summary>
    public static string NormalizeWorkOrder(string? workOrder) => (workOrder ?? "").Trim().ToUpperInvariant();

    /// <summary>Letters and digits, with - _ / . allowed inside. Returns null when valid, else the reason.</summary>
    public static string? ValidateWorkOrder(string workOrder, int minLength)
    {
        minLength = Math.Max(1, minLength);
        if (workOrder.Length < minLength)
            return $"Work order must be at least {minLength} characters.";
        if (workOrder.Length > 40)
            return "Work order must be 40 characters or fewer.";
        if (!char.IsLetterOrDigit(workOrder[0]) || !char.IsLetterOrDigit(workOrder[^1]))
            return "Work order must start and end with a letter or digit.";
        if (workOrder.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '/' or '.')))
            return "Work order may only contain letters, digits and - _ / .";
        return null;
    }

    public TagAssignment? GetByTag(string tagId)
        => _db.QuerySingle(SelectSql + "WHERE a.tag_id = $t;", Map, ("$t", NormalizeTag(tagId)));

    public TagAssignment? GetByWorkOrder(string workOrder)
        => _db.QuerySingle(SelectSql + "WHERE a.work_order = $w;", Map, ("$w", NormalizeWorkOrder(workOrder)));

    /// <summary>All assignments, newest first, optionally filtered by part of a tag id or work order.</summary>
    public List<TagAssignment> GetAll(string? search = null)
    {
        var like = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
        return _db.Query(
            SelectSql + "WHERE ($q IS NULL OR a.tag_id LIKE $q OR a.work_order LIKE $q) " +
            "ORDER BY a.assigned_at DESC;",
            Map, ("$q", like));
    }

    /// <summary>
    /// Links a tag to a work order. Any earlier link of that tag, or of that work order to
    /// another tag, is replaced.
    /// </summary>
    public void Assign(string tagId, string workOrder)
    {
        var tag = NormalizeTag(tagId);
        var order = NormalizeWorkOrder(workOrder);
        if (tag.Length == 0 || order.Length == 0)
            throw new ArgumentException("A tag id and a work order are both required.");

        _db.InTransaction((cn, tx) =>
        {
            DatabaseService.Exec(cn, tx,
                "DELETE FROM tag_assignments WHERE tag_id = $t OR work_order = $w;", ("$t", tag), ("$w", order));
            DatabaseService.Exec(cn, tx,
                "INSERT INTO tag_assignments (tag_id, work_order, assigned_at) VALUES ($t, $w, $at);",
                ("$t", tag), ("$w", order), ("$at", Db.ToDb(DateTime.Now)));
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string tagId)
    {
        _db.Execute("DELETE FROM tag_assignments WHERE tag_id = $t;", ("$t", NormalizeTag(tagId)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes every tag assignment. Returns rows removed.</summary>
    public int RemoveAll()
    {
        var n = _db.Execute("DELETE FROM tag_assignments;");
        Changed?.Invoke(this, EventArgs.Empty);
        return n;
    }

    private static TagAssignment Map(IDataRecord r)
    {
        var status = r.GetString("status");
        return new TagAssignment
        {
            TagId = r.GetString("tag_id"),
            WorkOrder = r.GetString("work_order"),
            AssignedAt = r.GetDate("assigned_at"),
            Status = Enum.TryParse<TraceStatus>(status, out var s) ? s : null,
            StationCode = r.GetString("station_code"),
        };
    }
}
