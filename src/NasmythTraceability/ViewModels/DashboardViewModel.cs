using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services;

namespace NasmythTraceability.ViewModels;

/// <summary>Live station monitoring: station tiles, scan log, counters, barcode lookup.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int MaxLogRows = 200;

    private readonly AppServices _services;
    private readonly Dispatcher? _dispatcher;

    public DashboardViewModel(AppServices services)
    {
        _services = services;
        _dispatcher = Application.Current?.Dispatcher;

        RebuildStations();

        RefreshDeviceInfo();

        RefreshPositions();

        _services.Coordinator.ScanProcessed += OnScanProcessed;
        _services.Stations.Changed += OnStationsChanged;
        _services.Trace.Changed += OnTraceChanged;
    }

    /// <summary>Trace data was deleted / cleared from Settings - refresh every derived view.</summary>
    private void OnTraceChanged(object? sender, TraceChangedEventArgs e)
    {
        void Apply()
        {
            RefreshPositions();

            if (!string.IsNullOrEmpty(CurrentBarcode))
                LoadBarcode(CurrentBarcode); // clears panel/history if the barcode is gone

            if (e.Cleared)
            {
                TotalScans = OkScans = NgScans = 0;
                OnPropertyChanged(nameof(OkRateText));
                LastActivityText = "No scan data yet";
                LiveScanLog.Clear();
                History.Clear();

                CurrentBarcode = "";
                CurrentStation = CurrentStatus = CurrentResult = CurrentLastScan = "-";
                BarcodeFound = false;

                foreach (var tile in StationTiles)
                {
                    tile.SessionScans = 0;
                    tile.LastScanAt = null;
                    tile.LastResult = "";
                }
            }
        }

        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            _dispatcher.BeginInvoke(Apply);
        else
            Apply();
    }

    private void OnStationsChanged(object? sender, EventArgs e)
    {
        void Rebuild()
        {
            RebuildStations();
            RefreshPositions();
        }

        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            _dispatcher.BeginInvoke(Rebuild);
        else
            Rebuild();
    }

    /// <summary>Re-reads the station list, keeping session counters and the chosen manual station.</summary>
    private void RebuildStations()
    {
        var priorCounts = StationTiles.ToDictionary(t => t.StationId, t => (t.SessionScans, t.LastScanAt, t.LastResult));
        var priorManualId = SelectedManualStation?.Id;

        StationTiles.Clear();
        ManualStations.Clear();

        foreach (var s in _services.Stations.GetStations())
        {
            var tile = new StationTileViewModel(s);
            if (priorCounts.TryGetValue(s.Id, out var prev))
            {
                tile.SessionScans = prev.SessionScans;
                tile.LastScanAt = prev.LastScanAt;
                tile.LastResult = prev.LastResult;
            }
            StationTiles.Add(tile);
            ManualStations.Add(s);
        }

        SelectedManualStation = ManualStations.FirstOrDefault(s => s.Id == priorManualId)
                                ?? ManualStations.FirstOrDefault();

        PositionStationOptions.Clear();
        PositionStationOptions.Add("All stations");
        foreach (var s in ManualStations)
            PositionStationOptions.Add(s.Code);
        if (!PositionStationOptions.Contains(PositionStationFilter))
            PositionStationFilter = "All stations"; // setter re-runs RefreshPositions

        MappedDeviceCount = _services.Stations.GetDevices().Count(d => d.IsEnabled);
    }

    // ---- collections ----------------------------------------------------
    public ObservableCollection<StationTileViewModel> StationTiles { get; } = new();
    public ObservableCollection<ScanRow> LiveScanLog { get; } = new();
    public ObservableCollection<ScanRow> History { get; } = new();
    public ObservableCollection<Station> ManualStations { get; } = new();

    // ---- "which barcode is at which station right now" ------------------
    public ObservableCollection<CurrentTrace> CurrentPositions { get; } = new();
    public ObservableCollection<string> PositionStationOptions { get; } = new();
    [ObservableProperty] private string _positionStationFilter = "All stations";
    [ObservableProperty] private string _positionSearch = "";
    [ObservableProperty] private CurrentTrace? _selectedPosition;
    [ObservableProperty] private int _inProgressCount;
    [ObservableProperty] private int _completedCount;

    partial void OnPositionStationFilterChanged(string value) => RefreshPositions();
    partial void OnPositionSearchChanged(string value) => RefreshPositions();

    partial void OnSelectedPositionChanged(CurrentTrace? value)
    {
        if (value is not null && value.Barcode != CurrentBarcode)
            LoadBarcode(value.Barcode);
    }

    [RelayCommand]
    private void RefreshPositions()
    {
        var pinned = SelectedPosition?.Barcode;

        var station = string.IsNullOrEmpty(PositionStationFilter) || PositionStationFilter == "All stations"
            ? null
            : PositionStationFilter;

        var rows = _services.Trace.GetCurrentPositions(station, PositionSearch);

        CurrentPositions.Clear();
        foreach (var r in rows)
            CurrentPositions.Add(r);

        // keep the operator's selected unit selected across refreshes
        SelectedPosition = pinned is null ? null : CurrentPositions.FirstOrDefault(p => p.Barcode == pinned);

        InProgressCount = rows.Count(r => r.Status == TraceStatus.InProgress);
        CompletedCount = rows.Count(r => r.Status == TraceStatus.Completed);
    }

    // ---- counters -----------------------------------------------------
    [ObservableProperty] private int _totalScans;
    [ObservableProperty] private int _okScans;
    [ObservableProperty] private int _ngScans;

    // ---- current information ------------------------------------------
    [ObservableProperty] private string _currentBarcode = "";
    [ObservableProperty] private string _currentStation = "-";
    [ObservableProperty] private string _currentStatus = "-";
    [ObservableProperty] private string _currentResult = "-";
    [ObservableProperty] private string _currentLastScan = "-";
    [ObservableProperty] private bool _barcodeFound;

    // ---- search / simulate ------------------------------------------
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _simulateBarcode = "";
    [ObservableProperty] private Station? _selectedManualStation;

    [ObservableProperty] private int _mappedDeviceCount;
    [ObservableProperty] private string _lastActivityText = "No scan data yet";

    public string OkRateText => TotalScans == 0 ? "0%" : ((double)OkScans / TotalScans).ToString("P0");

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private void Search()
    {
        var term = SearchText.Trim();
        if (term.Length == 0)
            return;

        var match = _services.Trace.GetCurrent(term)
                    ?? _services.Trace.SearchBarcodes(term, 1).FirstOrDefault();

        if (match is null)
        {
            BarcodeFound = false;
            CurrentBarcode = term;
            CurrentStation = "-";
            CurrentStatus = "Not found";
            CurrentResult = "-";
            CurrentLastScan = "-";
            History.Clear();
            return;
        }

        LoadBarcode(match.Barcode);
    }

    [RelayCommand]
    private void SimulateScan()
    {
        if (SelectedManualStation is null || string.IsNullOrWhiteSpace(SimulateBarcode))
            return;

        _services.Coordinator.SubmitManualScan(SelectedManualStation.Id, SimulateBarcode.Trim());
        SimulateBarcode = "";
    }

    [RelayCommand]
    private void ClearLog() => LiveScanLog.Clear();

    [RelayCommand]
    private void RefreshDeviceInfo()
        => MappedDeviceCount = _services.Stations.GetDevices().Count(d => d.IsEnabled);

    // ---------------------------------------------------------------- scan feed

    private void OnScanProcessed(object? sender, ScanProcessedEventArgs e)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Apply(e));
            return;
        }

        Apply(e);
    }

    private void Apply(ScanProcessedEventArgs e)
    {
        var row = ScanRow.From(e);

        LiveScanLog.Insert(0, row);
        while (LiveScanLog.Count > MaxLogRows)
            LiveScanLog.RemoveAt(LiveScanLog.Count - 1);

        TotalScans++;
        if (e.Outcome == ScanOutcome.Accepted) OkScans++;
        else if (e.Outcome == ScanOutcome.Rejected) NgScans++;
        OnPropertyChanged(nameof(OkRateText));

        LastActivityText = $"{row.TimeText}  {row.Station}  {row.Barcode}  {row.Result}";

        var tile = StationTiles.FirstOrDefault(t => t.StationId == (e.Station?.Id ?? -1));
        tile?.RegisterScan(e.Timestamp, row.Result);

        if (e.Outcome is ScanOutcome.Accepted or ScanOutcome.Rejected)
        {
            // follow the live scan unless the operator has pinned a different unit
            if (SelectedPosition is null || SelectedPosition.Barcode == e.Barcode)
                LoadBarcode(e.Barcode);
            RefreshPositions();
        }
    }

    private void LoadBarcode(string barcode)
    {
        var current = _services.Trace.GetCurrent(barcode);
        History.Clear();

        if (current is null)
        {
            BarcodeFound = false;
            CurrentBarcode = barcode;
            CurrentStation = "-";
            CurrentStatus = "Not found";
            CurrentResult = "-";
            CurrentLastScan = "-";
            return;
        }

        BarcodeFound = true;
        CurrentBarcode = current.Barcode;
        CurrentStation = current.StationCode;
        CurrentStatus = current.Status.ToString();
        CurrentResult = current.Result.ToString();
        CurrentLastScan = current.LastScanAt.ToString("dd/MM/yyyy HH:mm:ss");

        foreach (var h in _services.Trace.GetHistory(barcode))
            History.Add(ScanRow.From(h));
    }

    public void Dispose()
    {
        _services.Coordinator.ScanProcessed -= OnScanProcessed;
        _services.Stations.Changed -= OnStationsChanged;
        _services.Trace.Changed -= OnTraceChanged;
    }
}
