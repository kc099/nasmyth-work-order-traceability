using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services.Rfid;

namespace NasmythTraceability.ViewModels;

/// <summary>
/// Backs the Settings screen: Stations (each with the IP address of its network reader), the
/// work order assigning station, and Database.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SettingsService _settings;
    private readonly Dispatcher? _dispatcher;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _settings = services.Settings;
        _dispatcher = Application.Current?.Dispatcher;

        ReloadStations();
        LoadAssignmentStation();

        DatabasePath = _services.Database.DatabasePath;

        _services.Stations.Changed += OnStationsChanged;
        _services.Readers.StatusChanged += OnReaderStatusChanged;
    }

    private void OnStationsChanged(object? sender, EventArgs e) => OnUi(RefreshReaderStatuses);

    private void OnReaderStatusChanged(object? sender, ReaderStatusInfo e) => OnUi(RefreshReaderStatuses);

    /// <summary>Live reader status per station row and for the assignment station.</summary>
    private void RefreshReaderStatuses()
    {
        var saved = _services.Stations.GetStations().ToDictionary(s => s.Id);
        foreach (var s in Stations)
        {
            var row = saved.TryGetValue(s.Id, out var db) ? db : null;
            if (row is null || row.ReaderIp.Length == 0)
            {
                s.ReaderStatus = "No reader IP";
                s.ReaderStateKey = "";
            }
            else if (!row.IsEnabled)
            {
                s.ReaderStatus = "Station disabled - not polled";
                s.ReaderStateKey = "";
            }
            else
            {
                var status = _services.Readers.StationStatus(s.Id);
                s.ReaderStatus = status.StateText;
                s.ReaderStateKey = status.StateKey;
            }
        }

        var assignIp = _settings.Get(SettingsService.AssignmentReaderIp);
        var assign = _services.Readers.AssignmentStatus;
        AssignmentStatusText = assignIp.Length == 0 ? "Not set up" : assign.StateText;
        AssignmentStatusKey = assignIp.Length == 0 ? "" : assign.StateKey;
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
        RefreshReaderStatuses();
        SuggestNewStation();
    }

    // ---- new station ------------------------------------------------------
    [ObservableProperty] private string _newStationCode = "";
    [ObservableProperty] private string _newStationName = "";
    [ObservableProperty] private string _newStationIp = "";

    private void SuggestNewStation()
    {
        var next = Stations.Count + 1;
        while (Stations.Any(s => string.Equals(s.Code, $"ST{next:00}", StringComparison.OrdinalIgnoreCase)))
            next++;
        NewStationCode = $"ST{next:00}";
        NewStationName = $"Station {next}";
        NewStationIp = "";
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void AddStation()
    {
        var code = NewStationCode.Trim().ToUpperInvariant();
        var name = NewStationName.Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            Toast("Enter a code and a name for the new station.");
            return;
        }

        if (Stations.Any(s => string.Equals(s.Code.Trim(), code, StringComparison.OrdinalIgnoreCase)))
        {
            Toast($"There is already a station {code}.");
            return;
        }

        var ip = ReaderAddress.Normalize(NewStationIp, out var error);
        if (ip is null)
        {
            Toast(error!);
            return;
        }

        if (ip.Length > 0 && FindAddressUser(ip) is { } user)
        {
            Toast($"{ip} is already the reader of {user}. Each reader serves one station.");
            return;
        }

        var sequence = Stations.Count == 0 ? 1 : Stations.Max(s => s.Sequence) + 1;
        var station = new Station { Code = code, Name = name, Sequence = sequence, IsEnabled = true, ReaderIp = ip };
        try
        {
            station.Id = _services.Stations.AddStation(station);
        }
        catch (Exception ex)
        {
            Toast("Could not add the station: " + ex.Message);
            return;
        }

        ReloadStations();
        SelectedStation = Stations.FirstOrDefault(x => x.Id == station.Id);
        Toast(ip.Length == 0
            ? $"Added {code}. Enter its reader's IP address in the list when the reader is ready."
            : $"Added {code}. Its reader at {ip} is now polled.");
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void SaveStations()
    {
        // Validate everything first, so a bad row does not leave the list half saved.
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assignIp = _settings.Get(SettingsService.AssignmentReaderIp);
        var normalized = new Dictionary<Station, string>();

        foreach (var s in Stations)
        {
            if (string.IsNullOrWhiteSpace(s.Code) || string.IsNullOrWhiteSpace(s.Name))
            {
                Toast("Every station needs a code and a name.");
                return;
            }

            if (!codes.Add(s.Code.Trim()))
            {
                Toast($"Station code {s.Code.Trim()} is used twice.");
                return;
            }

            var ip = ReaderAddress.Normalize(s.ReaderIp, out var error);
            if (ip is null)
            {
                Toast($"{s.Code}: {error}");
                return;
            }

            if (ip.Length > 0)
            {
                if (ips.TryGetValue(ip, out var other))
                {
                    Toast($"{ip} is entered for both {other} and {s.Code}. Each reader serves one station.");
                    return;
                }

                if (string.Equals(ip, assignIp, StringComparison.OrdinalIgnoreCase))
                {
                    Toast($"{ip} is the work order assigning station's reader; it cannot also serve {s.Code}.");
                    return;
                }

                ips[ip] = s.Code;
            }

            normalized[s] = ip;
        }

        try
        {
            foreach (var s in Stations)
            {
                s.ReaderIp = normalized[s];
                if (s.Id == 0) s.Id = _services.Stations.AddStation(s);
                else _services.Stations.UpdateStation(s);
            }
        }
        catch (Exception ex)
        {
            Toast("Save failed: " + ex.Message);
            return;
        }

        ReloadStations();
        Toast("Stations saved. Each reader is polled at the address shown.");
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

    /// <summary>Asks the selected station's reader for its status (the address as typed, saved or not).</summary>
    [RelayCommand]
    private Task TestStationReaderAsync()
    {
        if (SelectedStation is null)
        {
            Toast("Select a station first.");
            return Task.CompletedTask;
        }

        return TestAsync(SelectedStation.ReaderIp, SelectedStation.Code);
    }

    [RelayCommand]
    private Task TestNewStationReaderAsync() => TestAsync(NewStationIp, "the new station");

    /// <summary>Station code (or the assigning station) already using a reader address.</summary>
    private string? FindAddressUser(string ip)
    {
        if (string.Equals(ip, _settings.Get(SettingsService.AssignmentReaderIp), StringComparison.OrdinalIgnoreCase))
            return "the work order assigning station";
        return Stations.FirstOrDefault(s =>
            string.Equals(ReaderAddress.Normalize(s.ReaderIp, out _), ip, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    // =====================================================================
    // Work order assigning station
    // =====================================================================
    [ObservableProperty] private string _assignmentIp = "";
    [ObservableProperty] private string _assignmentMac = "";
    [ObservableProperty] private string _assignmentStatusText = "";
    [ObservableProperty] private string _assignmentStatusKey = "";

    private void LoadAssignmentStation()
    {
        AssignmentIp = _settings.Get(SettingsService.AssignmentReaderIp);
        var mac = _settings.Get(SettingsService.AssignmentReaderMac);
        AssignmentMac = mac.Length == 0 || ReaderAddress.IsUsableMac(mac) ? mac : mac + "  (not reported by the reader firmware)";
        RefreshReaderStatuses();
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void SaveAssignmentStation()
    {
        var ip = ReaderAddress.Normalize(AssignmentIp, out var error);
        if (ip is null)
        {
            Toast(error!);
            return;
        }

        var user = ip.Length == 0
            ? null
            : _services.Stations.GetStations()
                .FirstOrDefault(s => string.Equals(s.ReaderIp, ip, StringComparison.OrdinalIgnoreCase))?.Code;
        if (user is not null)
        {
            Toast($"{ip} is the reader of {user}. The assigning station needs a reader of its own.");
            return;
        }

        if (ip != _settings.Get(SettingsService.AssignmentReaderIp))
        {
            if (_services.TagWriter.IsWaiting)
                _ = _services.TagWriter.CancelAsync();
            _settings.SetMany(new Dictionary<string, string>
            {
                [SettingsService.AssignmentReaderIp] = ip,
                [SettingsService.AssignmentReaderMac] = "", // a new address is a different reader
            });
        }

        LoadAssignmentStation();
        Toast(ip.Length == 0
            ? "Work order assigning station removed."
            : $"Work order assigning station saved. Its reader at {ip} is used by Tag Assignment.");
    }

    [RelayCommand]
    private Task TestAssignmentReaderAsync() => TestAsync(AssignmentIp, "the work order assigning station");

    // =====================================================================
    // Reader test (GET /api/status)
    // =====================================================================
    private async Task TestAsync(string address, string what)
    {
        var ip = ReaderAddress.Normalize(address, out var error);
        if (ip is null)
        {
            Toast(error!);
            return;
        }

        if (ip.Length == 0)
        {
            Toast($"Enter the IP address of {what}'s reader first.");
            return;
        }

        Toast($"Contacting {ip}...");
        try
        {
            var s = await _services.Readers.ProbeAsync(ip);
            Toast($"{ip} OK: {s.Device}, {(ReaderAddress.IsUsableMac(s.Mac) ? "MAC " + s.Mac : "no MAC reported (" + s.Mac + ")")}, firmware {s.Firmware}, {s.Mode} mode" +
                  (s.ReaderOk ? "." : " - WARNING: the RC522 module was not detected (check its wiring)."));
        }
        catch (Exception ex)
        {
            Toast($"{ip} did not answer: {RfidReaderClient.Describe(ex)}.");
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
            _services.ConfigureReaders(); // the restored stations may have other reader addresses
            ReloadStations();
            LoadAssignmentStation();
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
    // Edit lock - one password guards every change made on this screen
    // =====================================================================
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddStationCommand), nameof(SaveStationsCommand),
        nameof(DeleteStationCommand), nameof(SetFinalStationCommand), nameof(RestoreDatabaseCommand),
        nameof(SaveAssignmentStationCommand), nameof(ChangePasswordCommand))]
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
        LoadAssignmentStation();
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

    /// <summary>Selected tab (0=Stations, 1=Assignment Station, 2=Database). Lets navigation focus a section.</summary>
    [ObservableProperty] private int _settingsTabIndex;

    private void Toast(string message) => StatusMessage = message;

    private void OnUi(Action action)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            _dispatcher.BeginInvoke(action);
        else
            action();
    }

    private static MessageBoxResult Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

    public void Dispose()
    {
        _services.Stations.Changed -= OnStationsChanged;
        _services.Readers.StatusChanged -= OnReaderStatusChanged;
    }
}
