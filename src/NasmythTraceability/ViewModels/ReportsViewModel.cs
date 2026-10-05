using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Data;
using NasmythTraceability.Export;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services;

namespace NasmythTraceability.ViewModels;

/// <summary>Reports &amp; Analytics: date filter, valid / invalid / repeat scans per station, and Excel/PDF export of the valid scans.</summary>
public sealed partial class ReportsViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;

    public ReportsViewModel(AppServices services)
    {
        _services = services;

        var days = Math.Max(0, _services.Settings.GetInt(SettingsService.ReportDefaultRangeDays, 7));
        ToDate = DateTime.Today;
        FromDate = DateTime.Today.AddDays(-days);
        ExportFolder = ExportPaths.ResolveFolder(_services.Settings.Get(SettingsService.ReportExportFolder));
        ExportFormat = _services.Settings.Get(SettingsService.ReportDefaultFormat, "Excel");

        _services.Trace.Changed += OnTraceChanged;
        Apply();
    }

    private void OnTraceChanged(object? sender, TraceChangedEventArgs e) => Apply();

    public void Dispose() => _services.Trace.Changed -= OnTraceChanged;

    // ---- filter -------------------------------------------------------
    [ObservableProperty] private DateTime _fromDate;
    [ObservableProperty] private DateTime _toDate;

    // ---- chart ------------------------------------------------------
    /// <summary>Valid, invalid and repeat scans per station.</summary>
    public ObservableCollection<StationScanBreakdown> StationBreakdown { get; } = new();

    [ObservableProperty] private int _totalValid;
    [ObservableProperty] private int _totalInvalid;
    [ObservableProperty] private int _totalRepeat;

    // ---- export -----------------------------------------------------
    [ObservableProperty] private string _exportFolder = "";
    [ObservableProperty] private string _exportFormat = "Excel";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isBusy;

    public string RangeText => $"{FromDate:dd/MM/yyyy}  -  {ToDate:dd/MM/yyyy}";

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private void Apply()
    {
        if (ToDate < FromDate)
            (FromDate, ToDate) = (ToDate, FromDate);

        var rows = _services.Reports.GetScanBreakdown(FromDate, ToDate);
        StationBreakdown.Clear();
        foreach (var r in rows)
            StationBreakdown.Add(r);

        TotalValid = rows.Sum(r => r.Valid);
        TotalInvalid = rows.Sum(r => r.Invalid);
        TotalRepeat = rows.Sum(r => r.Repeat);

        OnPropertyChanged(nameof(RangeText));
        StatusMessage = $"{RangeText}: {TotalValid} valid, {TotalInvalid} invalid and {TotalRepeat} repeat scan(s). " +
                        "Exports contain the valid scans only.";
    }

    /// <summary>Re-runs the report - called when the Reports page is opened so it reflects deletions made elsewhere.</summary>
    public void Refresh() => Apply();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportExcelAsync() => ExportAsync("xlsx");

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportPdfAsync() => ExportAsync("pdf");

    private bool CanExport() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        ExportExcelCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    private async Task ExportAsync(string extension)
    {
        IsBusy = true;
        StatusMessage = $"Building {extension.ToUpperInvariant()} report...";
        try
        {
            var from = FromDate;
            var to = ToDate;
            var folder = ExportFolder;

            var path = await Task.Run(() =>
            {
                var bundle = _services.Reports.BuildBundle(from, to);
                var full = ExportPaths.BuildFullPath(folder, from, to, extension);
                if (extension == "pdf")
                    PdfExporter.Export(bundle, full);
                else
                    ExcelExporter.Export(bundle, full);
                return full;
            });

            StatusMessage = $"Saved: {path}";
            RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // best effort only
        }
    }
}
