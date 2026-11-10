using System.Globalization;
using Features.PublicDTOs;
using TaskManager.Components;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>
/// One chart of the Analytics view, as GitHub Projects' insights: a layout, what the X axis counts along, an optional group (the
/// series) and what is counted. Saved per board in <see cref="BoardOptions.Charts"/>.
/// <see cref="Layout"/> column | bar | line | stacked; <see cref="X"/> status | feature | label | priority | contributor | time (weeks);
/// <see cref="Group"/> none | status | feature | label | priority | contributor; <see cref="Y"/> items | subtasks.
/// </summary>
public sealed record ChartSpec(string Id, string Title, string Layout, string X, string Group, string Y);

/// <summary>One series of a chart: its name, the board colour token it is drawn in (a CSS variable name) and a value per category.</summary>
public sealed record Series(string Name, string ColourKey, IReadOnlyList<double> Values);

/// <summary><see cref="CategoryColours"/> is set when there is one series that has a board colour for each category (a status, a label): its marks take them.</summary>
public sealed record ChartData(IReadOnlyList<string> Categories, IReadOnlyList<Series> Series, IReadOnlyList<string>? CategoryColours = null);

/// <summary>Pure model of the Analytics view (no IO, unit-tested directly): a chart spec and the work items become categories and series.</summary>
public static class AnalyticsModel
{
    public static readonly IReadOnlyList<(string, string)> Layouts = [("column", "Column"), ("bar", "Bar"), ("line", "Line"), ("stacked", "Stacked column")];
    public static readonly IReadOnlyList<(string, string)> Xs = [("status", "Status"), ("feature", "Feature"), ("label", "Label"), ("priority", "Priority"), ("contributor", "Contributor"), ("time", "Time (weeks)")];
    public static readonly IReadOnlyList<(string, string)> Groups = [("none", "None"), ("status", "Status"), ("feature", "Feature"), ("label", "Label"), ("priority", "Priority"), ("contributor", "Contributor")];
    public static readonly IReadOnlyList<(string, string)> Ys = [("items", "Work items"), ("subtasks", "Subtasks")];

    /// <summary>The charts a board shows until the user changes them.</summary>
    public static readonly IReadOnlyList<ChartSpec> DefaultCharts =
    [
        new("items-by-status", "Items by status", "column", "status", "none", "items"),
        new("status-over-time", "Status over time", "stacked", "time", "status", "items"),
    ];

    /// <summary>The board's charts: its saved ones (an empty list is a board whose charts were all deleted), else the defaults.</summary>
    public static IReadOnlyList<ChartSpec> Charts(BoardOptions o) => o.Charts ?? DefaultCharts;

    public static ChartSpec NewChart() => new(Guid.NewGuid().ToString("N")[..8], "New chart", "column", "status", "none", "items");

    private const string NoneKey = "~none";
    private const int Weeks = 12;

    public static ChartData Build(ChartSpec spec, IReadOnlyList<WorkItem> items, Func<int, IReadOnlyList<WorkItemEvent>> events, IReadOnlyList<Feature> features, DateTime today)
    {
        string unit = spec.Y == "subtasks" ? "Subtasks" : "Work items";
        double Weight(WorkItem i) => spec.Y == "subtasks" ? i.Subtasks.Count : 1;
        if (spec.X == "time") return OverTime(spec, items, events, features, today, Weight);

        // one category per value present, one series per group value present; an item with several values counts under each
        Dictionary<string, string> labels = [];   // key -> label, for both axes
        IReadOnlyDictionary<string, string> map = Colours(spec.X, items, features);
        IReadOnlyDictionary<string, string> groupMap = Colours(spec.Group, items, features);
        List<(string X, string G, double W)> cells = [];
        foreach (WorkItem i in items)
            foreach ((string xk, string xl) in ValuesOf(i, spec.X, features))
                foreach ((string gk, string gl) in spec.Group == "none" ? [("", "")] : ValuesOf(i, spec.Group, features))
                {
                    labels[spec.X + "|" + xk] = xl;
                    if (spec.Group != "none") labels[spec.Group + "|" + gk] = gl;
                    cells.Add((xk, gk, Weight(i)));
                }

        List<string> xKeys = Ordered(spec.X, cells.Select(c => c.X).Distinct(), labels);
        if (spec.Group == "none")
        {
            double[] values = [.. xKeys.Select(k => cells.Where(c => c.X == k).Sum(c => c.W))];
            return new([.. xKeys.Select(k => labels[spec.X + "|" + k])], xKeys.Count == 0 ? [] : [new(unit, "--sky", values)],
                [.. xKeys.Select(k => ColourOf(spec.X, k, map))]);
        }
        List<string> gKeys = Ordered(spec.Group, cells.Select(c => c.G).Distinct(), labels);
        return new([.. xKeys.Select(k => labels[spec.X + "|" + k])],
            [.. gKeys.Select(g => new Series(labels[spec.Group + "|" + g], ColourOf(spec.Group, g, groupMap), [.. xKeys.Select(k => cells.Where(c => c.X == k && c.G == g).Sum(c => c.W))]))]);
    }

    /// <summary>The last twelve weeks (Monday starts) ending with the week of <paramref name="today"/>, and the items each group held at the end of each.</summary>
    private static ChartData OverTime(ChartSpec spec, IReadOnlyList<WorkItem> items, Func<int, IReadOnlyList<WorkItemEvent>> events, IReadOnlyList<Feature> features, DateTime today, Func<WorkItem, double> weight)
    {
        string field = spec.Group == "none" ? "status" : spec.Group;
        DateTime thisMonday = RoadmapModel.Monday(DateOnly.FromDateTime(today)).ToDateTime(TimeOnly.MinValue);
        DateTime[] starts = [.. Enumerable.Range(0, Weeks).Select(n => thisMonday.AddDays(7 * (n - Weeks + 1)))];
        // a week is judged at its end; the current one at the end of today
        DateTime End(DateTime start) => start.AddDays(7) is var e && e > today.Date.AddDays(1) ? today.Date.AddDays(1) : e;

        Dictionary<string, string> labels = [];
        IReadOnlyDictionary<string, string> map = Colours(field, items, features);
        // (week, group key) -> weight
        Dictionary<(int, string), double> counts = [];
        foreach (WorkItem i in items)
        {
            IReadOnlyList<WorkItemEvent> log = events(i.Id);
            DateTime created = log.FirstOrDefault(e => e.Kind == "created")?.At ?? i.Created;
            List<(DateTime At, string Old, string New)> moves = [.. log.Where(e => e.Kind == "status").Select(e => (e.At, Parts(e.Text)))
                .Where(t => t.Item2.Length == 2).Select(t => (t.At, t.Item2[0], t.Item2[1])).OrderBy(m => m.At)];
            string first = moves.Count > 0 ? moves[0].Old : i.Status;
            for (int w = 0; w < Weeks; w++)
            {
                DateTime end = End(starts[w]);
                if (created >= end) continue;
                string status = moves.LastOrDefault(m => m.At < end) is { New: not null } last ? last.New : first;
                if (i.Archived && i.Modified < end && moves.All(m => m.New != "Completed")) status = "Completed";   // archived by the CLI, with no move to Completed in its log
                foreach ((string k, string l) in field == "status" ? [(status, status)] : ValuesOf(i, field, features))
                {
                    labels[k] = l;
                    counts[(w, k)] = counts.GetValueOrDefault((w, k)) + weight(i);
                }
            }
        }
        List<string> keys = Ordered(field, counts.Keys.Select(k => k.Item2).Distinct(), labels, prefixed: false);
        return new([.. starts.Select(s => s.ToString("d MMM", CultureInfo.InvariantCulture))],
            [.. keys.Select(k => new Series(labels[k], ColourOf(field, k, map), [.. Enumerable.Range(0, Weeks).Select(w => counts.GetValueOrDefault((w, k)))]))]);
    }

    private static string[] Parts(string text) => text.Split(" -> ", 2);

    /// <summary>The values an item has for a field as (key, label): none of a field it has no value for.</summary>
    private static IEnumerable<(string Key, string Label)> ValuesOf(WorkItem i, string field, IReadOnlyList<Feature> features)
    {
        IEnumerable<(string, string)> values = field switch
        {
            "status" => [(i.Status, i.Status)],
            "label" => string.IsNullOrEmpty(i.Label) ? [] : [(i.Label, i.Label)],
            "priority" => [(i.Priority == "high" ? "high" : "low", i.Priority == "high" ? "High priority" : "Lower priority")],
            "feature" => i.Features.Select(f => (f.Path, features.FirstOrDefault(x => x.Path == f.Path)?.Name ?? f.Path)),
            "contributor" => i.Contributors.Select(c => (c, c)),
            _ => [],
        };
        return values.Any() ? values : [(NoneKey, "none")];
    }

    /// <summary>The keys in the field's order: status in the board's order, priority high first, the rest alphabetical by label; none last.</summary>
    private static List<string> Ordered(string field, IEnumerable<string> keys, Dictionary<string, string> labels, bool prefixed = true)
    {
        string Label(string k) => labels[prefixed ? field + "|" + k : k];
        int Rank(string k) => field switch
        {
            "status" => WorkItemsApi.Statuses.ToList().IndexOf(k) is var s && s >= 0 ? s : WorkItemsApi.Statuses.Count,
            "priority" => k == "high" ? 0 : 1,
            _ => 0,
        };
        return [.. keys.OrderBy(k => k == NoneKey).ThenBy(Rank).ThenBy(Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyDictionary<string, string> Colours(string field, IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features) =>
        BoardModel.ColourMap(field, items, features);

    /// <summary>The board's colour for a value of a field: its status band, label pill or priority colour, a palette entry for a feature or person.</summary>
    private static string ColourOf(string field, string key, IReadOnlyDictionary<string, string> map) => key == NoneKey ? "--stone" : field switch
    {
        "status" => Ui.Band(key),
        "label" => Ui.LabelColor(key),
        "priority" => Ui.PriorityColor(key),
        _ => map.GetValueOrDefault(key, "--stone"),
    };
}
