using CommunityToolkit.Mvvm.ComponentModel;

namespace NasmythTraceability.Models;

/// <summary>Production station. One row per physical station plus the fixed final station.</summary>
public sealed class Station : ObservableObject
{
    private string _readerName = "";

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

    /// <summary>
    /// Name of the reader assigned to this station (joined, not persisted on this row).
    /// Observable so the Settings grid follows a reader being assigned or removed.
    /// </summary>
    public string ReaderName
    {
        get => _readerName;
        set
        {
            if (SetProperty(ref _readerName, value))
                OnPropertyChanged(nameof(ReaderText));
        }
    }

    public string ReaderText => string.IsNullOrEmpty(ReaderName) ? "Not assigned" : ReaderName;

    public override string ToString() => $"{Code} - {Name}";
}
