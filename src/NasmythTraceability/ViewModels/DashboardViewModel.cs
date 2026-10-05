using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services;

namespace NasmythTraceability.ViewModels;

/// <summary>Live station monitoring: scan log, counters, work order lookup.</summary>
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

        RefreshPositions();

        _services.Coordinator.ScanProcessed += OnScanProcessed;
        _services.Stations.Changed += OnStationsChanged;
        _services.Trace.Changed += OnTraceChanged;
    }

    /// <summary>Trace data was deleted or cleared outside the scan flow - refresh every derived view.</summary>
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

    /// <summary>Re-reads the station list for the Scan Information station filter.</summary>
    private void RebuildStations()
    {
        PositionStationOptions.Clear();
        PositionStationOptions.Add("All stations");
        foreach (var s in _services.Stations.GetStations())
            PositionStationOptions.Add(s.Code);
        if (!PositionStationOptions.Contains(PositionStationFilter))
            PositionStationFilter = "All stations"; // setter re-runs RefreshPositions
    }

    // ---- collections ----------------------------------------------------
    public ObservableCollection<ScanRow> LiveScanLog { get; } = new();
    public ObservableCollection<ScanRow> History { get; } = new();

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

        RefreshExceptions();
    }

    // ---- invalid and repeat scans (Scan Information) --------------------
    // Taps that were not recorded as a station visit: tag not assigned, out of sequence,
    // blocked or already completed (invalid), and a work order tapped again where it is (repeat).
    public ObservableCollection<ScanLog> ScanExceptions { get; } = new();
    public string[] ExceptionKindOptions { get; } = { AllExceptionKinds, "Invalid", "Repeat" };
    private const string AllExceptionKinds = "Invalid and repeat";
    [ObservableProperty] private string _exceptionKind = AllExceptionKinds;

    partial void OnExceptionKindChanged(string value) => RefreshExceptions();

    /// <summary>Re-reads the invalid / repeat scans with the station filter and search of the page.</summary>
    [RelayCommand]
    private void RefreshExceptions()
    {
        var station = string.IsNullOrEmpty(PositionStationFilter) || PositionStationFilter == "All stations"
            ? null
            : PositionStationFilter;
        var kind = ExceptionKind is "Invalid" or "Repeat" ? ExceptionKind : null;

        ScanExceptions.Clear();
        foreach (var l in _services.Trace.GetScanExceptions(station, PositionSearch, kind))
            ScanExceptions.Add(l);
    }

    // ---- counters -----------------------------------------------------
    [ObservableProperty] private int _totalScans;
    [ObservableProperty] private int _okScans;
    [ObservableProperty] private int _ngScans;

    // ---- scan information ---------------------------------------------
    [ObservableProperty] private string _currentBarcode = "";
    [ObservableProperty] private string _currentStation = "-";
    [ObservableProperty] private string _currentStatus = "-";
    [ObservableProperty] private string _currentResult = "-";
    [ObservableProperty] private string _currentLastScan = "-";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteLastScanCommand), nameof(DeleteWorkOrderScansCommand))]
    private bool _barcodeFound;

    // ---- search ------------------------------------------------------
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _lastActivityText = "No scan data yet";

    public string OkRateText => TotalScans == 0 ? "0%" : ((double)OkScans / TotalScans).ToString("P0");

    // ---------------------------------------------------------------- commands

    // ---- search as you type ------------------------------------------
    private const int MinSuggestChars = 2;
    private const int MaxSuggestions = 10;
    private bool _suppressSuggestions;

    public ObservableCollection<WorkOrderSuggestion> Suggestions { get; } = new();
    [ObservableProperty] private WorkOrderSuggestion? _selectedSuggestion;
    [ObservableProperty] private bool _isSuggestionsOpen;

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressSuggestions)
            return;

        Suggestions.Clear();
        SelectedSuggestion = null;

        var term = value.Trim();
        if (term.Length >= MinSuggestChars)
            foreach (var s in _services.Trace.SearchWorkOrders(term, MaxSuggestions))
                Suggestions.Add(s);

        IsSuggestionsOpen = Suggestions.Count > 0;
    }

    /// <summary>Moves the highlight in the suggestion list (arrow keys); +1 down, -1 up.</summary>
    [RelayCommand]
    private void MoveSuggestion(int step)
    {
        if (Suggestions.Count == 0)
            return;

        IsSuggestionsOpen = true;
        var index = SelectedSuggestion is null ? -1 : Suggestions.IndexOf(SelectedSuggestion);
        index = SelectedSuggestion is null && step < 0
            ? Suggestions.Count - 1
            : Math.Clamp(index + step, 0, Suggestions.Count - 1);
        SelectedSuggestion = Suggestions[index];
    }

    [RelayCommand]
    private void CloseSuggestions() => IsSuggestionsOpen = false;

    /// <summary>Shows the chosen work order and puts its number in the search box.</summary>
    [RelayCommand]
    private void PickSuggestion(WorkOrderSuggestion? suggestion)
    {
        if (suggestion is null)
            return;

        _suppressSuggestions = true;
        SearchText = suggestion.WorkOrder;
        _suppressSuggestions = false;

        IsSuggestionsOpen = false;
        LoadBarcode(suggestion.WorkOrder);
    }

    [RelayCommand]
    private void Search()
    {
        // Enter on a highlighted suggestion picks it.
        if (IsSuggestionsOpen && SelectedSuggestion is not null)
        {
            PickSuggestion(SelectedSuggestion);
            return;
        }

        IsSuggestionsOpen = false;

        var term = SearchText.Trim();
        if (term.Length == 0)
            return;

        // An exact work order first, otherwise the most recent one matching by number or tag id.
        var match = _services.Trace.GetCurrent(term)?.Barcode
                    ?? _services.Trace.SearchWorkOrders(term, 1).FirstOrDefault()?.WorkOrder;

        LoadBarcode(match ?? term);
    }

    [RelayCommand]
    private void ClearLog() => LiveScanLog.Clear();

    // ---- correcting a mistake (administrator password) -----------------

    /// <summary>Removes the most recent scan of the shown work order, e.g. a tap at the wrong reader.</summary>
    [RelayCommand(CanExecute = nameof(BarcodeFound))]
    private void DeleteLastScan()
    {
        var order = CurrentBarcode;
        if (!BarcodeFound || History.Count == 0)
            return;

        var last = History[^1];
        if (!Views.PasswordDialog.Confirm(
                $"Delete the last scan of work order {order}?\n\n" +
                $"{last.Station} at {last.DateTimeText} ({last.Result}). " +
                "The work order goes back to where it was before that scan.", _services.Settings))
            return;

        _services.Trace.DeleteLastScan(order);
    }

    /// <summary>Removes every scan of the shown work order so it can be scanned again from the start.</summary>
    [RelayCommand(CanExecute = nameof(BarcodeFound))]
    private void DeleteWorkOrderScans()
    {
        var order = CurrentBarcode;
        if (!BarcodeFound)
            return;

        if (!Views.PasswordDialog.Confirm(
                $"Delete ALL {History.Count} scan(s) of work order {order}?\n\n" +
                "Its tag assignment is kept, so it can be scanned again from the first station. " +
                "This cannot be undone.", _services.Settings))
            return;

        _services.Trace.DeleteWorkOrder(order);
    }

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

        if (e.Outcome is ScanOutcome.Duplicate or ScanOutcome.Error)
            RefreshExceptions();

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
            // A work order with a tag but no scan yet is known, just not started.
            BarcodeFound = false;
            CurrentBarcode = barcode;
            CurrentStation = "-";
            CurrentStatus = _services.Tags.GetByWorkOrder(barcode) is null ? "Not found" : "Not scanned yet";
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
