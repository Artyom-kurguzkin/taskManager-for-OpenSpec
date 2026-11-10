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

    [Fact(DisplayName = "Rows split a section's items by priority (High / Lower) or by label (unlabeled first, then each label), empty rows included, and none means one row")]
    public void Swimlanes()
    {
        string[] InColumn(SwimRow row, string column) => row.Columns.Single(c => c.Key == column).Items.Select(i => i.Id.ToString()).ToArray();
        BoardSection section = Build(new BoardOptions(Swimlane: "priority")).Single();

        IReadOnlyList<SwimRow> byPriority = BoardModel.Swimlanes(section);
        Assert.Equal([("high", "High priority"), ("low", "Lower priority")], byPriority.Select(r => (r.Key, r.Label)));
        Assert.All(byPriority, r => Assert.Equal(section.Columns.Select(c => c.Key), r.Columns.Select(c => c.Key)));   // every row spans every column
        Assert.Equal(["2"], InColumn(byPriority[0], "In Progress"));
        Assert.Empty(InColumn(byPriority[0], "Backlog"));   // an empty cell is still there, to drop a card into
        Assert.Equal(["3"], InColumn(byPriority[1], "In Progress"));
        Assert.Equal(["1"], InColumn(byPriority[1], "Backlog"));
        Assert.Equal(["4"], InColumn(byPriority[1], "Completed"));

        IReadOnlyList<SwimRow> byLabel = BoardModel.Swimlanes(Build(new BoardOptions(Swimlane: "label")).Single());
        Assert.Equal([(BoardModel.NoRow, "Unlabeled"), ("bug", "bug"), ("feature", "feature"), ("enhancement", "enhancement")], byLabel.Select(r => (r.Key, r.Label)));
        Assert.Equal([4], byLabel[0].Items.Select(i => i.Id));
        Assert.Equal([2], byLabel[1].Items.Select(i => i.Id));
        Assert.Equal([1, 3], byLabel[2].Items.Select(i => i.Id).Order());
        Assert.Empty(byLabel[3].Items);   // no enhancement, the row is still there

        SwimRow only = Assert.Single(BoardModel.Swimlanes(section with { Rows = null }));
        Assert.Equal(section.Columns.Select(c => c.Key), only.Columns.Select(c => c.Key));
        Assert.Equal(section.Columns.Select(c => c.Items.Count), only.Columns.Select(c => c.Items.Count));
    }

    [Fact(DisplayName = "The roadmap window is whole months at month zoom and whole weeks at quarter zoom; an item is a bar from its start to its due date, clipped at the window's edges, and an undated item has no bar")]
    public void RoadmapWindowAndBars()
    {
        RoadmapWindow month = RoadmapModel.Window(new DateOnly(2026, 11, 15), "month");
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31), 92), (month.Start, month.End, month.Columns));   // three whole months, a column per day
        RoadmapWindow quarter = RoadmapModel.Window(new DateOnly(2026, 11, 15), "quarter");
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2027, 5, 2), 27), (quarter.Start, quarter.End, quarter.Columns));   // six months in whole weeks (Monday to Sunday), a column per week
        Assert.Equal(DayOfWeek.Monday, quarter.Start.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, quarter.End.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 12, 15), RoadmapModel.Move(new DateOnly(2026, 11, 15), "month", +1));   // a month at month zoom
        Assert.Equal(new DateOnly(2026, 8, 15), RoadmapModel.Move(new DateOnly(2026, 11, 15), "quarter", -1));   // a quarter at quarter zoom

        static (int, int, bool, bool) Pos(RoadmapBar? b) => (b!.Col, b.Span, b.StartsBefore, b.EndsAfter);
        Assert.Equal((0, 7, true, false), Pos(RoadmapModel.Bar(Items[2], month)));    // started 28 Oct, due Sat 7 Nov: clipped at the left edge, 1-7 Nov
        Assert.Equal((1, 1, false, false), Pos(RoadmapModel.Bar(Items[0], month)));   // due only: a one-day bar
        Assert.Equal((2, 2, false, false), Pos(RoadmapModel.Bar(Items[1], month)));   // 3-4 Nov
        Assert.Equal((0, 2, false, false), Pos(RoadmapModel.Bar(Items[2], quarter)));   // the same item in week columns: this week and the next
        Assert.Equal((1, 1, false, false), Pos(RoadmapModel.Bar(Items[1], quarter)));
        Assert.Equal((90, 2, false, true), Pos(RoadmapModel.Bar(Items[0] with { Started = new DateOnly(2027, 1, 30), Due = new DateOnly(2027, 2, 5) }, month)));   // clipped at the right edge
        Assert.Null(RoadmapModel.Bar(Items[0] with { Started = null, Due = null }, month));   // undated: no bar
        Assert.Null(RoadmapModel.Bar(Items[0] with { Started = null, Due = new DateOnly(2027, 3, 1) }, month));   // entirely outside the window

        Assert.Equal((new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 12)), RoadmapModel.GroupSpan(Items));
        Assert.Null(RoadmapModel.GroupSpan([Items[0] with { Started = null, Due = null }]));
    }

    [Fact(DisplayName = "Rows split a section by any field the columns can use, a multi-valued field putting an item in each of its rows, and colour gives every item a band from any field")]
    public void RowsByAnyFieldAndColourByAnyField()
    {
        DateOnly today = new(2026, 11, 4);   // a Wednesday
        IReadOnlyList<SwimRow> Rows(string field, string columns = "status") =>
            BoardModel.Swimlanes(Assert.Single(BoardModel.Build(Items, Features, Lanes, new BoardOptions(GroupBy: columns, Swimlane: field), today)));
        int[] Ids(SwimRow r) => [.. r.Items.Select(i => i.Id).Order()];

        // feature: a row per feature, an item with two features in both rows; the cells span every column
        IReadOnlyList<SwimRow> byFeature = Rows("feature");
        Assert.Equal(["functional/banking", "functional/remittances", "non-functional/availability", "non-functional/security"], byFeature.Select(r => r.Key));
        Assert.Equal(["Banking", "Remittances", "Availability", "Security"], byFeature.Select(r => r.Label));
        Assert.Equal([[1], [3], [2], [2, 4]], byFeature.Select(Ids).ToArray());
        Assert.All(byFeature, r => Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], r.Columns.Select(c => c.Key)));

        // contributor, due, started and status use the columns' values and labels
        Assert.Equal(["Artyom", "Mira", "Tom"], Rows("contributor").Select(r => r.Label));
        Assert.Equal([[2, 3], [4], [1, 2]], Rows("contributor").Select(Ids).ToArray());
        Assert.Equal(["Overdue", "Today", "This week", "Next week", "Later", "No due date"], Rows("due").Select(r => r.Label));
        Assert.Equal([[1], [2], [3], [4], [], []], Rows("due").Select(Ids).ToArray());
        Assert.Equal(["Earlier", "This week", "Next week", "Later", "Not started"], Rows("started").Select(r => r.Label));
        Assert.Equal([[3], [2], [], [], [1, 4]], Rows("started").Select(Ids).ToArray());
        Assert.Equal(["Backlog", "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed"], Rows("status").Select(r => r.Key));

        // rows and columns may be different fields; priority is a column field too
        BoardSection byPriority = Assert.Single(BoardModel.Build(Items, Features, Lanes, new BoardOptions(GroupBy: "priority", Swimlane: "feature"), today));
        Assert.Equal([("high", "High priority"), ("low", "Lower priority")], byPriority.Columns.Select(c => (c.Key, c.Label)));
        Assert.Equal("priority:high", BoardModel.ColumnKey(new BoardOptions(GroupBy: "priority"), "high"));
        Assert.Equal([2], BoardModel.Swimlanes(byPriority)[2].Columns[0].Items.Select(i => i.Id));   // Availability, high priority
        Assert.Equal("Priority", BoardModel.GroupedColumn(new BoardOptions(GroupBy: "priority")));
        Assert.Equal("Feature", BoardModel.FieldColumn("feature"));
        Assert.Null(BoardModel.FieldColumn(BoardModel.NoSwimlane));

        // the list's rows follow the columns then the rows (a group per column), and carry their row's key and label
        IReadOnlyList<GridRow> grid = BoardModel.Rows(BoardModel.Build(Items, Features, Lanes, new BoardOptions(Swimlane: "contributor"), today), new Dictionary<string, string>());
        Assert.Equal(["Artyom", "Mira", "Tom"], grid.Select(r => r.SwimLabel).Where(l => l.Length > 0).Distinct().Order());
        Assert.Equal(grid.Select(r => r.Column.Key).Distinct(), grid.Select(r => r.Group).Distinct().Select(g => grid.First(r => r.Group == g).Column.Key));   // one group per column
        Assert.Equal(grid.Count, grid.Select(r => r.Key).Distinct().Count());   // every row has its own key, even an item in two row values of one column
        Assert.Equal([2, 3], grid.Where(r => r.Swim == "Artyom" && r.Item is not null).Select(r => r.Id).Order());

        // colour: a CSS variable per item, from any field
        string Colour(int id, string field) => BoardModel.ColourOf(Items.Single(i => i.Id == id), field, today);
        Assert.Equal(["--sun", "--sage"], [Colour(2, "status"), Colour(4, "status")]);                                 // the card band's status colours
        Assert.Equal(["--sky", "--peach", "--stone"], [Colour(1, "label"), Colour(2, "label"), Colour(4, "label")]);   // the label pill's colours
        Assert.Equal(["--peach", "--stone"], [Colour(2, "priority"), Colour(1, "priority")]);
        Assert.Equal(["--peach", "--sun", "--sky", "--mint"], [Colour(1, "due"), Colour(2, "due"), Colour(3, "due"), Colour(4, "due")]);   // overdue, today, this week, next week
        Assert.Equal("--sage", BoardModel.ColourOf(Items[0] with { Due = new DateOnly(2026, 12, 20) }, "due", today));   // later
        Assert.Equal(["--sage", "--sun", "--stone"], [Colour(3, "started"), Colour(2, "started"), Colour(1, "started")]);   // earlier, this week, not started
        Assert.Equal("--stone", Colour(1, "none"));
        Assert.Equal(Colour(2, "contributor"), Colour(3, "contributor"));   // both Artyom's first: a stable pick from the palette
        Assert.Equal(Colour(2, "feature"), BoardModel.ColourOf(Items[1] with { Id = 22 }, "feature", today));
        Assert.All(new[] { "contributor", "feature" }, f => Assert.All(Items, i => Assert.Contains(BoardModel.ColourOf(i, f, today), PaletteTokens)));

        // the legend lists the colours of what is shown, once each
        Assert.Equal([("Backlog", "--stone"), ("In Progress", "--sun"), ("Completed", "--sage")], BoardModel.Legend(Items, Features, new BoardOptions(), today));
        Assert.Equal([("Unlabeled", "--stone"), ("bug", "--peach"), ("feature", "--sky")], BoardModel.Legend(Items, Features, new BoardOptions(ColorBy: "label"), today));
        Assert.Empty(BoardModel.Legend(Items, Features, new BoardOptions(ColorBy: "none"), today));
    }

    // the twelve categorical colours features and contributors take in turn (CSS variables in app.css); --stone is the neutral "none"
    private static readonly string[] PaletteTokens = ["--sky", "--lilac", "--sun", "--peach", "--mint", "--sage", "--rose", "--teal", "--sand", "--green", "--indigo", "--orchid"];

    [Fact(DisplayName = "Colour by feature gives the first twelve features twelve different colours, in the board's feature order, and the legend matches the cards")]
    public void ColourByFeatureGivesTwelveDifferentColours()
    {
        DateOnly today = new(2026, 11, 4);
        Feature[] fifteen = [.. Enumerable.Range(1, 15).Select(n => new Feature($"area/f{n:00}", $"Feature {n:00}", "Area"))];
        WorkItem[] items = [.. fifteen.Select((f, n) => W(n + 1, "Backlog", [f.Path], []))];
        BoardOptions o = new(ColorBy: "feature");

        IReadOnlyDictionary<string, string> map = BoardModel.ColourMap("feature", items, fifteen);
        string[] cards = [.. items.Select(i => BoardModel.ColourOf(i, "feature", today, map))];
        Assert.Equal(PaletteTokens, cards.Take(12));            // the board's feature order picks the colours, none twice
        Assert.Equal(PaletteTokens.Take(3), cards.Skip(12));    // only after twelve do they cycle

        // the legend shows each feature in the colour its card has
        IReadOnlyList<(string Label, string Colour)> legend = BoardModel.Legend(items, fifteen, o, today);
        Assert.Equal(fifteen.Select(f => f.Name), legend.Select(l => l.Label));
        Assert.Equal(cards, legend.Select(l => l.Colour));

        // a value that is not in the map falls back to a stable pick from the palette
        string fallback = BoardModel.ColourOf(items[0], "feature", today);
        Assert.Contains(fallback, PaletteTokens);
        Assert.Equal(fallback, BoardModel.ColourOf(items[0], "feature", today, new Dictionary<string, string>()));

        // other fields have no map
        Assert.Empty(BoardModel.ColourMap("status", items, fifteen));
    }

    [Fact(DisplayName = "Colour by contributor gives each person a different colour, stable when a filter hides some of them")]
    public void ColourByContributorGivesEachPersonAColour()
    {
        DateOnly today = new(2026, 11, 4);
        IReadOnlyDictionary<string, string> map = BoardModel.ColourMap("contributor", Items, Features);
        Assert.Equal(["Artyom", "Mira", "Tom"], map.Keys.Order());   // the people list is every contributor of every item
        Assert.Equal(3, map.Values.Distinct().Count());
        Assert.Equal(PaletteTokens.Take(3), new[] { "Artyom", "Mira", "Tom" }.Select(p => map[p]));   // in the people list's order

        // a card takes its first contributor's colour, and the legend shows the same
        string Card(int id) => BoardModel.ColourOf(Items.Single(i => i.Id == id), "contributor", today, map);
        Assert.Equal([map["Tom"], map["Artyom"], map["Artyom"], map["Mira"]], [Card(1), Card(2), Card(3), Card(4)]);
        Assert.Equal([("Artyom", map["Artyom"]), ("Mira", map["Mira"]), ("Tom", map["Tom"])], BoardModel.Legend(Items, Features, new BoardOptions(ColorBy: "contributor"), today));

        // a filter that hides some people does not repaint the others
        BoardOptions onlyTom = new(ColorBy: "contributor", Who: "Tom");
        IReadOnlyList<(string Label, string Colour)> filtered = BoardModel.Legend(Items, Features, onlyTom, today);
        Assert.Contains(("Tom", map["Tom"]), filtered);
        Assert.Equal(PaletteTokens[2], map["Tom"]);   // the third in the people list, not the first of those shown
        Assert.DoesNotContain(filtered, l => l.Colour != map[l.Label]);
    }

    [Fact(DisplayName = "Every palette colour has a light and a dark value")]
    public void EveryPaletteColourHasLightAndDarkValues()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "app.css")).Replace("\r\n", "\n");
        const string darkMedia = "@media (prefers-color-scheme: dark) { :root:not([data-theme=\"light\"])", darkTheme = ":root[data-theme=\"dark\"]";
        int media = css.IndexOf(darkMedia, StringComparison.Ordinal), theme = css.IndexOf(darkTheme, StringComparison.Ordinal);
        Assert.True(media > 0 && theme > media);
        string light = css[..media], dark = css[media..theme], forced = css[theme..css.IndexOf('}', theme)];   // the light :root, the system dark theme, the chosen dark theme
        foreach (string token in PaletteTokens)
            Assert.All(new[] { light, dark, forced }, block => Assert.Matches($@"{token}:\s*#[0-9a-fA-F]{{6}}\b", block));
        Assert.Matches(@"--stone:\s*#[0-9a-fA-F]{6}\b", light);   // the neutral colour stays outside the palette
    }

    [Fact(DisplayName = "Rows follow the board's saved order and leave out hidden rows, keyed by the row field and value")]
    public void RowsFollowTheSavedOrderAndLeaveOutHiddenRows()
    {
        BoardOptions byLabel = new(Swimlane: "label");
        string[] Keys(BoardOptions o) => [.. BoardModel.Swimlanes(Build(o).Single()).Select(r => r.Key)];
        string Row(string field, string value) => BoardModel.RowKey(field, value);
        Assert.Equal("row:label:bug", Row("label", "bug"));
        Assert.Equal(["~", "bug", "feature", "enhancement"], Keys(byLabel));   // the usual order

        // the saved order wins; rows it doesn't list keep their usual places after it
        BoardOptions ordered = byLabel with { Order = [Row("label", "feature"), Row("label", "bug")] };
        Assert.Equal(["feature", "bug", "~", "enhancement"], Keys(ordered));
        Assert.Equal([1, 3], BoardModel.Swimlanes(Build(ordered).Single())[0].Items.Select(i => i.Id).Order());   // a row keeps its items when it moves

        // a hidden row is left out, with its items, but only for its own row field and never as a column
        BoardOptions hidden = ordered with { Hidden = [Row("label", "bug"), Row("priority", "high"), Row("status", "Backlog")] };
        Assert.Equal(["feature", "~", "enhancement"], Keys(hidden));
        Assert.DoesNotContain(2, BoardModel.Swimlanes(Build(hidden).Single()).SelectMany(r => r.Items).Select(i => i.Id));
        Assert.Contains("Backlog", Build(hidden).Single().Columns.Select(c => c.Key));
        Assert.Equal(["low"], Keys(hidden with { Swimlane = "priority" }));   // priority rows have their own hidden list: its "high" row is hidden by its own key, the label ones no longer matter
        Assert.Equal(["high", "low"], Keys(ordered with { Swimlane = "priority" }));   // and the label order doesn't touch them
        Assert.Equal([("row:label:bug", "bug", 1)], BoardModel.HiddenRows(Items, Features, Lanes, hidden));   // what the side menu lists to bring back

        // Insert (a drag) and StepKeys (the arrow keys) move row keys through the same list
        string[] all = [Row("label", "~"), Row("label", "bug"), Row("label", "feature"), Row("label", "enhancement")];
        Assert.Equal(["~", "feature", "bug", "enhancement"], Keys(BoardModel.Insert(byLabel, all, [Row("label", "feature")], Row("label", "bug"))));
        Assert.Equal(["bug", "~", "feature", "enhancement"], Keys(BoardModel.StepKeys(byLabel, all, [Row("label", "bug")], -1)));
        Assert.Equal(["~", "bug", "enhancement", "feature"], Keys(BoardModel.StepKeys(byLabel, all, [Row("label", "feature")], +1)));
        Assert.Same(byLabel, BoardModel.StepKeys(byLabel, all, [Row("label", "~")], -1));   // already first: nothing changes
    }

    [Fact(DisplayName = "An item counts as changed in a period when an event, a note, its creation or a file write falls in it")]
    public void ChangedInAPeriod()
    {
        DateOnly from = new(2026, 11, 2), to = new(2026, 11, 8);
        WorkItem Plain(int id) => W(id, "Backlog", [], []);   // created in September, files last written in October: outside the period
        WorkItem[] items =
        [
            Plain(1),
            Plain(2),
            Plain(3),
            Plain(4) with { Created = new DateTime(2026, 11, 5, 13, 0, 0) },
            Plain(5) with { Modified = new DateTime(2026, 11, 2, 0, 0, 0) },
            Plain(6),
            Plain(7),
            Plain(8),
        ];
        Dictionary<int, DateTime[]> logged = new()
        {
            [2] = [new DateTime(2026, 11, 8, 23, 59, 59)],                                    // an event or note on the last day, any time of it: in
            [3] = [new DateTime(2026, 11, 2, 0, 0, 0)],                                       // on the first day, at midnight: in
            [6] = [new DateTime(2026, 11, 1, 23, 59, 59), new DateTime(2026, 11, 9, 0, 0, 0)],   // the day before and the day after: out
            [7] = [new DateTime(2026, 11, 4, 9, 0, 0)],
        };
        IReadOnlySet<int> changed = BoardModel.ChangedIn(items, i => logged.GetValueOrDefault(i.Id) ?? [], from, to);
        Assert.Equal([2, 3, 4, 5, 7], changed.Order());   // 1, 6 and 8 did nothing in it

        Assert.Empty(BoardModel.ChangedIn(items, _ => [], new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 7)));
        Assert.Equal([4], BoardModel.ChangedIn(items, _ => [], new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5)));   // one day: from == to
    }

    [Fact(DisplayName = "With a changed period set, items outside it are hidden on board, list and roadmap and items inside stay; without it nothing changes")]
    public void ChangedPeriodHidesTheRest()
    {
        DateOnly today = new(2026, 11, 4);
        // the items were created in September and last written on the "day" of October each has: 2 on the 5th, 3 on the 9th, 1 on the 3rd, 4 on the 1st
        BoardOptions all = new(), period = new(ChangedFrom: new DateOnly(2026, 10, 5), ChangedTo: new DateOnly(2026, 10, 9));
        int[] Ids(IEnumerable<WorkItem> items) => [.. items.Select(i => i.Id).Order()];

        // board columns, with their counts, and the shared filter the Analytics view charts
        Assert.Equal([1, 2, 3, 4], Ids(BoardModel.Build(Items, Features, Lanes, all, today).SelectMany(s => s.Columns).SelectMany(c => c.Items)));
        BoardSection board = Assert.Single(BoardModel.Build(Items, Features, Lanes, period, today));
        Assert.Equal(["In Progress"], board.Columns.Where(c => c.Items.Count > 0).Select(c => c.Label));   // the columns stay, only In Progress holds items
        Assert.Equal([2, 3], Ids(board.Columns.Single(c => c.Label == "In Progress").Items));
        Assert.Equal([2, 3], Ids(BoardModel.Filtered(Items, period, today)));
        Assert.Equal([1, 2, 3, 4], Ids(BoardModel.Filtered(Items, all, today)));

        // list rows and the calendar's groups come from the same sections
        Assert.Equal([2, 3], Ids(BoardModel.Rows([board], new Dictionary<string, string>()).Where(r => r.Item is not null).Select(r => r.Item!)));
        Assert.Equal([2, 3], Ids(RoadmapModel.Groups([new RoadmapSource("/repo", "repo", [board])], false).SelectMany(g => g.Rows).Select(r => r.Item)));

        // hidden-column counts follow the filter, and the colours still in use are the shown items'
        Assert.Equal(2, BoardModel.HiddenCounts(Items, Features, Lanes, period with { Hidden = ["status:In Progress"] })["status:In Progress"]);
        Assert.Equal(0, BoardModel.HiddenCounts(Items, Features, Lanes, period with { Hidden = ["status:Completed"] })["status:Completed"]);   // 4 did not change in it
        Assert.Equal([("In Progress", "--sun")], BoardModel.Legend(Items, Features, period, today));
        Assert.Equal([("Backlog", "--stone"), ("In Progress", "--sun"), ("Completed", "--sage")], BoardModel.Legend(Items, Features, all, today));

        // the other filters still apply with it, and the count says how many the period leaves of those they leave
        Assert.Equal([2], Ids(BoardModel.Filtered(Items, period with { Who = "Tom" }, today)));   // Tom has 1 and 2; only 2 changed
        Assert.Equal(((int, int)?)(2, 4), BoardModel.ShownCount(Items, period, today));
        Assert.Equal(((int, int)?)(1, 2), BoardModel.ShownCount(Items, period with { Who = "Artyom", ChangedTo = new DateOnly(2026, 10, 5) }, today));   // Artyom has 2 and 3; only 2 changed on the 5th
        Assert.Null(BoardModel.ShownCount(Items, all, today));

        // options: both days or neither
        Assert.Null(BoardModel.Changed(Items, new BoardOptions()));
        Assert.Null(BoardModel.Changed(Items, new BoardOptions(ChangedFrom: new DateOnly(2026, 11, 2))));
        Assert.False(new BoardOptions().ChangedOn);
        Assert.True(new BoardOptions(ChangedFrom: new DateOnly(2026, 11, 2), ChangedTo: new DateOnly(2026, 11, 8)).ChangedOn);
    }
}
