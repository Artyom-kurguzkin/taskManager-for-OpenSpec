using Features;
using Features.PublicDTOs;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>What the board/list is showing. Remembered per repository in the settings file.</summary>
public sealed record BoardOptions(
    string GroupBy = "status",     // status | bucket | label | contributor
    string SortBy = "priority",    // priority | feature | who | modified | created | status | title | id | manual (my order)
    int SortDir = 1,
    string Who = "all",
    string Category = "all",       // legacy; kept for deserialization but unused by bucket grouping
    string Unit = "tasks",         // tasks | subtasks
    string View = "board",         // board | list
    bool Compact = false,
    bool ShowHidden = false,       // include work items "deleted" (hidden) in the app
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
public sealed record GridRow(int Group, BoardColumn Column, string? Category, WorkItem? Item, int Position, string Features)
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
}

/// <summary>Pure arrangement of work items into board sections and columns (no IO, so it is unit-tested directly).</summary>
public static class BoardModel
{
    public static IReadOnlyList<BoardSection> Build(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o)
    {
        List<WorkItem> visible = Sort(items.Where(i => o.Who == "all" || i.Contributors.Contains(o.Who)), o).ToList();
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
            case "bucket":
            case "feature":   // legacy saved setting
                List<BoardSection> sections = [];
                if (visible.Where(i => i.Features.Count == 0).ToList() is { Count: > 0 } loose)
                    sections.Add(new(null, [new(Unassigned, "Unassigned", loose, [])]));
                foreach (string bucket in Arrange(Categories(features), b => "bucket:" + b, o))
                {
                    List<BoardColumn> cols = Arrange(features.Where(f => f.Group == bucket), f => FeatureKey(f.Path), o)
                        .Select(f => Col(f.Path, f.Name, i => i.Features.Any(x => x.Path == f.Path))).ToList();
                    if (cols.Count > 0) sections.Add(new(bucket, cols));
                }
                return sections.Count > 0 ? sections : [new(null, [])];
            case "label":
                List<BoardColumn> labelCols = [];
                if (visible.Where(i => string.IsNullOrEmpty(i.Label)).ToList() is { Count: > 0 } unlabeled)
                    labelCols.Add(new(Unassigned, "Unlabeled", unlabeled, []));
                labelCols.AddRange(Arrange(WorkItemsApi.Labels, l => "label:" + l, o)
                    .Select(l => Col(l, l, i => i.Label == l)));
                return [new(null, labelCols)];
            default:
                return [new(null, Arrange(WorkItemsApi.Statuses, s => ColumnKey(o, s), o).Select(s => Col(s, s, i => i.Status == s)).ToList())];
        }
    }

    /// <summary>The list view's rows, in board order: column by column, each column's items in their sorted order.</summary>
    public static IReadOnlyList<GridRow> Rows(IReadOnlyList<BoardSection> sections, IReadOnlyDictionary<string, string> featureNames)
    {
        List<GridRow> rows = [];
        int group = 0, position = 0;
        foreach (BoardSection s in sections)
        foreach (BoardColumn c in s.Columns)
        {
            if (c.Items.Count == 0) rows.Add(new(group, c, s.Category, null, position++, ""));
            foreach (WorkItem it in c.Items)
                rows.Add(new(group, c, s.Category, it, position++, string.Join(", ", it.Features.Select(f => featureNames.GetValueOrDefault(f.Path, f.Path)))));
            group++;
        }
        return rows;
    }

    /// <summary>Key of a list column (Title, Status, …) in the hidden list, next to the board columns' keys.</summary>
    public static string ListColumnKey(string title) => "col:" + title;

    /// <summary>Column key for work items without a feature (grouping by feature) or without contributors (by contributor).</summary>
    public const string Unassigned = "";

    /// <summary>The buckets features are organized into (folders under openspec/specs: Functional, Non-Functional; Ungrouped = no folder).</summary>
    public static IReadOnlyList<string> Categories(IReadOnlyList<Feature> features) =>
        features.Select(f => f.Group).Distinct().OrderBy(g => g == FeaturesApi.Ungrouped).ToList();   // named buckets first

    public static string FeatureKey(string path) => "feature:" + path;

    /// <summary>Key of a column under the current grouping, for the saved order / hidden lists.</summary>
    public static string ColumnKey(BoardOptions o, string column) => o.GroupBy switch
    {
        "bucket" or "feature" => FeatureKey(column),
        "label" => "label:" + column,
        "contributor" => "who:" + column,
        _ => "status:" + column,
    };

    /// <summary>Readable name of a saved key (for the side menu's list of hidden columns).</summary>
    public static string KeyLabel(string key, IReadOnlyList<Feature> features) => key.Split(':', 2) switch
    {
        ["feature", string path] => features.FirstOrDefault(f => f.Path == path)?.Name ?? path,
        ["bucket", string name] => name,
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
