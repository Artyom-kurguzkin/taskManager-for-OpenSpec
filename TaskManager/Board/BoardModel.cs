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
    string Category = "all",       // feature-group switcher; "all" shows every group
    string Unit = "tasks",         // tasks | subtasks
    string View = "board",         // board | list
    bool Compact = false,
    bool ShowHidden = false);      // include work items "deleted" (hidden) in the app

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
                return [new(null, items.SelectMany(i => i.Contributors).Distinct().Order(StringComparer.OrdinalIgnoreCase)
                    .Select(p => Col(p, p, i => i.Contributors.Contains(p))).ToList())];
            case "feature":
                return features.GroupBy(f => f.Group)
                    .Where(g => o.Category == "all" || g.Key == o.Category)
                    .Select(g => new BoardSection(g.Key, g.Select(f => Col(f.Path, f.Name, i => i.Features.Any(x => x.Path == f.Path))).ToList()))
                    .ToList();
            default:
                return [new(null, WorkItemsApi.Statuses.Select(s => Col(s, s, i => i.Status == s)).ToList())];
        }
    }

    public static IReadOnlyList<string> Categories(IReadOnlyList<Feature> features) => features.Select(f => f.Group).Distinct().ToList();

    /// <summary>Items in one sub-category of a column; items whose stored lane is unknown fall into the first lane.</summary>
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
