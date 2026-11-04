using Features;
using Features.PublicDTOs;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>What the board/list is showing. Remembered per repository in the settings file.</summary>
public sealed record BoardOptions(
    string GroupBy = "status",     // status | feature | label | contributor | due | started
    string SortBy = "priority",    // priority | feature | who | modified | created | status | title | id | manual (my order)
    int SortDir = 1,
    string Who = "all",
    string Period = "all",         // due in: all | overdue | today | week | month | none
    string Category = "all",       // legacy; kept for deserialization, unused
    string Unit = "tasks",         // tasks | subtasks
    string View = "board",         // board | list
    bool Compact = false,          // the board: cards show only their title band
    bool ShowSubtasks = false,     // the list: every row shows its subtasks (otherwise only rows opened by their chevron)
    bool ShowHidden = false,       // include work items "deleted" (hidden) in the app
    bool AgentOnly = false,        // only work with unreviewed agent changes
    IReadOnlyList<string>? Hidden = null,   // ColumnKey of columns taken off the board
    IReadOnlyList<string>? Order = null,    // column and card (item:<id>) keys in the user's order; anything not listed keeps its usual place after them
    string? ListSettings = null);           // the list view's grid settings (column widths, order, sort, filters) as JSON

public sealed record BoardColumn(string Key, string Label, IReadOnlyList<WorkItem> Items, IReadOnlyList<string> Lanes);

public sealed record BoardSection(string? Category, IReadOnlyList<BoardColumn> Columns);

/// <summary>
/// One row of the list view: a work item in one of the board's columns (an item in several features appears once per column).
/// An empty column gets one row with no item, so the column still shows as a group. The flat properties feed the grid's
/// sorting and filtering.
/// </summary>
public sealed record GridRow(int Group, BoardColumn Column, string? Category, WorkItem? Item, int Position, string Feature, string Root = "", string Project = "")
{
    public string Key => $"{Group}:{Item?.Id ?? 0}";
    public int Id => Item?.Id ?? 0;
    public string Title => Item?.Title ?? "";
    public string Status => Item?.Status ?? "";
    public string Label => Item?.Label ?? "";
    public string Contributors => Item is null ? "" : string.Join(", ", Item.Contributors);
    public string Priority => Item?.Priority ?? "";
    public int Progress => Item is { Subtasks.Count: > 0 } i ? i.Subtasks.Count(s => s.Done) * 100 / i.Subtasks.Count : 0;
    public DateTime? Modified => Item?.Modified;
    public DateTime? Started => Item?.Started?.ToDateTime(TimeOnly.MinValue);
    public DateTime? Due => Item?.Due?.ToDateTime(TimeOnly.MinValue);
}

/// <summary>Pure arrangement of work items into board sections and columns (no IO, so it is unit-tested directly).</summary>
public static class BoardModel
{
    public static IReadOnlyList<BoardSection> Build(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o, DateOnly? today = null)
    {
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Today);
        List<WorkItem> visible = Sort(items.Where(i => (o.Who == "all" || i.Contributors.Contains(o.Who)) && (!o.AgentOnly || AgentFlag(i)) && InPeriod(i.Due, o.Period, now)), o).ToList();
        BoardColumn Col(string key, string label, Func<WorkItem, bool> match) =>
            new(key, label, visible.Where(match).ToList(), lanes.TryGetValue(key, out IReadOnlyList<string>? l) ? l : []);

        switch (o.GroupBy)
        {
            case "contributor":
                List<BoardColumn> people = [];
                if (visible.Where(i => i.Contributors.Count == 0).ToList() is { Count: > 0 } nobody) people.Add(new(Unassigned, "No contributors", nobody, []));
                people.AddRange(Arrange(items.SelectMany(i => i.Contributors).Distinct().Order(StringComparer.OrdinalIgnoreCase), p => ColumnKey(o, p), o)
                    .Select(p => Col(p, p, i => i.Contributors.Contains(p))));
                return [new(null, people)];
            case "feature":   // one flat row: work items without a feature first, then every feature (no supergroups)
                List<BoardColumn> featureCols = [];
                if (visible.Where(i => i.Features.Count == 0).ToList() is { Count: > 0 } loose) featureCols.Add(new(Unassigned, "Unassigned", loose, []));
                featureCols.AddRange(Arrange(features, f => FeatureKey(f.Path), o).Select(f => Col(f.Path, f.Name, i => i.Features.Any(x => x.Path == f.Path))));
                return [new(null, featureCols)];
            case "label":
                List<BoardColumn> labelCols = [];
                if (visible.Where(i => string.IsNullOrEmpty(i.Label)).ToList() is { Count: > 0 } unlabeled)
                    labelCols.Add(new(Unassigned, "Unlabeled", unlabeled, []));
                labelCols.AddRange(Arrange(WorkItemsApi.Labels, l => "label:" + l, o)
                    .Select(l => Col(l, l, i => i.Label == l)));
                return [new(null, labelCols)];
            case "due":
                return [new(null, Arrange(DuePeriods, p => ColumnKey(o, p.Key), o).Select(p => Col(p.Key, p.Label, i => Period(i.Due, now) == p.Key)).ToList())];
            case "started":
                return [new(null, Arrange(StartPeriods, p => ColumnKey(o, p.Key), o).Select(p => Col(p.Key, p.Label, i => StartPeriod(i.Started, now) == p.Key)).ToList())];
            default:
                return [new(null, Arrange(WorkItemsApi.Statuses, s => ColumnKey(o, s), o).Select(s => Col(s, s, i => i.Status == s)).ToList())];
        }
    }

    /// <summary>The list view's rows, in board order: column by column, each column's items in their sorted order.</summary>
    public static IReadOnlyList<GridRow> Rows(IReadOnlyList<BoardSection> sections, IReadOnlyDictionary<string, string> featureNames,
        string root = "", string project = "", int firstGroup = 0)
    {
        List<GridRow> rows = [];
        int group = firstGroup, position = 0;
        foreach (BoardSection s in sections)
        foreach (BoardColumn c in s.Columns)
        {
            if (c.Items.Count == 0) rows.Add(new(group, c, s.Category, null, position++, "", root, project));
            foreach (WorkItem it in c.Items)
                rows.Add(new(group, c, s.Category, it, position++, it.Features.FirstOrDefault() is { } f ? featureNames.GetValueOrDefault(f.Path, f.Path) : "", root, project));
            group++;
        }
        return rows;
    }

    /// <summary>The list column that repeats the grouping (every row of a group has the same value there), or null.</summary>
    public static string? GroupedColumn(BoardOptions o) => o.GroupBy switch
    {
        "status" => "Status",
        "label" => "Label",
        "feature" => "Feature",
        "contributor" => "Contributors",
        "due" => "Due",
        "started" => "Started",
        _ => null,
    };

    // ── Time periods (due and start dates) ──
    public static readonly IReadOnlyList<(string Key, string Label)> DuePeriods =
        [("overdue", "Overdue"), ("today", "Today"), ("week", "This week"), ("next", "Next week"), ("later", "Later"), ("none", "No due date")];
    public static readonly IReadOnlyList<(string Key, string Label)> StartPeriods =
        [("earlier", "Earlier"), ("week", "This week"), ("next", "Next week"), ("later", "Later"), ("none", "Not started")];

    /// <summary>Which due period a day falls in, seen from today: overdue | today | week (to Sunday) | next (next week) | later | none.</summary>
    public static string Period(DateOnly? day, DateOnly today)
    {
        if (day is not DateOnly d) return "none";
        DateOnly sunday = CalendarModel.Monday(today).AddDays(6);
        return d < today ? "overdue" : d == today ? "today" : d <= sunday ? "week" : d <= sunday.AddDays(7) ? "next" : "later";
    }

    /// <summary>Which period work started in: earlier (before this week) | week | next | later | none.</summary>
    public static string StartPeriod(DateOnly? day, DateOnly today)
    {
        if (day is not DateOnly d) return "none";
        DateOnly monday = CalendarModel.Monday(today);
        return d < monday ? "earlier" : d < monday.AddDays(7) ? "week" : d < monday.AddDays(14) ? "next" : "later";
    }

    /// <summary>The side menu's period filter, on the due date: all | overdue | today | week | month | none.</summary>
    public static bool InPeriod(DateOnly? due, string period, DateOnly today) => period switch
    {
        "overdue" => due < today,
        "today" => due == today,
        "week" => due >= today && due <= CalendarModel.Monday(today).AddDays(6),
        "month" => due >= today && due <= new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
        "none" => due is null,
        _ => true,
    };

    /// <summary>An agent changed this item, or added one of its subtasks, and the user has not reviewed it.</summary>
    public static bool AgentFlag(WorkItem it) => it.AgentUnreviewed || it.Subtasks.Any(s => s.Agent is not null);

    /// <summary>Key of a list column (Title, Status, …) in the hidden list, next to the board columns' keys.</summary>
    public static string ListColumnKey(string title) => "col:" + title;

    /// <summary>Column key for work items without a feature (grouping by feature) or without contributors (by contributor).</summary>
    public const string Unassigned = "";

    public static string FeatureKey(string path) => "feature:" + path;

    /// <summary>Key of a column under the current grouping, for the saved order / hidden lists.</summary>
    public static string ColumnKey(BoardOptions o, string column) => o.GroupBy switch
    {
        "feature" => FeatureKey(column),
        "label" => "label:" + column,
        "contributor" => "who:" + column,
        "due" => "due:" + column,
        "started" => "started:" + column,
        _ => "status:" + column,
    };

    /// <summary>Readable name of a saved key (for the side menu's list of hidden columns).</summary>
    public static string KeyLabel(string key, IReadOnlyList<Feature> features) => key.Split(':', 2) switch
    {
        ["feature", string path] => features.FirstOrDefault(f => f.Path == path)?.Name ?? path,
        ["due", string p] => DuePeriods.FirstOrDefault(x => x.Key == p).Label ?? p,
        ["started", string p] => StartPeriods.FirstOrDefault(x => x.Key == p).Label ?? p,
        [_, string name] => name,
        _ => key,
    };

    /// <summary>How many items (or subtasks, per the unit) each hidden column would hold, under the current filters.</summary>
    public static Dictionary<string, int> HiddenCounts(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o)
    {
        Dictionary<string, int> counts = [];
        foreach (BoardColumn col in Build(items, features, lanes, o with { Hidden = null }).SelectMany(s => s.Columns))
            if (IsHidden(o, ColumnKey(o, col.Key))) counts[ColumnKey(o, col.Key)] = Count(col.Items, o.Unit);
        return counts;
    }

    public static bool IsHidden(BoardOptions o, string key) => o.Hidden?.Contains(key) ?? false;

    public static BoardOptions ToggleHidden(BoardOptions o, string key) =>
        o with { Hidden = IsHidden(o, key) ? o.Hidden!.Where(k => k != key).ToList() : [.. o.Hidden ?? [], key] };

    /// <summary>The user's order first, then the rest in their usual order; hidden ones dropped unless asked for.</summary>
    public static IEnumerable<T> Arrange<T>(IEnumerable<T> items, Func<T, string> key, BoardOptions o, bool includeHidden = false)
    {
        List<string> order = o.Order?.ToList() ?? [];
        return items.Where(x => includeHidden || !IsHidden(o, key(x)))
            .Select((x, i) => (x, i))
            .OrderBy(t => order.IndexOf(key(t.x)) is int p and >= 0 ? p : order.Count + t.i)
            .Select(t => t.x);
    }

    /// <summary>
    /// Drag-and-drop: <paramref name="moving"/> keys land together, in their given order, in front of <paramref name="before"/>
    /// among <paramref name="siblings"/> (given in their shown order), or at the end when it is null.
    /// Returns <paramref name="o"/> itself when nothing would change.
    /// </summary>
    public static BoardOptions Insert(BoardOptions o, IReadOnlyList<string> siblings, IReadOnlyList<string> moving, string? before)
    {
        List<string> list = siblings.Except(moving).ToList();
        int at = before is null ? list.Count : list.IndexOf(before);
        if (at < 0) return o;   // dropped in front of itself, or of a column it doesn't share a row with
        list.InsertRange(at, moving);
        return list.SequenceEqual(siblings) ? o : o with { Order = [.. (o.Order ?? []).Except(list), .. list] };
    }

    /// <summary>Key of a card in the saved order (same list as column keys; the prefixes never collide).</summary>
    public static string ItemKey(int id) => "item:" + id;

    /// <summary>Cards dropped into <paramref name="column"/> (ids in their shown order) at the gap; any change switches the sort to "my order".</summary>
    public static BoardOptions PlaceCards(BoardOptions o, IReadOnlyList<int> column, IReadOnlyList<int> moving, int? before) =>
        Insert(o, [.. column.Select(ItemKey)], [.. moving.Select(ItemKey)], before is int b ? ItemKey(b) : null) is var placed && !ReferenceEquals(placed, o)
            ? placed with { SortBy = "manual", SortDir = 1 }
            : o;

    /// <summary>
    /// Arrow key: the selected cards (<paramref name="moving"/>, a subset of <paramref name="column"/>) move one step up (-1) or down (+1).
    /// A scattered selection first gathers into a block, at its top card going up or its bottom card going down.
    /// </summary>
    public static BoardOptions Step(BoardOptions o, IReadOnlyList<int> column, IReadOnlyList<int> moving, int dir)
    {
        List<int> all = column.ToList(), block = all.Where(moving.Contains).ToList();
        if (block.Count == 0) return o;
        int top = all.IndexOf(block[0]), bottom = all.IndexOf(block[^1]);
        bool together = bottom - top + 1 == block.Count;
        List<int> others = column.Except(block).ToList();
        int at = !together ? (dir < 0 ? top : bottom - block.Count + 1)   // gather: the block starts where its top (or ends where its bottom) card is
               : Math.Clamp(top + dir, 0, others.Count);
        return PlaceCards(o, column, block, at < others.Count ? others[at] : null);
    }

    /// <summary>Items in one subgroup of a column; items whose stored lane is unknown fall into the first lane.</summary>
    public static IEnumerable<WorkItem> InLane(BoardColumn col, string lane) =>
        col.Items.Where(i => (i.Lanes.TryGetValue(col.Key, out string? l) && col.Lanes.Contains(l) ? l : col.Lanes[0]) == lane);

    /// <summary>
    /// A column's cards under automatic priority headings, highest first: one lane per priority present ("high", then everything else as "low").
    /// A single lane means the cards all share one priority, and the board draws no heading for it.
    /// </summary>
    public static IReadOnlyList<(string Priority, string Label, IReadOnlyList<WorkItem> Items)> PriorityLanes(BoardColumn col) =>
        [.. new[] { ("high", "High priority"), ("low", "Lower priority") }
            .Select(p => (p.Item1, p.Item2, (IReadOnlyList<WorkItem>)col.Items.Where(i => (i.Priority == "high" ? "high" : "low") == p.Item1).ToList()))
            .Where(l => l.Item3.Count > 0)];

    public static int Count(IEnumerable<WorkItem> items, string unit) => unit == "subtasks" ? items.Sum(i => i.Subtasks.Count) : items.Count();

    public static IEnumerable<WorkItem> Sort(IEnumerable<WorkItem> items, BoardOptions o)
    {
        Comparison<WorkItem> byPriority = (a, b) => (a.Priority == b.Priority ? 0 : a.Priority == "high" ? -1 : 1) is int p && p != 0 ? p : b.Modified.CompareTo(a.Modified);
        List<string> placed = o.Order?.ToList() ?? [];   // ponytail: IndexOf per compare, a dictionary if boards reach thousands of cards
        int Rank(WorkItem w) => placed.IndexOf(ItemKey(w.Id)) is int r and >= 0 ? r : int.MaxValue;
        Comparison<WorkItem> cmp = o.SortBy switch
        {
            "feature" => (a, b) => string.CompareOrdinal(a.Features.FirstOrDefault()?.Path ?? "~", b.Features.FirstOrDefault()?.Path ?? "~"),
            "label" => (a, b) => string.Compare(a.Label ?? "~", b.Label ?? "~", StringComparison.OrdinalIgnoreCase),
            "who" => (a, b) => string.Compare(a.Contributors.FirstOrDefault() ?? "~", b.Contributors.FirstOrDefault() ?? "~", StringComparison.OrdinalIgnoreCase),
            "modified" => (a, b) => b.Modified.CompareTo(a.Modified),
            "created" => (a, b) => b.Created.CompareTo(a.Created),
            "status" => (a, b) => IndexOf(a.Status).CompareTo(IndexOf(b.Status)),
            "title" => (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
            "id" => (a, b) => a.Id.CompareTo(b.Id),
            "manual" => (a, b) => Rank(a).CompareTo(Rank(b)) is int r && r != 0 ? r : byPriority(a, b),
            _ => byPriority,
        };
        List<WorkItem> list = items.ToList();
        list.Sort((a, b) => (cmp(a, b) is int c && c != 0 ? c : a.Id.CompareTo(b.Id)) * o.SortDir);
        return list;
    }

    private static int IndexOf(string status) => WorkItemsApi.Statuses is IReadOnlyList<string> s ? s.ToList().IndexOf(status) : 0;
}
