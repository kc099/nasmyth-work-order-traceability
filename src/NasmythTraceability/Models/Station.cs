using CommunityToolkit.Mvvm.ComponentModel;

namespace NasmythTraceability.Models;

/// <summary>Production station. One row per physical station plus the fixed final station.</summary>
public sealed class Station : ObservableObject
{
    private string _readerStatus = "";
    private string _readerStateKey = "";

    public int Id { get; set; }

    /// <summary>Short code shown in reports, e.g. ST01.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Ordering hint for display and default routes.</summary>
    public int Sequence { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>True for the single fixed final / end-of-line station.</summary>
    public bool IsFinal { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Address of the station's network RFID reader, host[:port]; empty when it has none.</summary>
    public string ReaderIp { get; set; } = "";

    /// <summary>MAC the reader at <see cref="ReaderIp"/> last reported - its permanent identity.</summary>
    public string ReaderMac { get; set; } = "";

    /// <summary>Live connection text for the Settings grid (not persisted).</summary>
    public string ReaderStatus
    {
        get => _readerStatus;
        set => SetProperty(ref _readerStatus, value);
    }

    /// <summary>"ONLINE" / "OFFLINE" / "WARN" / "" for the status brush (not persisted).</summary>
    public string ReaderStateKey
    {
        get => _readerStateKey;
        set => SetProperty(ref _readerStateKey, value);
    }

    public override string ToString() => $"{Code} - {Name}";
}
