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
/// <paramref name="Special"/> marks the one entry that is not a day: <see cref="DescriptionEntry"/> (the item's description, the feed's last post; Count 0).
/// </summary>
public sealed record WorklogDay(DateTime Date, string Label, int Count, string? Special = null)
{
    public const string DescriptionEntry = "description";
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

    /// <summary>One open worklog and what the side menu shows for it. It belongs to the pane its page sits in (null: the window is not split, or the page has no pane).</summary>
    private sealed class WorklogState(WorklogActions actions, Pane? pane)
    {
        public WorklogActions Actions { get; } = actions;
        public Pane? Pane { get; } = pane;
        public WorklogFilters Filter { get; set; } = new();
        public IReadOnlyList<string> People { get; set; } = [];
        public IReadOnlyList<WorklogDay> Days { get; set; } = [];
        public IReadOnlySet<DateTime> SelectedDays { get; set; } = new HashSet<DateTime>();
    }

    private readonly List<WorklogState> worklogs = [];

    /// <summary>The worklog the side menu speaks for: the one in the active pane (one with no pane counts for whichever is active), or none.</summary>
    private WorklogState? Current => worklogs.FirstOrDefault(w => w.Pane == Panes.Active) ?? worklogs.FirstOrDefault(w => w.Pane is null);

    private WorklogState? StateOf(WorklogActions? actions) => actions is null ? Current : worklogs.FirstOrDefault(w => ReferenceEquals(w.Actions, actions));

    /// <summary>The actions of the worklog in the active pane, or null when it has none (the side menu then has no Worklog block).</summary>
    public WorklogActions? WorklogOpen => Current?.Actions;

    /// <summary>The panes of this window (Alt+\ splits, Ctrl+W closes); one when not split.</summary>
    public Panes Panes { get; } = new();

    /// <summary>The window is too narrow for side-by-side panes: only the active one is shown.</summary>
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

    /// <summary>Ctrl+Shift+&lt;: the menu's collapse button, from the keyboard. Same state; the menu is told to draw.</summary>
    public void ToggleMenuExpanded()
    {
        SetMenuExpanded(!MenuExpanded);
        LayoutChanged?.Invoke();
    }

    public void Examine((string Root, int Id)? item)
    {
        if (item == Examined) return;
        Examined = item;
        ExamineChanged?.Invoke();
        LayoutChanged?.Invoke();
    }

    /// <summary>The filters of the worklog the side menu shows.</summary>
    public WorklogFilters WorklogFilter => Current?.Filter ?? new();

    /// <summary>The filters of the worklog opened with <paramref name="actions"/>: each pane's feed reads its own.</summary>
    public WorklogFilters WorklogFilterOf(WorklogActions actions) => StateOf(actions)?.Filter ?? new();

    /// <summary>The people and agents seen in the feed, for the "who" filter.</summary>
    public IReadOnlyList<string> WorklogPeople => Current?.People ?? [];

    /// <summary>The days the feed shows, newest first, for the side menu's Days list.</summary>
    public IReadOnlyList<WorklogDay> WorklogDays => Current?.Days ?? [];

    /// <summary>The days picked with Shift-click in the side menu's Days list (DateTime.MinValue is "Linked earlier"); the feed shows only those. Empty = every day. Per open worklog, never saved.</summary>
    public IReadOnlySet<DateTime> WorklogSelectedDays => Current?.SelectedDays ?? new HashSet<DateTime>();

    /// <summary>The days picked for the worklog opened with <paramref name="actions"/>.</summary>
    public IReadOnlySet<DateTime> WorklogSelectedDaysOf(WorklogActions actions) => StateOf(actions)?.SelectedDays ?? new HashSet<DateTime>();

    public void ToggleWorklogDay(DateTime day)
    {
        if (Current is not { } w) return;
        HashSet<DateTime> next = [.. w.SelectedDays];
        if (!next.Remove(day)) next.Add(day);
        w.SelectedDays = next;
        WorklogChanged?.Invoke();
    }

    public void ClearWorklogDays()
    {
        if (Current is not { SelectedDays.Count: > 0 } w) return;
        w.SelectedDays = new HashSet<DateTime>();
        WorklogChanged?.Invoke();
    }

    /// <summary>The worklog opened with <paramref name="from"/> publishes its days (the active pane's when null); a worklog that has gone is ignored.</summary>
    public void SetWorklogDays(IReadOnlyList<WorklogDay> days, WorklogActions? from = null)
    {
        if (StateOf(from) is not { } w || days.SequenceEqual(w.Days)) return;
        w.Days = days;
        WorklogChanged?.Invoke();
    }

    /// <summary>
    /// A worklog came on screen, in <paramref name="pane"/>. Filters start over unless a worklog of that pane was already open (the pane moving from one
    /// item to the next); the other panes' worklogs stay as they are.
    /// </summary>
    public void OpenWorklog(WorklogActions actions, Pane? pane = null)
    {
        WorklogState? before = worklogs.FirstOrDefault(w => w.Pane == pane);
        WorklogState state = new(actions, pane);   // days belong to one item's feed
        if (before is not null)
        {
            (state.Filter, state.People) = (before.Filter, before.People);
            worklogs.Remove(before);
        }
        worklogs.Add(state);
        WorklogChanged?.Invoke();
    }

    /// <summary>The worklog that opened with <paramref name="actions"/> went away; ignored when another one has taken over its place since.</summary>
    public void CloseWorklog(WorklogActions actions)
    {
        if (StateOf(actions) is not { } w) return;
        worklogs.Remove(w);
        WorklogChanged?.Invoke();
    }

    /// <summary>A click on a kind's pill in the Worklog block: see <see cref="WorklogFilters.Click"/>.</summary>
    public void ClickWorklogKind(string kind) => FilterWorklog(WorklogFilter.Click(kind));

    public void FilterWorklog(WorklogFilters filters)
    {
        if (Current is not { } w || filters == w.Filter) return;
        w.Filter = filters;
        WorklogChanged?.Invoke();
    }

    public void SetWorklogPeople(IReadOnlyList<string> people, WorklogActions? from = null)
    {
        if (StateOf(from) is not { } w || people.SequenceEqual(w.People)) return;
        w.People = people;
        if (w.Filter.Who != "all" && !people.Contains(w.Filter.Who)) w.Filter = w.Filter with { Who = "all" };   // that person is not in this item's feed
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
