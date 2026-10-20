using Features.PublicDTOs;
using TaskManager.Board;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

public sealed class ArrangingTheBoardTests
{
    private static readonly Feature[] Features =
    [
        new("functional/banking", "Banking", "Functional"),
        new("functional/remittances", "Remittances", "Functional"),
        new("non-functional/availability", "Availability", "Non-Functional"),
        new("non-functional/security", "Security", "Non-Functional"),
    ];

    private static WorkItem W(int id, string status, string[] features, string[] who, string priority = "low", int subs = 0, string? lane = null, int day = 1, string? label = null) =>
        new(id, $"f{id}", $"/repo/f{id}", $"Item {id}", status, priority, who,
            features.Select(f => new FeatureLink(f, false)).ToList(),
            Enumerable.Range(1, subs).Select(i => new Subtask($"1.{i}", $"Sub {i}", i == 1, "1. Tasks")).ToList(),
            lane is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["In Progress"] = lane },
            status == "Completed", new DateTime(2026, 9, day), new DateTime(2026, 10, day), null, Label: label);

    private static readonly WorkItem[] Items =
    [
        W(1, "Backlog", ["functional/banking"], ["Tom"], day: 3, label: "feature"),
        W(2, "In Progress", ["non-functional/availability", "non-functional/security"], ["Artyom", "Tom"], "high", subs: 3, lane: "High priority", day: 5, label: "bug"),
        W(3, "In Progress", ["functional/remittances"], ["Artyom"], subs: 2, day: 9, label: "feature"),
        W(4, "Completed", ["non-functional/security"], ["Mira"], day: 1),
    ];

    private static readonly Dictionary<string, IReadOnlyList<string>> Lanes = new() { ["In Progress"] = ["High priority", "Lower priority"] };

    private static IReadOnlyList<BoardSection> Build(BoardOptions o) => BoardModel.Build(Items, Features, Lanes, o);

    [Fact(DisplayName = "Grouping by status gives one column per status in workflow order")]
    public void ByStatus()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions()));
        Assert.Null(s.Category);
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], s.Columns.Select(c => c.Key));
        Assert.Equal([2, 3], s.Columns.Single(c => c.Key == "In Progress").Items.Select(i => i.Id));   // high priority first
    }

    [Fact(DisplayName = "Grouping by bucket shows all buckets as sections, each with its feature columns")]
    public void ByBucket()
    {
        IReadOnlyList<BoardSection> sections = Build(new BoardOptions(GroupBy: "bucket"));
        Assert.Equal(2, sections.Count);
        Assert.Equal("Functional", sections[0].Category);
        Assert.Equal(["Banking", "Remittances"], sections[0].Columns.Select(c => c.Label));
        Assert.Equal("Non-Functional", sections[1].Category);
        Assert.Equal(["Availability", "Security"], sections[1].Columns.Select(c => c.Label));
        Assert.Contains(sections[1].Columns[0].Items, i => i.Id == 2);   // item 2 in both non-functional features
        Assert.Contains(sections[1].Columns[1].Items, i => i.Id == 2);
    }

    [Fact(DisplayName = "Grouping by label shows one column per label plus Unlabeled")]
    public void ByLabel()
    {
        IReadOnlyList<BoardSection> sections = Build(new BoardOptions(GroupBy: "label"));
        BoardSection s = Assert.Single(sections);
        Assert.Equal(["Unlabeled", "bug", "feature", "enhancement"], s.Columns.Select(c => c.Label));
        Assert.Equal([4], s.Columns.Single(c => c.Label == "Unlabeled").Items.Select(i => i.Id));
        Assert.Equal([2], s.Columns.Single(c => c.Label == "bug").Items.Select(i => i.Id));
        Assert.Equal([3, 1], s.Columns.Single(c => c.Label == "feature").Items.Select(i => i.Id));   // sorted by modified desc
    }

    [Fact(DisplayName = "Grouping by contributor gives one column per person, sortable by feature")]
    public void ByContributorSortedByFeature()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions(GroupBy: "contributor", SortBy: "feature")));
        Assert.Equal(["Artyom", "Mira", "Tom"], s.Columns.Select(c => c.Key));
        Assert.Equal([1, 2], s.Columns.Single(c => c.Key == "Tom").Items.Select(i => i.Id));   // banking before availability
    }

    [Fact(DisplayName = "Filtering by a contributor keeps only their work items")]
    public void FilterByContributor()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions(Who: "Mira")));
        Assert.Equal([4], s.Columns.SelectMany(c => c.Items).Select(i => i.Id));
    }

    [Fact(DisplayName = "Sorting can be reversed")]
    public void ReverseSort()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions(SortBy: "modified", SortDir: -1)));
        Assert.Equal([2, 3], s.Columns.Single(c => c.Key == "In Progress").Items.Select(i => i.Id));   // oldest first
    }

    [Fact(DisplayName = "A column's sub-categories split its items, and items without a known lane go to the first")]
    public void LanesSplitColumn()
    {
        BoardColumn col = Build(new BoardOptions())[0].Columns.Single(c => c.Key == "In Progress");
        Assert.Equal(["High priority", "Lower priority"], col.Lanes);
        Assert.Equal([2, 3], BoardModel.InLane(col, "High priority").Select(i => i.Id));
        Assert.Empty(BoardModel.InLane(col, "Lower priority"));
    }

    [Fact(DisplayName = "In subtask mode a column counts subtasks instead of work items")]
    public void SubtaskCounts()
    {
        BoardColumn col = Build(new BoardOptions(Unit: "subtasks"))[0].Columns.Single(c => c.Key == "In Progress");
        Assert.Equal(5, BoardModel.Count(col.Items, "subtasks"));
        Assert.Equal(2, BoardModel.Count(col.Items, "tasks"));
    }

    [Fact(DisplayName = "A hidden feature leaves the board within its bucket")]
    public void HiddenFeature()
    {
        BoardOptions o = new(GroupBy: "bucket", Hidden: [BoardModel.FeatureKey("non-functional/security")]);
        BoardSection nf = Build(o).Single(s => s.Category == "Non-Functional");
        Assert.Equal(["Availability"], nf.Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Features follow the saved order within their bucket")]
    public void OrderedFeatures()
    {
        BoardOptions o = new(GroupBy: "bucket", Order: [BoardModel.FeatureKey("non-functional/security")]);
        BoardSection nf = Build(o).Single(s => s.Category == "Non-Functional");
        Assert.Equal(["Security", "Availability"], nf.Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Dropping Backlog onto Completed moves the Backlog column to after Completed")]
    public void DragStatusColumn()
    {
        BoardOptions o = new();
        List<string> keys = Build(o)[0].Columns.Select(c => BoardModel.ColumnKey(o, c.Key)).ToList();

        BoardOptions moved = BoardModel.MoveTo(o, keys, BoardModel.ColumnKey(o, "Backlog"), BoardModel.ColumnKey(o, "Completed"));

        Assert.Equal(["Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "Backlog"], Build(moved)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Dropping a column onto an earlier one puts it in front of that one")]
    public void DragLeft()
    {
        BoardOptions o = new();
        List<string> keys = Build(o)[0].Columns.Select(c => BoardModel.ColumnKey(o, c.Key)).ToList();

        BoardOptions moved = BoardModel.MoveTo(o, keys, BoardModel.ColumnKey(o, "Blocked"), BoardModel.ColumnKey(o, "Planned"));

        Assert.Equal(["Backlog", "Blocked", "Planned", "Confirmed", "In Progress", "In Review", "Completed"], Build(moved)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "A hidden status column leaves the board")]
    public void HiddenStatus()
    {
        BoardOptions o = BoardModel.ToggleHidden(new BoardOptions(), BoardModel.ColumnKey(new BoardOptions(), "Completed"));
        Assert.DoesNotContain("Completed", Build(o)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Work items without a feature show in an Unassigned section when grouping by bucket")]
    public void Unassigned()
    {
        WorkItem loose = W(5, "Backlog", [], ["Tom"]);

        IReadOnlyList<BoardSection> sections = BoardModel.Build([.. Items, loose], Features, Lanes, new BoardOptions(GroupBy: "bucket"));

        Assert.Null(sections[0].Category);   // unassigned section has no bucket name
        Assert.Equal(["Unassigned"], sections[0].Columns.Select(c => c.Label));
        Assert.Equal([5], sections[0].Columns[0].Items.Select(i => i.Id));
    }

    [Fact(DisplayName = "Work items without contributors show in their own column when grouping by contributor")]
    public void NoContributors()
    {
        WorkItem loose = W(5, "Backlog", [], []);

        BoardSection s = Assert.Single(BoardModel.Build([.. Items, loose], Features, Lanes, new BoardOptions(GroupBy: "contributor")));

        Assert.Equal("No contributors", s.Columns[0].Label);
        Assert.Equal([5], s.Columns[0].Items.Select(i => i.Id));
    }
}
