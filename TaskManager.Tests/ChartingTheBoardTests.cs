using Features.PublicDTOs;
using TaskManager.Board;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>The Analytics view's pure model: a chart spec plus work items become categories and series of numbers.</summary>
public sealed class ChartingTheBoardTests
{
    private static readonly Feature[] Features =
    [
        new("functional/banking", "Banking", "Functional"),
        new("non-functional/security", "Security", "Non-Functional"),
    ];

    private static WorkItem W(int id, string status, string[] features, string[] who, int subs = 0, string? label = null, string priority = "low", DateTime? created = null) =>
        new(id, $"f{id}", $"/repo/f{id}", $"Item {id}", status, priority, who,
            features.Select(f => new FeatureLink(f, false)).ToList(),
            Enumerable.Range(1, subs).Select(i => new Subtask($"1.{i}", $"Sub {i}", i == 1, "1. Tasks")).ToList(),
            new Dictionary<string, string>(), status == "Completed", created ?? new DateTime(2026, 9, 1), new DateTime(2026, 10, 1), null, Label: label);

    private static readonly WorkItem[] Items =
    [
        W(1, "Backlog", ["functional/banking"], ["Tom"], label: "feature"),
        W(2, "In Progress", ["non-functional/security"], ["Artyom", "Tom"], subs: 3, label: "bug", priority: "high"),
        W(3, "In Progress", ["functional/banking"], ["Artyom"], subs: 2, label: "feature"),
        W(4, "Completed", [], [], label: null),
    ];

    private static ChartData Build(string x, string group = "none", string y = "items", params WorkItem[] items) =>
        AnalyticsModel.Build(new("c", "t", "column", x, group, y), items.Length == 0 ? Items : items, _ => [], Features, new DateTime(2026, 11, 4));

    [Fact(DisplayName = "A chart by status counts the work items in each status in the board's order, and grouped by label it has one series per label")]
    public void ByStatusAndGrouped()
    {
        ChartData plain = Build("status");
        Assert.Equal(["Backlog", "In Progress", "Completed"], plain.Categories);
        Series only = Assert.Single(plain.Series);
        Assert.Equal([1d, 2d, 1d], only.Values);

        ChartData grouped = Build("status", "label");
        Assert.Equal(["Backlog", "In Progress", "Completed"], grouped.Categories);
        Assert.Equal(["bug", "feature", "none"], grouped.Series.Select(s => s.Name));
        Assert.Equal([0d, 1d, 0d], grouped.Series[0].Values);
        Assert.Equal([1d, 1d, 0d], grouped.Series[1].Values);
        Assert.Equal([0d, 0d, 1d], grouped.Series[2].Values);
        Assert.Equal(["--peach", "--sky", "--stone"], grouped.Series.Select(s => s.ColourKey));   // the label pill colours of the board
    }

    [Fact(DisplayName = "A chart of subtasks counts subtasks instead of items")]
    public void SubtasksInsteadOfItems()
    {
        ChartData data = Build("status", y: "subtasks");
        Assert.Equal(["Backlog", "In Progress", "Completed"], data.Categories);
        Assert.Equal([0d, 5d, 0d], Assert.Single(data.Series).Values);
    }

    [Fact(DisplayName = "A chart over time shows, week by week for twelve weeks, how many items were in each status, replayed from their worklog")]
    public void OverTime()
    {
        WorkItem a = W(1, "Completed", [], [], created: new DateTime(2026, 9, 1, 9, 0, 0));   // Backlog from Tue 1 Sep, In Progress from Sun 20 Sep, Completed from Wed 14 Oct
        WorkItem b = W(2, "Planned", [], [], created: new DateTime(2026, 10, 1, 9, 0, 0));    // never moved: Planned since it was created
        WorkItem later = W(3, "Backlog", [], [], created: new DateTime(2026, 11, 3));          // created this week: only in the last one
        Dictionary<int, WorkItemEvent[]> log = new()
        {
            [1] = [new(new DateTime(2026, 9, 1, 9, 0, 0), "x", "created", "A"), new(new DateTime(2026, 9, 20, 12, 0, 0), "x", "status", "Backlog -> In Progress"), new(new DateTime(2026, 10, 14, 12, 0, 0), "x", "status", "In Progress -> Completed")],
            [2] = [new(new DateTime(2026, 10, 1, 9, 0, 0), "x", "created", "B")],
        };
        ChartData data = AnalyticsModel.Build(new("c", "t", "stacked", "time", "none", "items"), [a, b, later],
            id => log.GetValueOrDefault(id, []), Features, new DateTime(2026, 11, 4));   // a Wednesday: the last week starts Mon 2 Nov

        Assert.Equal(12, data.Categories.Count);
        Assert.Equal("17 Aug", data.Categories[0]);
        Assert.Equal("2 Nov", data.Categories[11]);
        Assert.Equal(["Backlog", "Planned", "In Progress", "Completed"], data.Series.Select(s => s.Name));
        Assert.Equal([0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1], data.Series[0].Values.Select(v => (int)v));   // Backlog: item a in weeks 2-3, item 3 in the last
        Assert.Equal([0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1], data.Series[1].Values.Select(v => (int)v));   // Planned: item b from the week it was created
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0], data.Series[2].Values.Select(v => (int)v));   // In Progress: item a in weeks 4-7
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1], data.Series[3].Values.Select(v => (int)v));   // Completed: item a from week 8
    }

    [Fact(DisplayName = "Items without the field fall under none, which comes last")]
    public void NoneComesLast()
    {
        ChartData label = Build("label");
        Assert.Equal(["bug", "feature", "none"], label.Categories);
        Assert.Equal([1d, 2d, 1d], label.Series[0].Values);

        ChartData feature = Build("feature");
        Assert.Equal(["Banking", "Security", "none"], feature.Categories);   // an item in two features would count in both; this one has none
        Assert.Equal([2d, 1d, 1d], feature.Series[0].Values);

        ChartData who = Build("contributor");
        Assert.Equal(["Artyom", "Tom", "none"], who.Categories);
        Assert.Equal([2d, 2d, 1d], who.Series[0].Values);
    }
}
