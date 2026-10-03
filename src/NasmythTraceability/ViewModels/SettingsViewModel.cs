using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.ViewModels;

/// <summary>Backs the Settings screen: Stations (with their readers) and Database.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SettingsService _settings;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _settings = services.Settings;

        ReloadStations();

        AutoDetectReaders = _settings.GetBool(SettingsService.AutoDetectReaders, true);
        DatabasePath = _services.Database.DatabasePath;

        // A reader can link itself on its first card tap - show it against its station straight away.
        _services.Stations.Changed += OnStationsChanged;
    }

    /// <summary>Refreshes the Reader column in place, so unsaved edits in the grid are not lost.</summary>
    private void OnStationsChanged(object? sender, EventArgs e)
    {
        var readers = _services.Stations.GetStations().ToDictionary(s => s.Id, s => s.ReaderName);
        foreach (var s in Stations)
            s.ReaderName = readers.TryGetValue(s.Id, out var name) ? name : "";
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
    // Reader of the selected station
    // =====================================================================
    [ObservableProperty] private bool _autoDetectReaders;

    /// <summary>True while waiting for a card tap that tells which reader belongs to the station.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssignReaderText))]
    private bool _isCapturingReader;

    private int _captureStationId;

    public string AssignReaderText => IsCapturingReader ? "Cancel" : "Assign reader";

    partial void OnAutoDetectReadersChanged(bool value)
    {
        if (_settings.GetBool(SettingsService.AutoDetectReaders, true) == value)
            return;
        _settings.Set(SettingsService.AutoDetectReaders, value);
        Toast(value ? "A new reader is linked to the first free station on its first card tap."
                    : "Automatic linking of new readers is off. Use Assign reader.");
    }

    /// <summary>
    /// Starts (or cancels) waiting for a card tap: the reader the card is tapped on becomes
    /// the selected station's reader.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void AssignReader()
    {
        if (IsCapturingReader)
        {
            StopReaderCapture();
            Toast("Reader assignment cancelled.");
            return;
        }

        if (SelectedStation is null || SelectedStation.Id == 0)
        {
            Toast("Select a saved station first.");
            return;
        }

        _captureStationId = SelectedStation.Id;
        IsCapturingReader = true;
        _services.Coordinator.ReadInterceptor = OnReaderCaptured;
        Toast($"Tap any card on the reader that belongs to {SelectedStation.Code}...");
    }

    private void OnReaderCaptured(BarcodeScannedEventArgs e)
    {
        var stationId = _captureStationId;
        StopReaderCapture();

        var station = _services.Stations.GetStation(stationId);
        if (station is null)
        {
            Toast("That station no longer exists.");
            return;
        }

        var previous = _services.Stations.GetDeviceByKey(e.DeviceKey);
        _services.Stations.MapDeviceToStation(e.DeviceKey, e.DeviceName, stationId);
        Toast(previous is not null && previous.StationId != stationId
            ? $"Reader moved from {previous.StationCode} to {station.Code}."
            : $"Reader assigned to {station.Code}.");
    }

    private void StopReaderCapture()
    {
        if (!IsCapturingReader)
            return;
        IsCapturingReader = false;
        _services.Coordinator.ReadInterceptor = null;
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void RemoveReader()
    {
        if (SelectedStation is null || SelectedStation.Id == 0)
        {
            Toast("Select a saved station first.");
            return;
        }

        var code = SelectedStation.Code;
        if (string.IsNullOrEmpty(SelectedStation.ReaderName))
        {
            Toast($"{code} has no reader assigned.");
            return;
        }

        if (Confirm($"Remove the reader from {code}? Card taps on it will not be recorded until it is assigned again.")
            != MessageBoxResult.Yes)
            return;

        _services.Stations.RemoveStationReader(SelectedStation.Id);
        Toast($"Reader removed from {code}.");
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
            AutoDetectReaders =_settings.GetBool(SettingsService.AutoDetectReaders, true);
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
        nameof(AssignReaderCommand), nameof(RemoveReaderCommand),
        nameof(ChangePasswordCommand))]
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
        StopReaderCapture();
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

    /// <summary>Selected tab (0=Stations, 1=Database). Lets navigation focus a section.</summary>
    [ObservableProperty] private int _settingsTabIndex;

    private void Toast(string message) => StatusMessage = message;

    private static MessageBoxResult Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

    public void Dispose()
    {
        _services.Stations.Changed -= OnStationsChanged;
    }
}
