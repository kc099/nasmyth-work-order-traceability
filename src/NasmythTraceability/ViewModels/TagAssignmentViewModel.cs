using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NasmythTraceability.Data;
using NasmythTraceability.Helpers;
using NasmythTraceability.Models;
using NasmythTraceability.Services;
using NasmythTraceability.Services.Scanning;

namespace NasmythTraceability.ViewModels;

/// <summary>
/// Tag Assignment. The page is locked until the administrator password is entered. While it
/// is open and unlocked the readers are in assignment mode - a tag that is tapped is not
/// tracked, its id is shown here so a work order can be entered for it.
/// </summary>
public sealed partial class TagAssignmentViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;

    public TagAssignmentViewModel(AppServices services)
    {
        _services = services;
        _services.Tags.Changed += OnDataChanged;
        _services.Trace.Changed += OnTraceChanged;
        Reload();
    }

    private void OnDataChanged(object? sender, EventArgs e) => Reload();

    private void OnTraceChanged(object? sender, TraceChangedEventArgs e) => Reload();

    // ---- the tag being assigned ---------------------------------------
    [ObservableProperty] private string _tagId = "";
    [ObservableProperty] private string _tagInfo = "Tap a tag on any reader, or type its id.";
    [ObservableProperty] private string _workOrder = "";
    [ObservableProperty] private string _statusMessage = "";

    partial void OnTagIdChanged(string value) => TagInfo = DescribeTag(value);

    // ---- existing assignments -----------------------------------------
    public ObservableCollection<TagAssignment> Assignments { get; } = new();
    [ObservableProperty] private TagAssignment? _selectedAssignment;
    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value) => Reload();

    // ---- lock: assigning needs the administrator password --------------
    private bool _isPageOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand), nameof(ClearCommand), nameof(RemoveAssignmentCommand))]
    private bool _isUnlocked;

    /// <summary>Called as the page is opened and left. Leaving always locks it again.</summary>
    public void SetActive(bool active)
    {
        _isPageOpen = active;
        if (active)
            Reload(); // statuses move on while the page is closed
        else
            Lock();
        ApplyAssignmentMode();
    }

    /// <summary>Readers feed this page only while it is open and unlocked; otherwise they track.</summary>
    private void ApplyAssignmentMode()
        => _services.Coordinator.ReadInterceptor = _isPageOpen && IsUnlocked ? OnTagRead : null;

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
        ApplyAssignmentMode();
        StatusMessage = "Unlocked - tap a tag on any reader.";
    }

    /// <summary>Locks the page again and clears what was being entered.</summary>
    [RelayCommand]
    public void Lock()
    {
        if (!IsUnlocked)
            return;

        IsUnlocked = false;
        ApplyAssignmentMode();
        TagId = "";
        WorkOrder = "";
        StatusMessage = "Tag assignment locked.";
    }

    private void OnTagRead(BarcodeScannedEventArgs e)
    {
        TagId = TagService.NormalizeTag(e.Barcode);
        StatusMessage = $"Tag {TagId} read. Enter its work order and press Assign.";
    }

    private string DescribeTag(string tagId)
    {
        var tag = TagService.NormalizeTag(tagId);
        if (tag.Length == 0)
            return "Tap a tag on any reader, or type its id.";

        var current = _services.Tags.GetByTag(tag);
        return current is null
            ? "This tag is not assigned to a work order."
            : $"Currently assigned to {current.WorkOrder} ({current.StatusText}).";
    }

    [RelayCommand]
    private void Reload()
    {
        var keep = SelectedAssignment?.TagId;
        Assignments.Clear();
        foreach (var a in _services.Tags.GetAll(Search))
            Assignments.Add(a);
        SelectedAssignment = Assignments.FirstOrDefault(a => a.TagId == keep);
        TagInfo = DescribeTag(TagId);
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void Assign()
    {
        var tag = TagService.NormalizeTag(TagId);
        var order = TagService.NormalizeWorkOrder(WorkOrder);

        if (tag.Length == 0)
        {
            StatusMessage = "Tap a tag on a reader first.";
            return;
        }

        var problem = TagService.ValidateWorkOrder(order,
            _services.Settings.GetInt(SettingsService.MinBarcodeLength, 4));
        if (problem is not null)
        {
            StatusMessage = problem;
            return;
        }

        // A finished or rejected work order has to be cleared in Scan Information before it
        // can be run again; assigning a tag to it would only produce blocked scans.
        var tracked = _services.Trace.GetCurrent(order);
        if (tracked is { Status: TraceStatus.Completed })
        {
            StatusMessage = $"Work order {order} is already completed. Delete its scans in Scan Information to run it again.";
            return;
        }

        var tagNow = _services.Tags.GetByTag(tag);
        if (tagNow is not null && tagNow.WorkOrder == order)
        {
            StatusMessage = $"Tag {tag} is already assigned to {order}.";
            return;
        }

        if (tagNow is { Status: TraceStatus.InProgress or TraceStatus.Rejected }
            && !Confirm($"Tag {tag} is in use by work order {tagNow.WorkOrder}, which is not completed yet.\n\n" +
                        $"Move the tag to {order}? {tagNow.WorkOrder} will be left without a tag."))
            return;

        var orderNow = _services.Tags.GetByWorkOrder(order);
        if (orderNow is not null && orderNow.TagId != tag
            && !Confirm($"Work order {order} already has tag {orderNow.TagId}.\n\nReplace it with tag {tag}?"))
            return;

        _services.Tags.Assign(tag, order);

        var first = _services.Stations.GetStations(includeDisabled: false).FirstOrDefault(s => !s.IsFinal);
        StatusMessage = $"Tag {tag} assigned to {order}." +
                        (first is null ? "" : $" Lock or leave this page, then tap the tag at {first.Code} to record its entry.");
        TagId = "";
        WorkOrder = "";
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private void Clear()
    {
        TagId = "";
        WorkOrder = "";
        StatusMessage = "";
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

    private static bool Confirm(string text)
        => MessageBox.Show(text, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _services.Tags.Changed -= OnDataChanged;
        _services.Trace.Changed -= OnTraceChanged;
        SetActive(false);
    }
}
