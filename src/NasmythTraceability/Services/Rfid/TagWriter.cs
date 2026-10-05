using NasmythTraceability.Models;

namespace NasmythTraceability.Services.Rfid;

public enum TagWriteState
{
    Idle,

    /// <summary>Sending the write request to the assignment reader.</summary>
    Arming,

    /// <summary>The reader is armed; waiting for the tag to be presented.</summary>
    Waiting,

    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
}

public sealed class TagWriteProgressEventArgs : EventArgs
{
    public required TagWriteState State { get; init; }
    public string WorkOrder { get; init; } = "";
    public string Uid { get; init; } = "";
    public required string Message { get; init; }

    /// <summary>True for a failed attempt while the reader stays armed (the operator can try again).</summary>
    public bool IsRetryable { get; init; }
}

/// <summary>
/// Writes a work order to a tag on the work order assigning station and records the tag
/// (by uid) against the work order once the reader confirms the write (README section 5):
/// arm a one-shot write, wait for a <c>write</c> event newer than the arm, then assign.
/// A failed attempt leaves the reader armed so the tag can be presented again; a timeout,
/// cancel or leaving the page puts the reader back in read mode.
/// </summary>
public sealed class TagWriter : IDisposable
{
    private readonly NetworkReaderService _readers;
    private readonly TagService _tags;
    private readonly TraceService _trace;
    private readonly object _lock = new();
    private readonly List<ReaderCardEvent> _early = new();

    private TagWriteState _state = TagWriteState.Idle;
    private string _workOrder = "";
    private long _armedAfterId;
    private TimeSpan _timeout;

    // The last request putting the reader back in read mode; closing the app waits for it.
    private Task _disarm = Task.CompletedTask;

    public TagWriter(NetworkReaderService readers, TagService tags, TraceService trace)
    {
        _readers = readers;
        _tags = tags;
        _trace = trace;
        _readers.AssignmentCardEvent += OnCardEvent;
        _readers.AssignmentReaderRestarted += OnReaderRestarted;
        _readers.AssignmentWriteAllowed = () => IsWaiting;
    }

    /// <summary>Every step of a write: armed, failed attempt, done, timed out...</summary>
    public event EventHandler<TagWriteProgressEventArgs>? Progress;

    /// <summary>A card was presented at the assignment station outside a write (read mode).</summary>
    public event EventHandler<ReaderCardEvent>? CardSeen;

    public TagWriteState State
    {
        get { lock (_lock) return _state; }
    }

    public bool IsWaiting => State is TagWriteState.Arming or TagWriteState.Waiting;

    public string PendingWorkOrder
    {
        get { lock (_lock) return IsWaitingLocked ? _workOrder : ""; }
    }

    /// <summary>When the pending write gives up; null when nothing is pending.</summary>
    public DateTime? Deadline { get; private set; }

    private bool IsWaitingLocked => _state is TagWriteState.Arming or TagWriteState.Waiting;

    /// <summary>
    /// Arms the assignment reader to write <paramref name="workOrder"/> to the next tag presented.
    /// Returns false (with a <see cref="Progress"/> message) when the reader could not be armed.
    /// </summary>
    public async Task<bool> StartAsync(string workOrder, TimeSpan timeout)
    {
        lock (_lock)
        {
            if (IsWaitingLocked)
                return false;
            _state = TagWriteState.Arming;
            _workOrder = workOrder;
            _timeout = timeout;
            _early.Clear();
            Deadline = null;
        }

        Raise(TagWriteState.Arming, $"Sending {workOrder} to the assignment reader...");

        RfidStatusDto reply;
        try
        {
            reply = await _readers.SetAssignmentModeAsync(RfidModeRequest.WriteOnce(workOrder)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (_state != TagWriteState.Arming)
                    return false; // cancelled meanwhile
                _state = TagWriteState.Failed;
            }

            Raise(TagWriteState.Failed, "Could not reach the assignment reader: " + RfidReaderClient.Describe(ex));
            return false;
        }

        List<ReaderCardEvent> early;
        lock (_lock)
        {
            if (_state != TagWriteState.Arming)
            {
                // Cancelled while the request was on its way: do not leave the reader armed.
                _disarm = SendReadAsync();
                return false;
            }

            _armedAfterId = reply.LastEventId;
            _state = TagWriteState.Waiting;
            Deadline = DateTime.Now + timeout;
            early = _early.ToList();
            _early.Clear();
        }

        Raise(TagWriteState.Waiting, $"Present the tag on the assignment reader to write {workOrder}.");

        // A write event polled before the reply was handled.
        foreach (var e in early)
            OnCardEvent(this, e);
        return true;
    }

    /// <summary>Cancels a pending write and puts the reader back in read mode.</summary>
    public Task CancelAsync() => StopAsync(TagWriteState.Cancelled, "Write cancelled. The assignment reader is back in read mode.");

    /// <summary>Called regularly (once a second) to apply the timeout.</summary>
    public void CheckTimeout(DateTime now)
    {
        if (State == TagWriteState.Waiting && Deadline is { } d && now >= d)
            _ = StopAsync(TagWriteState.TimedOut,
                "No tag was presented in time. The write was cancelled and the reader is back in read mode.");
    }

    private async Task StopAsync(TagWriteState final, string message)
    {
        string order;
        lock (_lock)
        {
            if (!IsWaitingLocked)
                return;
            order = _workOrder;
            _state = final;
            Deadline = null;
        }

        Raise(final, message, order);
        _disarm = SendReadAsync();
        await _disarm.ConfigureAwait(false);
    }

    private async Task SendReadAsync()
    {
        try
        {
            await _readers.SetAssignmentModeAsync(RfidModeRequest.Read()).ConfigureAwait(false);
        }
        catch
        {
            // Offline: the poller switches it back to read mode when it is reachable again,
            // because no write is pending any more.
        }
    }

    private enum EventOutcome { NotOurs, Retry, Mismatch, Written }

    private void OnCardEvent(object? sender, ReaderCardEvent e)
    {
        string order;
        EventOutcome outcome;

        lock (_lock)
        {
            order = _workOrder;
            if (_state == TagWriteState.Arming && e.IsWrite)
            {
                _early.Add(e);
                return;
            }

            if (_state != TagWriteState.Waiting || !e.IsWrite || e.Id <= _armedAfterId)
                outcome = EventOutcome.NotOurs;
            else if (!e.Ok)
                outcome = EventOutcome.Retry;
            else
            {
                outcome = string.Equals(e.Data.Trim(), order, StringComparison.Ordinal)
                    ? EventOutcome.Written
                    : EventOutcome.Mismatch;
                _state = outcome == EventOutcome.Written ? TagWriteState.Succeeded : TagWriteState.Failed;
                Deadline = null;
            }
        }

        switch (outcome)
        {
            case EventOutcome.NotOurs:
                CardSeen?.Invoke(this, e);
                break;

            case EventOutcome.Retry:
                // The reader stays armed after a failed attempt (README section 5).
                Progress?.Invoke(this, new TagWriteProgressEventArgs
                {
                    State = TagWriteState.Waiting,
                    WorkOrder = order,
                    Uid = e.Uid,
                    IsRetryable = true,
                    Message = $"Write to tag {e.Uid} failed: {e.Error}. Present the tag again (MIFARE Classic cards only).",
                });
                break;

            case EventOutcome.Mismatch:
                _disarm = SendReadAsync();
                Raise(TagWriteState.Failed,
                    $"Tag {e.Uid} reads back '{e.Data}' instead of {order}. Nothing was assigned - write it again.",
                    order, e.Uid);
                break;

            case EventOutcome.Written:
                Complete(e, order);
                break;
        }
    }

    private void Complete(ReaderCardEvent e, string order)
    {
        // The card now physically carries the work order, so the assignment follows the card.
        var before = _tags.GetByTag(e.Uid);
        var replacedTag = _tags.GetByWorkOrder(order) is { } o && o.TagId != e.Uid ? o.TagId : null;

        try
        {
            _tags.Assign(e.Uid, order);
        }
        catch (Exception ex)
        {
            lock (_lock)
                _state = TagWriteState.Failed;
            Raise(TagWriteState.Failed, $"Tag {e.Uid} was written but could not be saved: {ex.Message}", order, e.Uid);
            return;
        }

        var message = $"Tag {e.Uid} now carries work order {order}.";
        if (before is not null && before.WorkOrder != order)
            message += before.Status is TraceStatus.InProgress or TraceStatus.Rejected
                ? $" It was in use by {before.WorkOrder} ({before.StatusText}), which now has no tag."
                : $" It previously carried {before.WorkOrder}.";
        if (replacedTag is not null)
            message += $" Its old tag {replacedTag} is released.";

        _trace.LogScan(order, null, "ASSIGN", e.Mac, e.Device, ScanLogType.Info,
            $"Tag {e.Uid} written and assigned to {order}", e.Timestamp);

        Raise(TagWriteState.Succeeded, message, order, e.Uid);
    }

    private void OnReaderRestarted(object? sender, EventArgs e)
    {
        string order;
        lock (_lock)
        {
            if (_state != TagWriteState.Waiting)
                return;
            order = _workOrder;
            _state = TagWriteState.Idle;
        }

        // The restart dropped the armed write; arm it again with the time that was left.
        var left = Deadline is { } d ? d - DateTime.Now : _timeout;
        _ = StartAsync(order, left > TimeSpan.FromSeconds(5) ? left : TimeSpan.FromSeconds(5));
    }

    private void Raise(TagWriteState state, string message, string? order = null, string uid = "")
        => Progress?.Invoke(this, new TagWriteProgressEventArgs
        {
            State = state,
            WorkOrder = order ?? _workOrder,
            Uid = uid,
            Message = message,
        });

    public void Dispose()
    {
        _readers.AssignmentCardEvent -= OnCardEvent;
        _readers.AssignmentReaderRestarted -= OnReaderRestarted;
        _readers.AssignmentWriteAllowed = null;

        // Never leave the reader armed when the app closes, including a cancel that is still on
        // its way (leaving Tag Assignment cancels just before this). Nothing here needs the UI thread.
        if (IsWaiting)
            _ = CancelAsync();
        try { _disarm.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
    }
}
