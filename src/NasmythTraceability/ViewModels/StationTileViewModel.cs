using CommunityToolkit.Mvvm.ComponentModel;
using NasmythTraceability.Models;

namespace NasmythTraceability.ViewModels;

/// <summary>One of the big station status cards across the top of the dashboard.</summary>
public sealed partial class StationTileViewModel : ObservableObject
{
    public StationTileViewModel(Station station)
    {
        StationId = station.Id;
        Code = station.Code;
        Name = station.Name;
        IsEnabled = station.IsEnabled;
        IsFinal = station.IsFinal;
    }

    public int StationId { get; }

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isFinal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivity))]
    [NotifyPropertyChangedFor(nameof(LastScanText))]
    private DateTime? _lastScanAt;

    [ObservableProperty] private string _lastResult = "";
    [ObservableProperty] private int _sessionScans;

    public bool HasActivity => LastScanAt is not null;

    public string StatusText => IsEnabled ? "ONLINE" : "OFFLINE";

    public string LastScanText => LastScanAt is { } t ? t.ToString("HH:mm:ss") : "--:--:--";

    public void RegisterScan(DateTime at, string result)
    {
        LastScanAt = at;
        LastResult = result;
        SessionScans++;
        OnPropertyChanged(nameof(StatusText));
    }
}
