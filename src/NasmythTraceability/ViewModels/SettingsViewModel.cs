using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NasmythTraceability.Data;
using NasmythTraceability.Export;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;

namespace NasmythTraceability.ViewModels;

/// <summary>Backs the Settings screen: Stations, Database, Logs.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SettingsService _settings;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _settings = services.Settings;

        ReloadStations();
        ReloadLogs();

        DatabasePath = _services.Database.DatabasePath;
    }

    // =====================================================================
    // Stations
    // =====================================================================
    public ObservableCollection<Station> Stations { get; } = new();
    [ObservableProperty] private Station? _selectedStation;

    private void ReloadStations()
    {
        Stations.Clear();
        foreach (var s in _services.Stations.GetStations())
            Stations.Add(s);
        SelectedStation = Stations.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void AddStation()
    {
        var next = Stations.Count + 1;
        var s = new Station { Code = $"ST{next:00}", Name = $"Station {next}", Sequence = next, IsEnabled = true };
        s.Id = _services.Stations.AddStation(s);
        Stations.Add(s);
        SelectedStation = s;
        Toast($"Added {s.Code}.");
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void SaveStations()
    {
        foreach (var s in Stations)
        {
            if (s.Id == 0) s.Id = _services.Stations.AddStation(s);
            else _services.Stations.UpdateStation(s);
        }
        ReloadStations();
        Toast("Stations saved.");
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void DeleteStation()
    {
        if (SelectedStation is null) return;

        var code = SelectedStation.Code;
        if (SelectedStation.Id == 0)
        {
            Stations.Remove(SelectedStation);
            SelectedStation = Stations.FirstOrDefault();
            return;
        }

        var refs = _services.Stations.CountStationTraceRefs(SelectedStation.Id);
        var prompt = refs > 0
            ? $"Station {code} has {refs} scan record(s). Delete the station AND all of its scan history / logs?"
            : $"Delete station {code}?";
        if (Confirm(prompt) != MessageBoxResult.Yes)
            return;

        try
        {
            _services.Stations.DeleteStation(SelectedStation.Id, cascadeTraceData: refs > 0);
            ReloadStations();
            ReloadLogs();
            Toast($"Deleted station {code}.");
        }
        catch (Exception ex)
        {
            Toast("Delete failed: " + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void SetFinalStation()
    {
        if (SelectedStation is null || SelectedStation.Id == 0)
        {
            Toast("Select a saved station first.");
            return;
        }

        var id = SelectedStation.Id;
        var code = SelectedStation.Code;
        try
        {
            _services.Stations.SetFinalStation(id);
            ReloadStations();
            SelectedStation = Stations.FirstOrDefault(s => s.Id == id);
            Toast($"{code} is now the final station.");
        }
        catch (Exception ex)
        {
            Toast("Could not set final station: " + ex.Message);
        }
    }

    // =====================================================================
    // Database
    // =====================================================================
    [ObservableProperty] private string _databasePath = "";

    [RelayCommand]
    private void BackupDatabase()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Backup database",
            Filter = "SQLite database (*.db)|*.db",
            FileName = $"traceability_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _services.Database.Backup(dlg.FileName);
            Toast("Backup written.");
        }
        catch (Exception ex)
        {
            Toast("Backup failed: " + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void RestoreDatabase()
    {
        var dlg = new OpenFileDialog { Title = "Restore database", Filter = "SQLite database (*.db)|*.db" };
        if (dlg.ShowDialog() != true) return;
        if (Confirm("Restore will replace ALL current data. Continue?") != MessageBoxResult.Yes) return;

        try
        {
            _services.Database.Restore(dlg.FileName);
            _settings.Reload();
            ReloadStations();
            ReloadLogs();
            Toast("Database restored.");
        }
        catch (Exception ex)
        {
            Toast("Restore failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_services.Database.DatabasePath);
            if (dir is not null && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    // =====================================================================
    // Logs
    // =====================================================================
    public ObservableCollection<ScanLog> Logs { get; } = new();
    public string[] LogFilters { get; } = { "All", "Raw", "Duplicate", "Rejected", "Error", "Info" };
    [ObservableProperty] private string _selectedLogFilter = "All";
    [ObservableProperty] private ScanLog? _selectedLog;

    partial void OnSelectedLogFilterChanged(string value) => ReloadLogs();

    [RelayCommand]
    private void ReloadLogs()
    {
        Logs.Clear();
        ScanLogType? type = SelectedLogFilter == "All" || !Enum.TryParse<ScanLogType>(SelectedLogFilter, out var t)
            ? null : t;
        foreach (var l in _services.Trace.GetScanLogs(300, type))
            Logs.Add(l);
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void DeleteLog()
    {
        if (SelectedLog is null) return;

        // Raw / Rejected logs correspond to a recorded scan - remove that too so reports stay in sync.
        _services.Trace.DeleteScanLogAndTrace(SelectedLog);
        Logs.Remove(SelectedLog);
        SelectedLog = null;
        Toast("Log entry deleted (and its scan record, if any).");
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void ClearLogs()
    {
        var scoped = SelectedLogFilter != "All"
                     && Enum.TryParse<ScanLogType>(SelectedLogFilter, out _);

        if (scoped)
        {
            if (Confirm($"Delete all '{SelectedLogFilter}' log entries? This cannot be undone.") != MessageBoxResult.Yes)
                return;

            Enum.TryParse<ScanLogType>(SelectedLogFilter, out var t);
            var n = _services.Trace.ClearScanLogs(t);
            ReloadLogs();
            Toast($"Deleted {n} '{SelectedLogFilter}' log entr{(n == 1 ? "y" : "ies")}.");
            return;
        }

        if (Confirm("Delete ALL logs AND the scan history / current-position data they came from?\n\n" +
                    "Stations, routes and settings are kept. This cannot be undone.") != MessageBoxResult.Yes)
            return;

        var removed = _services.Trace.ClearScanLogsAndTraces();
        ReloadLogs();
        Toast($"Cleared {removed} log entr{(removed == 1 ? "y" : "ies")} and all scan history.");
    }

    [RelayCommand]
    private void ExportLogsCsv()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export logs",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"scan_logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var w = new StreamWriter(dlg.FileName);
            w.WriteLine("CreatedAt,Type,Station,Device,Barcode,Message");
            foreach (var l in Logs)
                w.WriteLine(string.Join(',',
                    Csv(l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")), Csv(l.LogType.ToString()),
                    Csv(l.StationCode), Csv(l.DeviceName), Csv(l.RawData), Csv(l.Message)));
            Toast("Logs exported.");
        }
        catch (Exception ex)
        {
            Toast("Export failed: " + ex.Message);
        }
    }

    private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    // =====================================================================
    // Edit lock - one password guards every change made on this screen
    // =====================================================================
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddStationCommand), nameof(SaveStationsCommand),
        nameof(DeleteStationCommand), nameof(SetFinalStationCommand), nameof(RestoreDatabaseCommand),
        nameof(DeleteLogCommand), nameof(ClearLogsCommand), nameof(ChangePasswordCommand))]
    private bool _isUnlocked;

    [RelayCommand]
    private void Unlock(PasswordBox? box)
    {
        var entered = box?.Password ?? "";
        var stored = _settings.Get(SettingsService.SettingsPasswordHash,
            SettingsService.HashPassword(SettingsService.DefaultSettingsPassword));
        box?.Clear();

        if (SettingsService.HashPassword(entered) != stored)
        {
            Toast("Wrong password.");
            return;
        }

        IsUnlocked = true;
        Toast("Unlocked - you can now edit settings.");
    }

    /// <summary>Locks editing again; also called when leaving the Settings screen.</summary>
    [RelayCommand]
    public void Lock()
    {
        if (!IsUnlocked) return;
        IsUnlocked = false;
        ReloadStations(); // drop any unsaved grid edits
        Toast("Settings locked.");
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void ChangePassword(PasswordBox? box)
    {
        var next = box?.Password ?? "";
        if (next.Length < 4)
        {
            Toast("New password must be at least 4 characters.");
            return;
        }

        _settings.Set(SettingsService.SettingsPasswordHash, SettingsService.HashPassword(next));
        box?.Clear();
        Toast("Password changed.");
    }

    // =====================================================================
    // shared
    // =====================================================================
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Selected tab (0=Stations, 1=Database, 2=Logs). Lets navigation focus a section.</summary>
    [ObservableProperty] private int _settingsTabIndex;

    private void Toast(string message) => StatusMessage = message;

    private static MessageBoxResult Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

    public void Dispose()
    {
    }
}
