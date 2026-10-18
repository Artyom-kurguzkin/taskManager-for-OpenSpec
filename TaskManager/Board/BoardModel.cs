using Features;
using Features.PublicDTOs;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>What the board/list is showing. Remembered per repository in the settings file.</summary>
public sealed record BoardOptions(
    string GroupBy = "status",     // status | feature | contributor
    string SortBy = "priority",    // priority | feature | who | modified | created | status | title | id
    int SortDir = 1,
    string Who = "all",
    string Category = "all",       // which side of the features is shown (Functional, Non-Functional, Ungrouped); unknown = the first
    string Unit = "tasks",         // tasks | subtasks
    string View = "board",         // board | list
    bool Compact = false,
    bool ShowHidden = false,       // include work items "deleted" (hidden) in the app
    IReadOnlyList<string>? Hidden = null,   // ColumnKey of columns taken off the board
    IReadOnlyList<string>? Order = null);   // keys in the user's order; anything not listed keeps its usual place after them

public sealed record BoardColumn(string Key, string Label, IReadOnlyList<WorkItem> Items, IReadOnlyList<string> Lanes);

public sealed record BoardSection(string? Category, IReadOnlyList<BoardColumn> Columns);

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
            case "feature":
                // one flat row: Unassigned first, then the chosen side's features (Functional / Non-Functional is a filter, not a level)
                string side = Side(features, o);
                List<BoardColumn> cols = [];
                if (visible.Where(i => i.Features.Count == 0).ToList() is { Count: > 0 } loose)
                    cols.Add(new(Unassigned, "Unassigned", loose, lanes.TryGetValue(Unassigned, out IReadOnlyList<string>? ul) ? ul : []));
                cols.AddRange(Arrange(features.Where(f => f.Group == side), f => FeatureKey(f.Path), o).Select(f => Col(f.Path, f.Name, i => i.Features.Any(x => x.Path == f.Path))));
                return [new(null, cols)];
            default:
                return [new(null, Arrange(WorkItemsApi.Statuses, s => ColumnKey(o, s), o).Select(s => Col(s, s, i => i.Status == s)).ToList())];
        }
    }

    /// <summary>Column key for work items without a feature (grouping by feature) or without contributors (by contributor).</summary>
    public const string Unassigned = "";

    /// <summary>The sides features are split into (folders under openspec/specs: Functional, Non-Functional; Ungrouped = no folder).</summary>
    public static IReadOnlyList<string> Categories(IReadOnlyList<Feature> features) =>
        features.Select(f => f.Group).Distinct().OrderBy(g => g == FeaturesApi.Ungrouped).ToList();   // named sides first

    /// <summary>The side being shown: the chosen one if it exists, else the first.</summary>
    public static string Side(IReadOnlyList<Feature> features, BoardOptions o) =>
        Categories(features) is var sides && sides.Contains(o.Category) ? o.Category : sides.FirstOrDefault() ?? FeaturesApi.Ungrouped;

    public static string FeatureKey(string path) => "feature:" + path;

    /// <summary>Key of a column under the current grouping, for the saved order / hidden lists.</summary>
    public static string ColumnKey(BoardOptions o, string column) => o.GroupBy switch
    {
        "feature" => FeatureKey(column),
        "contributor" => "who:" + column,
        _ => "status:" + column,
    };

    /// <summary>Readable name of a saved key (for the side menu's list of hidden columns).</summary>
    public static string KeyLabel(string key, IReadOnlyList<Feature> features) => key.Split(':', 2) switch
    {
        ["feature", string path] => features.FirstOrDefault(f => f.Path == path)?.Name ?? path,
        [_, string name] => name,
        _ => key,
    };

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
    /// Drag-and-drop reorder: <paramref name="dragged"/> takes <paramref name="target"/>'s place among <paramref name="siblings"/>
    /// (given in their current order), so dropping onto a later column lands after it and onto an earlier one lands before it.
    /// </summary>
    public static BoardOptions MoveTo(BoardOptions o, IReadOnlyList<string> siblings, string dragged, string target)
    {
        List<string> list = siblings.ToList();
        int to = list.IndexOf(target);
        if (dragged == target || to < 0 || !list.Remove(dragged)) return o;
        list.Insert(to, dragged);
        return o with { Order = [.. (o.Order ?? []).Except(list), .. list] };
    }

    /// <summary>Items in one subgroup of a column; items whose stored lane is unknown fall into the first lane.</summary>
    public static IEnumerable<WorkItem> InLane(BoardColumn col, string lane) =>
        col.Items.Where(i => (i.Lanes.TryGetValue(col.Key, out string? l) && col.Lanes.Contains(l) ? l : col.Lanes[0]) == lane);

    public static int Count(IEnumerable<WorkItem> items, string unit) => unit == "subtasks" ? items.Sum(i => i.Subtasks.Count) : items.Count();

    public static IEnumerable<WorkItem> Sort(IEnumerable<WorkItem> items, BoardOptions o)
    {
        Comparison<WorkItem> cmp = o.SortBy switch
        {
            "feature" => (a, b) => string.CompareOrdinal(a.Features.FirstOrDefault()?.Path ?? "~", b.Features.FirstOrDefault()?.Path ?? "~"),
            "who" => (a, b) => string.Compare(a.Contributors.FirstOrDefault() ?? "~", b.Contributors.FirstOrDefault() ?? "~", StringComparison.OrdinalIgnoreCase),
            "modified" => (a, b) => b.Modified.CompareTo(a.Modified),
            "created" => (a, b) => b.Created.CompareTo(a.Created),
            "status" => (a, b) => IndexOf(a.Status).CompareTo(IndexOf(b.Status)),
            "title" => (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
            "id" => (a, b) => a.Id.CompareTo(b.Id),
            _ => (a, b) => (a.Priority == b.Priority ? 0 : a.Priority == "high" ? -1 : 1) is int p && p != 0 ? p : b.Modified.CompareTo(a.Modified),
        };
        List<WorkItem> list = items.ToList();
        list.Sort((a, b) => (cmp(a, b) is int c && c != 0 ? c : a.Id.CompareTo(b.Id)) * o.SortDir);
        return list;
    }

    private static int IndexOf(string status) => WorkItemsApi.Statuses is IReadOnlyList<string> s ? s.ToList().IndexOf(status) : 0;
}
