namespace NasmythTraceability.Models;

/// <summary>Production station. One row per physical station plus the fixed final station.</summary>
public sealed class Station
{
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

    public override string ToString() => $"{Code} - {Name}";
}
