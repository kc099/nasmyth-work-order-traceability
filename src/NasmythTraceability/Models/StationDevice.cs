namespace NasmythTraceability.Models;

/// <summary>Maps one USB barcode scanner (HID device) to the station it sits at.</summary>
public sealed class StationDevice
{
    public int Id { get; set; }

    public int StationId { get; set; }

    /// <summary>Raw Input device path, e.g. \\?\HID#VID_05E0&amp;PID_1200#... . Unique key for the scanner.</summary>
    public string DeviceKey { get; set; } = "";

    /// <summary>Friendly name for the Settings UI.</summary>
    public string DeviceName { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    // Convenience (joined) - not persisted on this row.
    public string StationCode { get; set; } = "";
}
