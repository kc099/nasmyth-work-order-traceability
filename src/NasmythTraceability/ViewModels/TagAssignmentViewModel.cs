using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services;
using NasmythTraceability.Services.Rfid;

namespace NasmythTraceability.ViewModels;

/// <summary>
/// Tag Assignment. Works only with the work order assigning station's reader: a work order is
/// written to the next tag presented there, and once the reader confirms the write the tag
/// (by its uid) is assigned to the work order. The production station readers are not
/// involved and keep recording scans while this page is open. The page is locked until the
/// administrator password is entered.
/// </summary>
public sealed partial class TagAssignmentViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly Dispatcher? _dispatcher;
    private readonly DispatcherTimer? _timer;

    public TagAssignmentViewModel(AppServices services)
    {
        _services = services;
        _dispatcher = Application.Current?.Dispatcher;

        _services.Tags.Changed += OnDataChanged;
        _services.Trace.Changed += OnTraceChanged;
        _services.Readers.StatusChanged += OnReaderStatusChanged;
        _services.Settings.Changed += OnSettingsChanged;
        _services.TagWriter.Progress += OnWriteProgress;
        _services.TagWriter.CardSeen += OnCardSeen;

        if (_dispatcher is not null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Tick(DateTime.Now);
        }

        RefreshReaderStatus();
        Reload();
    }

    private void OnDataChanged(object? sender, EventArgs e) => OnUi(Reload);

    private void OnTraceChanged(object? sender, TraceChangedEventArgs e) => OnUi(Reload);

    // ---- the assignment station's reader --------------------------------
    [ObservableProperty] private string _readerText = "";
    [ObservableProperty] private string _readerStateKey = "";
    [ObservableProperty] private bool _isReaderConfigured;

    private void OnReaderStatusChanged(object? sender, ReaderStatusInfo e)
    {
        if (e.Endpoint.Key == ReaderEndpoint.AssignmentKey)
            OnUi(RefreshReaderStatus);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => OnUi(RefreshReaderStatus);

    private void RefreshReaderStatus()
    {
        var host = _services.Settings.Get(SettingsService.AssignmentReaderIp);
        var status = _services.Readers.AssignmentStatus;
        IsReaderConfigured = host.Length > 0;
        ReaderStateKey = status.StateKey;
        ReaderText = IsReaderConfigured
            ? $"Assignment station reader {host}: {status.StateText}"
            : "No work order assigning station is set up. Enter its reader's IP address in Settings > Stations.";
    }

    // ---- writing a work order to a tag ---------------------------------
    [ObservableProperty] private string _workOrder = "";

    /// <summary>Live check of what is typed: why it cannot be written, or that it is ready.</summary>
    [ObservableProperty] private string _workOrderHint = "";
    [ObservableProperty] private string _workOrderHintKey = "";

    partial void OnWorkOrderChanged(string value)
    {
        var order = TagService.NormalizeWorkOrder(value);
        if (order.Length == 0)
        {
            WorkOrderHint = "";
            WorkOrderHintKey = "";
            return;
        }

        var problem = TagService.ValidateWorkOrder(order, _services.Settings.GetInt(SettingsService.MinBarcodeLength, 4));
        WorkOrderHint = problem ?? $"{order} - {order.Length} of {TagService.MaxWorkOrderLength} characters.";
        WorkOrderHintKey = problem is null ? "OK" : "NG";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WriteTagCommand), nameof(CancelWriteCommand))]
    private bool _isWriting;

    /// <summary>"WARN" while waiting, "OK" after a write, "NG" after a failure; "" when idle.</summary>
    [ObservableProperty] private string _writeStateKey = "";
    [ObservableProperty] private string _writeMessage = "Enter a work order and press Write to tag.";
    [ObservableProperty] private string _countdownText = "";

    // Typing is allowed while locked; pressing Write explains that the page must be unlocked.
    private bool CanWrite() => !IsWriting;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task WriteTagAsync()
    {
        if (!IsUnlocked)
        {
            ShowResult("NG", "Tag Assignment is locked. Enter the administrator password at the top right, then press Write to tag again.");
            return;
        }

        var order = TagService.NormalizeWorkOrder(WorkOrder);

        if (!IsReaderConfigured)
        {
            ShowResult("NG", "Set up the work order assigning station first (Settings > Stations).");
            return;
        }

        var problem = TagService.ValidateWorkOrder(order, _services.Settings.GetInt(SettingsService.MinBarcodeLength, 4));
        if (problem is not null)
        {
            ShowResult("NG", problem);
            return;
        }

        // A finished work order has to be cleared in Scan Information before it can be run
        // again; a tag carrying it would only produce blocked scans.
        if (_services.Trace.GetCurrent(order) is { Status: TraceStatus.Completed })
        {
            ShowResult("NG", $"Work order {order} is already completed. Delete its scans in Scan Information to run it again.");
            return;
        }

        if (_services.Tags.GetByWorkOrder(order) is { } existing
            && !Confirm($"Work order {order} already has tag {existing.TagId}.\n\n" +
                        "Write it to a new tag? The old tag will be released."))
            return;

        WorkOrder = order;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(
            _services.Settings.GetInt(SettingsService.TagWriteTimeoutSeconds, 30), 5, 300));
        await _services.TagWriter.StartAsync(order, timeout);
    }

    [RelayCommand(CanExecute = nameof(IsWriting))]
    private Task CancelWriteAsync() => _services.TagWriter.CancelAsync();

    private void OnWriteProgress(object? sender, TagWriteProgressEventArgs e) => OnUi(() =>
    {
        switch (e.State)
        {
            case TagWriteState.Arming:
            case TagWriteState.Waiting:
                IsWriting = true;
                _timer?.Start();
                WriteStateKey = e.IsRetryable ? "NG" : "WARN";
                WriteMessage = e.Message;
                StatusMessage = e.IsRetryable ? "Write failed - present the tag again." : "Waiting for the tag...";
                break;

            case TagWriteState.Succeeded:
                StopWriting();
                WorkOrder = "";
                ShowResult("OK", e.Message);
                Reload();
                break;

            default: // Failed, TimedOut, Cancelled
                StopWriting();
                ShowResult(e.State == TagWriteState.Cancelled ? "" : "NG", e.Message);
                break;
        }

        UpdateCountdown(DateTime.Now);
    });

    private void StopWriting()
    {
        IsWriting = false;
        _timer?.Stop();
        CountdownText = "";
    }

    private void ShowResult(string key, string message)
    {
        WriteStateKey = key;
        WriteMessage = message;
        StatusMessage = message;
    }

    /// <summary>Once a second while a write is pending: timeout and countdown.</summary>
    public void Tick(DateTime now)
    {
        _services.TagWriter.CheckTimeout(now);
        UpdateCountdown(now);
    }

    private void UpdateCountdown(DateTime now)
    {
        var left = _services.TagWriter.Deadline is { } d ? (int)Math.Ceiling((d - now).TotalSeconds) : 0;
        CountdownText = IsWriting && left > 0 ? $"{left} s" : "";
    }

    // ---- a tag presented at the assignment station outside a write ------
    [ObservableProperty] private string _lastTagId = "";
    [ObservableProperty] private string _lastTagInfo = "Present a tag on the assignment reader to see what it carries.";

    private void OnCardSeen(object? sender, ReaderCardEvent e) => OnUi(() =>
    {
        LastTagId = e.Uid;
        var assigned = e.Uid.Length == 0 ? null : _services.Tags.GetByTag(e.Uid);
        var onCard = e.Ok
            ? (string.IsNullOrEmpty(e.Data) ? "The card is blank." : $"The card reads '{e.Data}'.")
            : $"The card could not be read: {e.Error}.";
        LastTagInfo = onCard + " " + (assigned is null
            ? "Not assigned to a work order."
            : $"Assigned to {assigned.WorkOrder} ({assigned.StatusText}{(assigned.StationCode.Length > 0 ? ", " + assigned.StationCode : "")}).");
    });

    // ---- existing assignments -----------------------------------------
    public ObservableCollection<TagAssignment> Assignments { get; } = new();
    [ObservableProperty] private TagAssignment? _selectedAssignment;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _statusMessage = "";

    partial void OnSearchChanged(string value) => Reload();

    [RelayCommand]
    private void Reload()
    {
        var keep = SelectedAssignment?.TagId;
        Assignments.Clear();
        foreach (var a in _services.Tags.GetAll(Search))
            Assignments.Add(a);
        SelectedAssignment = Assignments.FirstOrDefault(a => a.TagId == keep);
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void RemoveAssignment()
    {
        if (SelectedAssignment is not { } a)
        {
            StatusMessage = "Select an assignment in the list first.";
            return;
        }

        if (!Confirm($"Remove the link between tag {a.TagId} and work order {a.WorkOrder}?\n\n" +
                     "Scans already recorded for the work order are kept."))
            return;

        _services.Tags.Remove(a.TagId);
        StatusMessage = $"Tag {a.TagId} is no longer assigned.";
    }

    // ---- lock: assigning needs the administrator password --------------
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAssignmentCommand))]
    private bool _isUnlocked;

    /// <summary>Called as the page is opened and left. Leaving locks it and cancels a pending write.</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            RefreshReaderStatus();
            Reload(); // statuses move on while the page is closed
        }
        else
        {
            Lock();
        }
    }

    [RelayCommand]
    private void Unlock(PasswordBox? box)
    {
        var entered = box?.Password ?? "";
        box?.Clear();

        if (!_services.Settings.CheckPassword(entered))
        {
            StatusMessage = "Wrong password.";
            return;
        }

        IsUnlocked = true;
        StatusMessage = "Unlocked - enter a work order and press Write to tag.";
        if (!IsWriting)
            ShowResult("", "Enter a work order and press Write to tag.");
    }

    /// <summary>Locks the page again, cancelling a pending write so the reader is not left armed.</summary>
    [RelayCommand]
    public void Lock()
    {
        if (_services.TagWriter.IsWaiting)
            _ = _services.TagWriter.CancelAsync();

        if (!IsUnlocked)
            return;

        IsUnlocked = false;
        WorkOrder = "";
        StatusMessage = "Tag assignment locked.";
    }

    private void OnUi(Action action)
    {
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
            _dispatcher.BeginInvoke(action);
        else
            action();
    }

    private static bool Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _timer?.Stop();
        _services.Tags.Changed -= OnDataChanged;
        _services.Trace.Changed -= OnTraceChanged;
        _services.Readers.StatusChanged -= OnReaderStatusChanged;
        _services.Settings.Changed -= OnSettingsChanged;
        _services.TagWriter.Progress -= OnWriteProgress;
        _services.TagWriter.CardSeen -= OnCardSeen;
        SetActive(false);
    }
}
