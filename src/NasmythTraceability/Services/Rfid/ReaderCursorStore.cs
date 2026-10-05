using NasmythTraceability.Data;

namespace NasmythTraceability.Services.Rfid;

/// <summary>
/// Remembers, per reader (by MAC), the boot and the last event that was processed. On the next
/// start the app catches up on taps made while it was closed instead of skipping or repeating them.
/// </summary>
public sealed class ReaderCursorStore
{
    private readonly DatabaseService _db;

    public ReaderCursorStore(DatabaseService db) => _db = db;

    public (long BootId, long LastEventId)? Get(string mac)
    {
        if (string.IsNullOrEmpty(mac))
            return null;
        var row = _db.QuerySingle("SELECT boot_id, last_event_id FROM reader_cursors WHERE mac = $m;",
            r => new long[] { r.GetLong("boot_id"), r.GetLong("last_event_id") }, ("$m", mac));
        return row is null ? null : (row[0], row[1]);
    }

    public void Save(string mac, long bootId, long lastEventId)
    {
        if (string.IsNullOrEmpty(mac))
            return;
        _db.Execute(
            "INSERT INTO reader_cursors (mac, boot_id, last_event_id, updated_at) VALUES ($m, $b, $e, $t) " +
            "ON CONFLICT(mac) DO UPDATE SET boot_id = excluded.boot_id, last_event_id = excluded.last_event_id, " +
            "  updated_at = excluded.updated_at;",
            ("$m", mac), ("$b", bootId), ("$e", lastEventId), ("$t", Db.ToDb(DateTime.Now)));
    }
}
