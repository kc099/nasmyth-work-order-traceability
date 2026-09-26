using System.IO;

namespace NasmythTraceability.Export;

/// <summary>Builds default output locations for report exports.</summary>
public static class ExportPaths
{
    public static string ResolveFolder(string configuredFolder)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
            return configuredFolder;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Nasmyth Traceability", "Exports");
    }

    public static string BuildFileName(DateTime from, DateTime to, string extension)
        => $"Traceability_{from:yyyyMMdd}_{to:yyyyMMdd}_{DateTime.Now:HHmmss}.{extension}";

    public static string BuildFullPath(string configuredFolder, DateTime from, DateTime to, string extension)
        => Path.Combine(ResolveFolder(configuredFolder), BuildFileName(from, to, extension));
}
