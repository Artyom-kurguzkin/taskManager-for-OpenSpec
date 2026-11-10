namespace TaskManager.Services;

public enum ToastKind { Info, Success, Warning }

/// <summary>What the open worklog shows: which kinds of entry (notes, decisions, created and status events, links, subtask events, edits) and whose. Set from the side menu.</summary>
public sealed record WorklogFilters(bool Notes = true, bool Decisions = true, bool Events = true, bool Links = true, bool Subtasks = true, bool Edits = true, string Who = "all");

/// <summary>What the side menu can do for the open worklog: the entries of its day menu, for today.</summary>
public sealed record WorklogActions(Action AddNote, Action AddDecision, Action LinkDocument, Action LinkTest, Action LinkCommit, Action AddSubtask, Func<bool>? CanLinkTest = null, Func<bool>? CanLinkCommit = null, Action<DateTime>? JumpTo = null);

/// <summary>A day of the worklog feed as the side menu lists it: Date is DateTime.MinValue for the "Linked earlier" group; Count is the entries shown.</summary>
public sealed record WorklogDay(DateTime Date, string Label, int Count);

public sealed record Toast(int Id, string Text, ToastKind Kind);

/// <summary>A confirm or a one-line prompt; <see cref="Result"/> completes with the typed text / "yes", or null when cancelled.</summary>
public sealed record DialogRequest(string Title, string Text, string Yes, string No, bool IsPrompt, string Placeholder, TaskCompletionSource<string?> Result,
    IReadOnlyList<(string Value, string Label)>? Choices = null, string Value = "");   // Choices: a pick-one dialog, one button per choice, no OK; Value: a prompt's starting text

/// <summary>
/// App-wide dialogs and toasts, rendered by the Overlays component in the layout. Pages call
/// <see cref="Confirm"/>, <see cref="Prompt"/> and <see cref="Toast"/> and await the answer.
/// </summary>
public sealed class UiService
{
    private readonly List<Toast> toasts = [];
    private int nextToast;

    public event Action? Changed;

    // ── The worklog on screen: the side menu shows its filters and actions, and the feed reads the filters from here ──

    /// <summary>Fired when a worklog opens or closes, or its filters or people change; the side menu and the feed redraw.</summary>
    public event Action? WorklogChanged;

    /// <summary>The actions of the worklog on screen, or null when none is (the side menu then has no Worklog block).</summary>
    public WorklogActions? WorklogOpen { get; private set; }

    /// <summary>The window is too narrow for the split view (the list beside an open item is not shown): the side menu then keeps the list's options for the board.</summary>
    public bool NarrowWindow { get; private set; }

    public event Action? LayoutChanged;

    public void SetNarrowWindow(bool narrow)
    {
        if (narrow == NarrowWindow) return;
        NarrowWindow = narrow;
        LayoutChanged?.Invoke();
    }

    public WorklogFilters WorklogFilter { get; private set; } = new();

    /// <summary>The people and agents seen in the feed, for the "who" filter.</summary>
    public IReadOnlyList<string> WorklogPeople { get; private set; } = [];

    /// <summary>The days the feed shows, newest first, for the side menu's Days list.</summary>
    public IReadOnlyList<WorklogDay> WorklogDays { get; private set; } = [];

    public void SetWorklogDays(IReadOnlyList<WorklogDay> days)
    {
        if (days.SequenceEqual(WorklogDays)) return;
        WorklogDays = days;
        WorklogChanged?.Invoke();
    }

    /// <summary>A worklog came on screen. Filters start over unless another worklog was already open (the split view moving from one item to the next).</summary>
    public void OpenWorklog(WorklogActions actions)
    {
        if (WorklogOpen is null) WorklogFilter = new();
        WorklogOpen = actions;
        WorklogChanged?.Invoke();
    }

    /// <summary>The worklog that opened with <paramref name="actions"/> went away; ignored when another one has taken over since.</summary>
    public void CloseWorklog(WorklogActions actions)
    {
        if (!ReferenceEquals(WorklogOpen, actions)) return;
        (WorklogOpen, WorklogFilter, WorklogPeople, WorklogDays) = (null, new(), [], []);
        WorklogChanged?.Invoke();
    }

    public void FilterWorklog(WorklogFilters filters)
    {
        if (filters == WorklogFilter) return;
        WorklogFilter = filters;
        WorklogChanged?.Invoke();
    }

    public void SetWorklogPeople(IReadOnlyList<string> people)
    {
        if (people.SequenceEqual(WorklogPeople)) return;
        WorklogPeople = people;
        if (WorklogFilter.Who != "all" && !people.Contains(WorklogFilter.Who)) WorklogFilter = WorklogFilter with { Who = "all" };   // that person is not in this item's feed
        WorklogChanged?.Invoke();
    }

    public DialogRequest? Dialog { get; private set; }

    /// <summary>The message of the slow step running now (Overlays shows it with a spinner), or null.</summary>
    public string? BusyText { get; private set; }

    /// <summary>
    /// Runs a slow synchronous step (the OpenSpec CLI) behind a busy panel. The short delay lets the panel reach the
    /// screen first; its spinner is CSS, so it keeps turning while the step blocks .NET.
    /// </summary>
    public async Task<T> Busy<T>(string text, Func<T> work)
    {
        BusyText = text;
        Changed?.Invoke();
        await Task.Delay(50);   // ponytail: one render round trip; move work to Task.Run if steps ever need live progress
        try { return work(); }
        finally { BusyText = null; Changed?.Invoke(); }
    }

    public IReadOnlyList<Toast> Toasts { get { lock (toasts) return toasts.ToList(); } }

    public async Task<bool> Confirm(string title, string text, string yes = "OK", string no = "Cancel") =>
        await Show(new DialogRequest(title, text, yes, no, false, "", new())) is not null;

    public Task<string?> Prompt(string title, string placeholder, string action, string value = "") =>
        Show(new DialogRequest(title, "", action, "Cancel", true, placeholder, new(), Value: value));

    /// <summary>Asks the user to pick one of the choices; the picked value, or null when cancelled.</summary>
    public Task<string?> Choose(string title, string text, IReadOnlyList<(string Value, string Label)> choices) =>
        Show(new DialogRequest(title, text, "", "Cancel", false, "", new(), choices));

    /// <summary>Closes the open dialog: a value means OK (the prompt text, or "yes" for a confirm), null means cancel.</summary>
    public void Close(string? result)
    {
        DialogRequest? d = Dialog;
        Dialog = null;
        Changed?.Invoke();
        d?.Result.TrySetResult(result);
    }

    private DateTime lastToast;

    /// <summary>
    /// "Saved", after any write to the repository. Skipped when a more specific toast just went up (Created, Merged, …),
    /// and never shown twice at once.
    /// </summary>
    public void Saved()
    {
        lock (toasts) if ((DateTime.UtcNow - lastToast).TotalMilliseconds < 600 || toasts.Any(t => t.Text == "Saved")) return;
        Toast("Saved", ToastKind.Success);
    }

    public void Toast(string text, ToastKind kind = ToastKind.Info)
    {
        Toast t;
        lock (toasts)
        {
            if (text != "Saved") toasts.RemoveAll(x => x.Text == "Saved");   // the specific message replaces the plain one
            lastToast = DateTime.UtcNow;
            toasts.Add(t = new Toast(++nextToast, text, kind));
        }
        Changed?.Invoke();
        _ = Task.Delay(3500).ContinueWith(_ =>
        {
            lock (toasts) toasts.Remove(t);
            Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    private Task<string?> Show(DialogRequest d)
    {
        Dialog?.Result.TrySetResult(null);   // only one dialog at a time
        Dialog = d;
        Changed?.Invoke();
        return d.Result.Task;
    }
}
