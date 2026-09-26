namespace NasmythTraceability.Models;

/// <summary>A named barcode routing - an ordered list of stations a unit should pass through.</summary>
public sealed class Route
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public override string ToString() => $"{Code} - {Name}";
}

/// <summary>One station slot within a route, ordered by <see cref="Sequence"/>.</summary>
public sealed class RouteStation
{
    public int Id { get; set; }

    public int RouteId { get; set; }

    public int StationId { get; set; }

    public int Sequence { get; set; }

    // Convenience (joined).
    public string StationCode { get; set; } = "";
    public string StationName { get; set; } = "";
}
