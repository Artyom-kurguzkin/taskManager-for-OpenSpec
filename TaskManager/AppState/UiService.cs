namespace TaskManager.AppState;

public enum ToastKind { Info, Success, Warning }

/// <summary>What the open worklog shows: which kinds of entry (notes, decisions, created and status events, links, subtask events, edits) and whose. Set from the side menu.</summary>
public sealed record WorklogFilters(bool Notes = true, bool Decisions = true, bool Events = true, bool Links = true, bool Subtasks = true, bool Edits = true, string Who = "all", bool Bugs = true)
{
    /// <summary>The kinds the pills of the side menu switch, in their order.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["Notes", "Decisions", "Bugs", "Events", "Links", "Subtasks", "Edits"];

    public bool Has(string kind) => kind switch
    {
        "Notes" => Notes, "Decisions" => Decisions, "Bugs" => Bugs, "Events" => Events, "Links" => Links, "Subtasks" => Subtasks, "Edits" => Edits,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public WorklogFilters With(string kind, bool on) => kind switch
    {
        "Notes" => this with { Notes = on }, "Decisions" => this with { Decisions = on }, "Bugs" => this with { Bugs = on }, "Events" => this with { Events = on },
        "Links" => this with { Links = on }, "Subtasks" => this with { Subtasks = on }, "Edits" => this with { Edits = on },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public bool AllKinds => Kinds.All(Has);

    /// <summary>Every kind on again; who stays.</summary>
    public WorklogFilters ShowAllKinds() => this with { Notes = true, Decisions = true, Bugs = true, Events = true, Links = true, Subtasks = true, Edits = true };

    /// <summary>
    /// A click on one kind's pill. With every kind on it shows only that one (the quick filter); otherwise it adds the kind or takes it out.
    /// Taking out the last one brings every kind back, so the feed is never empty.
    /// </summary>
    public WorklogFilters Click(string kind)
    {
        WorklogFilters next = AllKinds ? Kinds.Aggregate(this, (f, k) => f.With(k, k == kind)) : With(kind, !Has(kind));
        return Kinds.Any(next.Has) ? next : next.ShowAllKinds();
    }
}

/// <summary>What the side menu can do for the open worklog: the entries of its day menu, for today.</summary>
public sealed record WorklogActions(Action AddNote, Action AddDecision, Action LinkDocument, Action LinkTest, Action LinkCommit, Action AddSubtask, Func<bool>? CanLinkTest = null, Func<bool>? CanLinkCommit = null, Action<DateTime>? JumpTo = null, Action? AddBug = null, Action? JumpToDescription = null);

/// <summary>
/// A day of the worklog feed as the side menu lists it: Date is DateTime.MinValue for the "Linked earlier" group; Count is the entries shown.
/// <paramref name="Special"/> marks the two entries that are not days: <see cref="DescriptionEntry"/> (the item's description, the feed's last post; Count 0) and <see cref="BugsEntry"/> (the unresolved bugs).
/// </summary>
public sealed record WorklogDay(DateTime Date, string Label, int Count, string? Special = null)
{
    public const string DescriptionEntry = "description", BugsEntry = "bugs";
}

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

    /// <summary>Where each view was scrolled to (top, left) when the user left it, by repository and view: it comes back to the same spot (#wi202). Per window, never saved.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<(string Repo, string View), (double Top, double Left)> ScrollSpots { get; } = new();

    /// <summary>The tab the Settings page is on: "general" or an open project's folder (#wi160). Per window, never saved; a project no longer open is General again.</summary>
    public string SettingsTab { get; set; } = "general";

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

    // ── Examine: with the menu expanded a click on a work item looks at it (its metadata fills the menu) instead of opening it ──

    /// <summary>The side menu is expanded; set by the menu. Only then does a click on a card or row examine it.</summary>
    public bool MenuExpanded { get; private set; }

    /// <summary>The one work item being examined (its project and number), or null.</summary>
    public (string Root, int Id)? Examined { get; private set; }

    /// <summary>Fired when the examined item changes; the board redraws its marks. The menu follows <see cref="LayoutChanged"/>, fired with it.</summary>
    public event Action? ExamineChanged;

    public void SetMenuExpanded(bool expanded)
    {
        if (expanded == MenuExpanded) return;
        MenuExpanded = expanded;
        if (!expanded) Examine(null);   // no menu, nowhere to show the metadata
    }

    public void Examine((string Root, int Id)? item)
    {
        if (item == Examined) return;
        Examined = item;
        ExamineChanged?.Invoke();
        LayoutChanged?.Invoke();
    }

    public WorklogFilters WorklogFilter { get; private set; } = new();

    /// <summary>The people and agents seen in the feed, for the "who" filter.</summary>
    public IReadOnlyList<string> WorklogPeople { get; private set; } = [];

    /// <summary>The days the feed shows, newest first, for the side menu's Days list.</summary>
    public IReadOnlyList<WorklogDay> WorklogDays { get; private set; } = [];

    /// <summary>The days picked with Shift-click in the side menu's Days list (DateTime.MinValue is "Linked earlier"); the feed shows only those. Empty = every day. Per open worklog, never saved.</summary>
    public IReadOnlySet<DateTime> WorklogSelectedDays { get; private set; } = new HashSet<DateTime>();

    public void ToggleWorklogDay(DateTime day)
    {
        HashSet<DateTime> next = [.. WorklogSelectedDays];
        if (!next.Remove(day)) next.Add(day);
        WorklogSelectedDays = next;
        WorklogChanged?.Invoke();
    }

    public void ClearWorklogDays()
    {
        if (WorklogSelectedDays.Count == 0) return;
        WorklogSelectedDays = new HashSet<DateTime>();
        WorklogChanged?.Invoke();
    }

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
        WorklogSelectedDays = new HashSet<DateTime>();   // days belong to one item's feed
        WorklogOpen = actions;
        WorklogChanged?.Invoke();
    }

    /// <summary>The worklog that opened with <paramref name="actions"/> went away; ignored when another one has taken over since.</summary>
    public void CloseWorklog(WorklogActions actions)
    {
        if (!ReferenceEquals(WorklogOpen, actions)) return;
        (WorklogOpen, WorklogFilter, WorklogPeople, WorklogDays, WorklogSelectedDays) = (null, new(), [], [], new HashSet<DateTime>());
        WorklogChanged?.Invoke();
    }

    /// <summary>A click on a kind's pill in the Worklog block: see <see cref="WorklogFilters.Click"/>.</summary>
    public void ClickWorklogKind(string kind) => FilterWorklog(WorklogFilter.Click(kind));

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

    /// <summary>The confirm for removing a status (Settings, the board's column menu, the list's group menu): names how many open work items move to <paramref name="first"/>. True at once when none have it.</summary>
    public async Task<bool> ConfirmRemoveStatus(string status, int n, string first) =>
        n == 0 || await Confirm($"Remove {status}?", $"{n} work item{(n == 1 ? "" : "s")} {(n == 1 ? "has" : "have")} this status and move{(n == 1 ? "s" : "")} to {first}, the project's first status. Archived work items keep the name.", yes: "Remove");

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

    /// <summary>How long a toast stays up; <see cref="Timeout.InfiniteTimeSpan"/> keeps it (a test that asserts on toasts sets this, so a busy machine cannot expire one mid-test).</summary>
    public TimeSpan ToastLifetime { get; init; } = TimeSpan.FromMilliseconds(3500);

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
        if (ToastLifetime == Timeout.InfiniteTimeSpan) return;
        _ = Task.Delay(ToastLifetime).ContinueWith(_ =>
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
