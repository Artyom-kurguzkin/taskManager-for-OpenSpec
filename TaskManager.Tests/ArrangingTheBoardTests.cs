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

    private static WorkItem W(int id, string status, string[] features, string[] who, string priority = "low", int subs = 0, string? lane = null, int day = 1, string? label = null, DateOnly? due = null, DateOnly? started = null) =>
        new(id, $"f{id}", $"/repo/f{id}", $"Item {id}", status, priority, who,
            features.Select(f => new FeatureLink(f, false)).ToList(),
            Enumerable.Range(1, subs).Select(i => new Subtask($"1.{i}", $"Sub {i}", i == 1, "1. Tasks")).ToList(),
            lane is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["In Progress"] = lane },
            status == "Completed", new DateTime(2026, 9, day), new DateTime(2026, 10, day), null, Label: label, Started: started, Due: due);

    private static readonly WorkItem[] Items =
    [
        W(1, "Backlog", ["functional/banking"], ["Tom"], day: 3, label: "feature", due: new DateOnly(2026, 11, 2)),
        W(2, "In Progress", ["non-functional/availability", "non-functional/security"], ["Artyom", "Tom"], "high", subs: 3, lane: "High priority", day: 5, label: "bug", due: new DateOnly(2026, 11, 4), started: new DateOnly(2026, 11, 3)),
        W(3, "In Progress", ["functional/remittances"], ["Artyom"], subs: 2, day: 9, label: "feature", due: new DateOnly(2026, 11, 7), started: new DateOnly(2026, 10, 28)),
        W(4, "Completed", ["non-functional/security"], ["Mira"], day: 1, due: new DateOnly(2026, 11, 12)),
    ];

    private static readonly Dictionary<string, IReadOnlyList<string>> Lanes = new() { ["In Progress"] = ["High priority", "Lower priority"] };

    private static IReadOnlyList<BoardSection> Build(BoardOptions o) => BoardModel.Build(Items, Features, Lanes, o);

    [Fact(DisplayName = "Within a column cards fall into priority lanes, highest first, one lane per priority present")]
    public void PriorityLanes()
    {
        BoardColumn Column(string label) => Build(new BoardOptions()).Single().Columns.Single(c => c.Label == label);

        var lanes = BoardModel.PriorityLanes(Column("In Progress"));
        Assert.Equal([("high", "High priority"), ("low", "Lower priority")], lanes.Select(l => (l.Priority, l.Label)));
        Assert.Equal([[2], [3]], lanes.Select(l => l.Items.Select(i => i.Id).ToArray()).ToArray());

        var single = Assert.Single(BoardModel.PriorityLanes(Column("Backlog")));   // item 1 alone
        Assert.Equal(("low", "Lower priority"), (single.Priority, single.Label));
        Assert.Empty(BoardModel.PriorityLanes(Column("Blocked")));
    }

    [Fact(DisplayName = "Grouping by due date gives Overdue, Today, This week, Next week, Later and No due date; by start date, Earlier, This week, Next week, Later and Not started")]
    public void ByPeriod()
    {
        DateOnly today = new(2026, 11, 4);   // a Wednesday
        BoardSection due = Assert.Single(BoardModel.Build(Items, Features, Lanes, new BoardOptions(GroupBy: "due"), today));
        Assert.Equal(["Overdue", "Today", "This week", "Next week", "Later", "No due date"], due.Columns.Select(c => c.Label));
        Assert.Equal(["due:overdue", "due:today", "due:week", "due:next", "due:later", "due:none"], due.Columns.Select(c => BoardModel.ColumnKey(new BoardOptions(GroupBy: "due"), c.Key)));
        Assert.Equal([[1], [2], [3], [4], [], []], due.Columns.Select(c => c.Items.Select(i => i.Id).ToArray()).ToArray());

        BoardSection started = Assert.Single(BoardModel.Build(Items, Features, Lanes, new BoardOptions(GroupBy: "started"), today));
        Assert.Equal(["Earlier", "This week", "Next week", "Later", "Not started"], started.Columns.Select(c => c.Label));
        Assert.Equal([[3], [2], [], [], [1, 4]], started.Columns.Select(c => c.Items.Select(i => i.Id).ToArray()).ToArray());
        Assert.Equal("Due", BoardModel.GroupedColumn(new BoardOptions(GroupBy: "due")));
        Assert.Equal("Started", BoardModel.GroupedColumn(new BoardOptions(GroupBy: "started")));
    }

    [Fact(DisplayName = "The period filter keeps only work due in that period (overdue, today, this week, this month, none) under any grouping")]
    public void PeriodFilter()
    {
        DateOnly today = new(2026, 11, 4);
        int[] Ids(string period) => BoardModel.Build(Items, Features, Lanes, new BoardOptions(Period: period), today).SelectMany(s => s.Columns).SelectMany(c => c.Items).Select(i => i.Id).Order().ToArray();
        Assert.Equal([1, 2, 3, 4], Ids("all"));
        Assert.Equal([1], Ids("overdue"));
        Assert.Equal([2], Ids("today"));
        Assert.Equal([2, 3], Ids("week"));     // today to Sunday
        Assert.Equal([2, 3, 4], Ids("month"));  // today to the end of the month
        Assert.Equal([], Ids("none"));
        Assert.Equal("overdue", BoardModel.Period(new DateOnly(2026, 11, 3), today));
        Assert.Equal("week", BoardModel.Period(new DateOnly(2026, 11, 8), today));   // Sunday still this week
        Assert.Equal("next", BoardModel.Period(new DateOnly(2026, 11, 9), today));
        Assert.Equal("none", BoardModel.Period(null, today));
    }

    [Fact(DisplayName = "A calendar month runs in whole weeks from Monday; a bar spans an item's start to its due date, clipped to the week and stacked without overlap")]
    public void CalendarMonth()
    {
        IReadOnlyList<IReadOnlyList<DateOnly>> weeks = CalendarModel.Weeks(new DateOnly(2026, 11, 1));
        Assert.Equal(6, weeks.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), weeks[0][0]);
        Assert.Equal(new DateOnly(2026, 12, 6), weeks[^1][6]);
        Assert.All(weeks, w => Assert.Equal(7, w.Count));

        IReadOnlyList<CalendarBar> bars = CalendarModel.Bars(Items, weeks[1]);   // 2–8 Nov
        CalendarBar Bar(int id) => bars.Single(b => b.Item.Id == id);
        Assert.Equal((0, 6, true, false, 0), (Bar(3).Col, Bar(3).Span, Bar(3).StartsBefore, Bar(3).EndsAfter, Bar(3).Lane));   // started 28 Oct, due Sat 7 Nov
        Assert.Equal((0, 1, false, false, 1), (Bar(1).Col, Bar(1).Span, Bar(1).StartsBefore, Bar(1).EndsAfter, Bar(1).Lane));   // due only: one day
        Assert.Equal((1, 2, false, false, 1), (Bar(2).Col, Bar(2).Span, Bar(2).StartsBefore, Bar(2).EndsAfter, Bar(2).Lane));   // Tue–Wed, fits under item 1
        Assert.DoesNotContain(bars, b => b.Item.Id == 4);   // due next week
        Assert.Empty(CalendarModel.Unscheduled(Items));
        Assert.Equal([1], CalendarModel.Unscheduled([Items[0] with { Due = null }, Items[3] with { Due = null }]).Select(i => i.Id));   // a completed item without dates is done, not unscheduled
    }

    [Fact(DisplayName = "Grouping by status gives one column per status in workflow order")]
    public void ByStatus()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions()));
        Assert.Null(s.Category);
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], s.Columns.Select(c => c.Key));
        Assert.Equal([2, 3], s.Columns.Single(c => c.Key == "In Progress").Items.Select(i => i.Id));   // high priority first
    }

    [Fact(DisplayName = "Grouping by feature gives one flat row of feature columns, with no supergroups")]
    public void ByFeature()
    {
        BoardSection s = Assert.Single(Build(new BoardOptions(GroupBy: "feature")));
        Assert.Null(s.Category);
        Assert.Equal(["Banking", "Remittances", "Availability", "Security"], s.Columns.Select(c => c.Label));
        Assert.Contains(s.Columns[2].Items, i => i.Id == 2);   // item 2 in both non-functional features
        Assert.Contains(s.Columns[3].Items, i => i.Id == 2);
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

    [Fact(DisplayName = "A hidden feature leaves the board")]
    public void HiddenFeature()
    {
        BoardOptions o = new(GroupBy: "feature", Hidden: [BoardModel.FeatureKey("non-functional/security")]);
        Assert.Equal(["Banking", "Remittances", "Availability"], Build(o).Single().Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Features follow the saved order")]
    public void OrderedFeatures()
    {
        BoardOptions o = new(GroupBy: "feature", Order: [BoardModel.FeatureKey("non-functional/security")]);
        Assert.Equal(["Security", "Banking", "Remittances", "Availability"], Build(o).Single().Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Dropping Backlog past the last column moves it to the end")]
    public void DragStatusColumn()
    {
        BoardOptions o = new();
        List<string> keys = Build(o)[0].Columns.Select(c => BoardModel.ColumnKey(o, c.Key)).ToList();

        BoardOptions moved = BoardModel.Insert(o, keys, [BoardModel.ColumnKey(o, "Backlog")], null);

        Assert.Equal(["Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "Backlog"], Build(moved)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Dropping a column at a gap puts it in front of the column after the gap")]
    public void DragLeft()
    {
        BoardOptions o = new();
        List<string> keys = Build(o)[0].Columns.Select(c => BoardModel.ColumnKey(o, c.Key)).ToList();

        BoardOptions moved = BoardModel.Insert(o, keys, [BoardModel.ColumnKey(o, "Blocked")], BoardModel.ColumnKey(o, "Planned"));

        Assert.Equal(["Backlog", "Blocked", "Planned", "Confirmed", "In Progress", "In Review", "Completed"], Build(moved)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Hidden columns report how many items they hold, following the contributor filter and the work items / subtasks switch")]
    public void HiddenCounts()
    {
        BoardOptions o = new(Hidden: ["status:In Progress", "status:Backlog", "status:Blocked"]);

        Dictionary<string, int> counts = BoardModel.HiddenCounts(Items, Features, Lanes, o);

        Assert.Equal(new Dictionary<string, int> { ["status:In Progress"] = 2, ["status:Backlog"] = 1, ["status:Blocked"] = 0 }, counts);
        Assert.Equal(1, BoardModel.HiddenCounts(Items, Features, Lanes, o with { Who = "Tom" })["status:In Progress"]);
        Assert.Equal(5, BoardModel.HiddenCounts(Items, Features, Lanes, o with { Unit = "subtasks" })["status:In Progress"]);   // 3 + 2 subtasks
    }

    [Fact(DisplayName = "A hidden status column leaves the board")]
    public void HiddenStatus()
    {
        BoardOptions o = BoardModel.ToggleHidden(new BoardOptions(), BoardModel.ColumnKey(new BoardOptions(), "Completed"));
        Assert.DoesNotContain("Completed", Build(o)[0].Columns.Select(c => c.Label));
    }

    [Fact(DisplayName = "Work items without a feature show in an Unassigned column, first, when grouping by feature")]
    public void Unassigned()
    {
        WorkItem loose = W(5, "Backlog", [], ["Tom"]);

        BoardSection s = Assert.Single(BoardModel.Build([.. Items, loose], Features, Lanes, new BoardOptions(GroupBy: "feature")));

        Assert.Equal("Unassigned", s.Columns[0].Label);
        Assert.Equal([5], s.Columns[0].Items.Select(i => i.Id));
        Assert.Equal(5, s.Columns.Count);
    }

    [Fact(DisplayName = "Work items without contributors show in their own column when grouping by contributor")]
    public void NoContributors()
    {
        WorkItem loose = W(5, "Backlog", [], []);

        BoardSection s = Assert.Single(BoardModel.Build([.. Items, loose], Features, Lanes, new BoardOptions(GroupBy: "contributor")));

        Assert.Equal("No contributors", s.Columns[0].Label);
        Assert.Equal([5], s.Columns[0].Items.Select(i => i.Id));
    }

    private static IEnumerable<int> InProgress(BoardOptions o) => Build(o)[0].Columns.Single(c => c.Key == "In Progress").Items.Select(i => i.Id);

    [Fact(DisplayName = "Dropping a card at the end of its column puts it last, and the column sorts by my order")]
    public void PlaceCardAtEnd()
    {
        BoardOptions o = BoardModel.PlaceCards(new BoardOptions(), [2, 3], [2], null);

        Assert.Equal("manual", o.SortBy);
        Assert.Equal([3, 2], InProgress(o));
    }

    [Fact(DisplayName = "A card from another column lands in front of the card after the gap; cards never placed follow in priority order")]
    public void PlaceCardFromElsewhere()
    {
        BoardOptions o = BoardModel.PlaceCards(new BoardOptions(), [2, 3], [1], 3);

        Assert.Equal([2, 1, 3, 4], BoardModel.Sort(Items, o).Select(i => i.Id));
    }

    [Fact(DisplayName = "Several selected cards land together at the gap, in their board order")]
    public void PlaceSeveral()
    {
        BoardOptions o = BoardModel.PlaceCards(new BoardOptions(), [2, 3], [1, 4], 3);

        Assert.Equal([2, 1, 4, 3], BoardModel.Sort(Items, o).Select(i => i.Id));
    }

    [Fact(DisplayName = "Dropping a card back where it was changes nothing, not even the sort")]
    public void DropInPlace()
    {
        BoardOptions o = new(SortBy: "id");

        Assert.Same(o, BoardModel.PlaceCards(o, [2, 3], [2], 2));
        Assert.Equal("id", BoardModel.PlaceCards(o, [2, 3], [2], 3).SortBy);   // already right in front of 3
    }

    private static readonly WorkItem[] Five = [.. Enumerable.Range(1, 5).Select(i => W(i, "Backlog", [], ["Tom"], day: 6 - i))];   // 1..5 by priority order

    private static IEnumerable<int> Backlog(BoardOptions o) => BoardModel.Sort(Five, o).Select(i => i.Id);

    [Fact(DisplayName = "Arrow up/down moves a selected card one step inside its column; it stops at the ends")]
    public void StepOne()
    {
        BoardOptions o = BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [3], -1);
        Assert.Equal([1, 3, 2, 4, 5], Backlog(o));
        Assert.Equal("manual", o.SortBy);

        Assert.Equal([1, 2, 3, 4, 5], Backlog(BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [3], -1) is var up ? BoardModel.Step(up, [1, 3, 2, 4, 5], [3], +1) : up));
        Assert.Equal([1, 2, 3, 4, 5], Backlog(BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [1], -1)));
        Assert.Equal([1, 2, 3, 4, 5], Backlog(BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [5], +1)));
    }

    [Fact(DisplayName = "A scattered selection first gathers: up at its top card, down at its bottom card; then it moves as a block")]
    public void StepGroup()
    {
        BoardOptions up = BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [2, 4], -1);
        Assert.Equal([1, 2, 4, 3, 5], Backlog(up));
        Assert.Equal([2, 4, 1, 3, 5], Backlog(BoardModel.Step(up, [1, 2, 4, 3, 5], [2, 4], -1)));

        BoardOptions down = BoardModel.Step(new BoardOptions(), [1, 2, 3, 4, 5], [2, 4], +1);
        Assert.Equal([1, 3, 2, 4, 5], Backlog(down));
        Assert.Equal([1, 3, 5, 2, 4], Backlog(BoardModel.Step(down, [1, 3, 2, 4, 5], [2, 4], +1)));
    }

    [Fact(DisplayName = "Picking another sort ignores my order but keeps it for later")]
    public void OtherSortKeepsMyOrder()
    {
        BoardOptions mine = BoardModel.PlaceCards(new BoardOptions(), [2, 3], [2], null);

        Assert.Equal([2, 3], InProgress(mine with { SortBy = "id" }));
        Assert.Equal([3, 2], InProgress(mine with { SortBy = "id" } with { SortBy = "manual" }));
    }
}
