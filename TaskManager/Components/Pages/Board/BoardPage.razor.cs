using Features;
using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using TaskManager.Components.Pages;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;
using WorkItems.PublicDTOs;
using TaskManager.AppState;

namespace TaskManager.Components.Pages;

public partial class BoardPage
{
    [Inject] private SettingsStore Settings { get; set; } = default!;

    /// <summary>One open repository on the board: its items and features, and the columns they fall into under the current options.</summary>
    private sealed record Project(string Root, string Name, IReadOnlyList<WorkItem> Items, IReadOnlyList<Feature> Features,
        IReadOnlyDictionary<string, string> Names, IReadOnlyList<BoardSection> Sections, IReadOnlyDictionary<string, string> Colours, IReadOnlyList<string> Statuses,
        IReadOnlySet<int> Dirty,   // the items with uncommitted changes (the card's marker; empty while a commit is chosen)
        IReadOnlySet<int> Failing)   // the items a linked test of which failed in the newest results (the card's marker)
    {
        public IEnumerable<(string? Category, BoardColumn Column)> Flat => Sections.SelectMany(s => s.Columns.Select(c => (s.Category, c)));
    }

    private BoardOptions O = new();
    /// <summary>The options as this view reads them: the list's own groups and subgroups (flat until chosen) stand in for the board's columns and rows.</summary>
    private BoardOptions Opt => View == "list" ? O.ForList() : O;

    /// <summary>True when the layout shows this page as the left half of a split screen (an open work item on the right): always the list.</summary>
    [Parameter] public bool Embedded { get; set; }
    /// <summary>The work item open on the right of a split screen; its row is marked.</summary>
    [Parameter] public int? Current { get; set; }
    private string View => Embedded ? "list" : O.View;
    private List<Project> projects = [];   // the visible projects (open tabs not hidden), in tab order
    private readonly HashSet<(string Root, int Id)> selected = [];   // a number is only unique within its project
    private readonly HashSet<string> selectedProjects = [];          // project headings Shift-clicked, to move with the arrow keys

    /// <summary>Shift / Ctrl / ⌘-click on a project's heading selects it, as a card is selected; the arrow keys then move it.</summary>
    private void ProjectClick(string root, MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) return;
        selected.Clear();   // projects, rows or cards, never mixed: the arrow keys move one kind
        selectedRows.Clear();
        selectedColumns.Clear();
        Toggle(selectedProjects, root);
    }

    private Action<MouseEventArgs>? backgroundClickHandler;
    /// <summary>The wrapper's click: one delegate for the page's life, and it does not render the page unless it changed something.</summary>
    private Action<MouseEventArgs> BackgroundClickHandler => backgroundClickHandler ??= EventUtil.AsNonRenderingEventHandler<MouseEventArgs>(BackgroundClick);

    /// <summary>A plain click on the board or list (not on a project heading) lets go of the selected projects and rows.</summary>
    private void BackgroundClick(MouseEventArgs e)
    {
        bool onItem = itemClicked;   // the click came up from a card or row: it examined or opened it, so it is not "somewhere else"
        itemClicked = false;
        if (e.ShiftKey || e.CtrlKey || e.MetaKey) return;
        bool held = selectedProjects.Count + selectedRows.Count + selectedColumns.Count > 0;
        selectedProjects.Clear();
        selectedRows.Clear();
        if (!onItem) Popups.Examine(null);   // redraws by ExamineChanged when something was examined
        selectedColumns.Clear();
        if (held) StateHasChanged();   // the handler does not render (see the markup): only a selection let go is a change to show
    }

    private readonly HashSet<string> selectedColumns = [];   // status column headers Shift-clicked in the shared header row (their keys), to merge

    /// <summary>Shift / Ctrl / ⌘-click on a status column's header selects it (a plain click opens its menu); "Merge" in the menu then folds the selected ones into one column.</summary>
    private void ColumnClick(string key, MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) return;
        selected.Clear();   // columns, projects, rows or cards, never mixed
        selectedProjects.Clear();
        selectedRows.Clear();
        Toggle(selectedColumns, key);
    }

    /// <summary>The columns of the shared header row, in the order shown.</summary>
    private List<string> HeadColumns => projects.Count == 0 ? [] : [.. projects[0].Sections[0].Columns.Select(c => c.Key)];

    /// <summary>"Merge" on a selected header: the selected columns become one, called after the first of them unless another name is typed. Saved with the board, in the user's settings.</summary>
    private async Task MergeSelected()
    {
        List<string> picked = [.. HeadColumns.Where(selectedColumns.Contains)];   // in column order; taken before the dialog, as a click elsewhere lets go of the selection
        if (picked.Count < 2) return;
        if (await Popups.Prompt("Merge columns", picked[0], "Merge", picked[0]) is not string name || name.Trim().Length == 0) return;
        selectedColumns.Clear();
        Set(BoardModel.Merge(O, picked, name));
    }

    /// <summary>"Merge with Completed": the column and every column that looks like it (case, spaces, dashes) in one click, named after the fixed status.</summary>
    private void MergeLookAlikes((string Fixed, IReadOnlyList<string> Columns) look)
    {
        selectedColumns.Clear();
        Set(BoardModel.Merge(O, look.Columns, look.Fixed));
    }

    private void Unmerge(string name)
    {
        selectedColumns.Remove(name);
        Set(BoardModel.Unmerge(O, name));
    }

    private readonly HashSet<string> selectedRows = [];   // swimlane headers Shift-clicked (BoardModel.RowKey), to move with the arrow keys
    private (IReadOnlyList<string> Siblings, IReadOnlyList<string> Moving)? draggingRow;   // a swimlane header being dragged, or every selected one

    private List<string> RowKeys(BoardSection sec) => [.. BoardModel.Swimlanes(sec).Select(r => BoardModel.RowKey(Opt.Swimlane, r.Key))];

    /// <summary>Shift / Ctrl / ⌘-click on a swimlane header selects it, as on a project heading; the arrow keys then move it.</summary>
    private void RowClick(string key, MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) return;   // a plain click is the board's (BackgroundClick): it lets go
        selected.Clear();
        selectedProjects.Clear();
        selectedColumns.Clear();
        Toggle(selectedRows, key);
    }

    private void HideRow(string key)
    {
        selectedRows.Remove(key);
        Set(BoardModel.ToggleHidden(O, key));   // the side menu's Hidden list brings it back
    }

    /// <summary>A header drag carries every selected header when the dragged one is part of the selection, as a card drag does.</summary>
    private void StartRowDrag(IReadOnlyList<string> siblings, string key)
    {
        dragging = null;
        draggingColumn = null;
        draggingRow = (siblings, selectedRows.Contains(key) ? [.. siblings.Where(selectedRows.Contains)] : [key]);
    }

    /// <summary>The dragged headers dropped in front of a row (<paramref name="before"/>), or after the last one (null).</summary>
    private void DropRow(string? before)
    {
        if (draggingRow is not (IReadOnlyList<string> siblings, IReadOnlyList<string> moving)) return;
        draggingRow = null;
        if (before is null || siblings.Contains(before)) Set(BoardModel.Insert(O, siblings, moving, before));
    }

    /// <summary>Arrow keys on selected swimlane headers: up / down past the unselected neighbour.</summary>
    private void MoveRows(int dir)
    {
        List<List<string>> rowsOfProjects = [.. projects.SelectMany(p => p.Sections).Select(RowKeys)];
        if (rowsOfProjects.FirstOrDefault(r => selectedRows.All(r.Contains)) is { } siblings)
            Set(BoardModel.StepKeys(O, siblings, [.. siblings.Where(selectedRows.Contains)], dir));
    }

    private (Project From, IReadOnlyList<(WorkItem Item, string Column, string? Row)> Cards)? dragging;   // the dragged card, or every selected card of its project, with the column and row each is dragged from
    private (IReadOnlyList<string> Siblings, string Key)? draggingColumn;   // a column header or group name being dragged

    internal static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List"), ("calendar", "Calendar"), ("analytics", "Analytics")];
    internal static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    // One field list for the three Organise options: columns take any of them, rows and colour also "none".
    internal static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("feature", "feature"), ("label", "label"), ("priority", "priority"), ("contributor", "contributor"), ("due", "due date"), ("started", "start date")];
    internal static readonly IReadOnlyList<(string, string)> Swimlanes = [("none", "none"), .. GroupBys];
    internal static readonly IReadOnlyList<(string, string)> ListGroupBys = [("none", "none"), .. GroupBys];   // the list's groups and subgroups: none (flat) or any field
    internal static readonly IReadOnlyList<(string, string)> ColorBys = [.. GroupBys, ("none", "none")];
    internal static readonly IReadOnlyList<(string, string)> Periods =[("all", "any time"), ("overdue", "overdue"), ("today", "today"), ("week", "this week"), ("month", "this month"), ("none", "no due date")];
    internal static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("label", "label"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id"), ("manual", "my order")];

    private IReadOnlyList<(string, string)> People =>
        [("all", "everyone"), .. projects.SelectMany(p => p.Items).SelectMany(i => i.Contributors).Distinct().Order().Select(p => (p, p))];

    private bool Multi => projects.Count > 1;

    /// <summary>Several projects with the same columns (grouped by status or label): one header row above serves them all.</summary>
    private bool Aligned => Multi && Opt.Swimlane == BoardModel.NoSwimlane && projects.All(p => p.Sections.Count == 1)
        && projects.Select(p => string.Join("|", p.Sections[0].Columns.Select(c => c.Key))).Distinct().Count() == 1;

    private IReadOnlyList<string> HeadKeys => projects[0].Sections[0].Columns.Select(c => BoardModel.ColumnKey(Opt, c.Key)).ToList();

    /// <summary>A project folded to its heading, on the board and in the list; "show all hidden" does not unfold it, and its items count in no column header.</summary>
    private bool Collapsed(Project p) => Settings.ProjectCollapsed(p.Root);
    private bool IsActive(Project p) => Workspace.Root is string r && SettingsStore.Key(r) == SettingsStore.Key(p.Root);
    /// <summary>The project a shared control (the header row's "+ Add work item") acts on: the active one, else the first shown.</summary>
    private Project? Active => projects.FirstOrDefault(IsActive) ?? projects.FirstOrDefault();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Settings.Changed += OnSettings;
        Popups.ExamineChanged += OnExamineChanged;
        Nav.LocationChanged += OnLocation;
        if (Workspace.ViewRoot is string root) O = Settings.Board(root);
        projectState = ProjectState;
    }

    // The side menu edits the view options, and the hidden / collapsed projects, through the settings store. Other settings
    // (the grid's own layout, the theme) change nothing here, so they cause no reload.
    private string projectState = "";
    private string ProjectState => string.Join(";", Settings.Current.OpenRepos ?? []) + "|" + string.Join(";", Settings.Current.HiddenProjects ?? []) + "|" + string.Join(";", Settings.Current.CollapsedProjects ?? []);   // tab order, hidden, collapsed

    private void OnSettings() => InvokeAsync(() =>
    {
        BoardOptions o = Workspace.ViewRoot is string root ? Settings.Board(root) : O;
        if (o == O && ProjectState == projectState) return;
        O = o;
        projectState = ProjectState;
        Load();
        StateHasChanged();
    });

    /// <summary>The board reads once, then on a workspace or settings change: its parameters (embedded, the open item) only mark rows.</summary>
    protected override object? LoadKey => "board";

    protected override void Load()
    {
        List<string> roots = [.. Workspace.Projects.Where(r => O.ShowHidden || !Settings.ProjectHidden(r))];
        var read = roots.Select(r => Workspace.In(r, () => (Items: ItemsCache.All(includeHidden: O.ShowHidden), Features: FeaturesCache.Get(), Names: Workspace.FeatureNames,
            Lanes: WorkItemsApi.GetLanes(), Statuses: WorkItemsApi.GetStatuses()))).ToList();
        // the status columns: each project's own list, or with several projects the union of their lists with the user's merges folded in
        IReadOnlyList<FieldValue> statusColumns = BoardModel.StatusColumns(read.Select(x => x.Statuses), roots.Count > 1 ? O.Merges : null);
        // Build and the uncommitted marker read the project's own git history and files (the commit filter), so they run inside it
        projects = roots.Select((r, n) => new Project(r, Path.GetFileName(r), read[n].Items, read[n].Features, read[n].Names,
            Workspace.In(r, () => BoardModel.Build(read[n].Items, read[n].Features, read[n].Lanes, Opt, Today, statusColumns)), BoardModel.ColourMap(O.ColorBy, read[n].Items, read[n].Features), read[n].Statuses,
            O.Commit == "any" ? Workspace.In(r, () => BoardModel.Uncommitted(read[n].Items)) : new HashSet<int>(),
            Workspace.In(r, () => FailingTests(read[n].Items)))).ToList();
        loadVersion++;
        cardHandlers.Clear();   // the callbacks act on this load's project and columns; the cards are drawn again for the new ones
        selectedColumns.RemoveWhere(k => !HeadColumns.Contains(k));
        selected.RemoveWhere(s => !projects.Any(p => p.Root == s.Root && p.Items.Any(i => i.Id == s.Id)));
        cachedFor = null;   // the examined item's neighbours are read again
        if (!Embedded && Popups.Examined is (string xr, int xi) && !projects.Any(p => p.Root == xr && p.Items.Any(i => i.Id == xi))) Popups.Examine(null);
        selectedProjects.RemoveWhere(r => !Multi || !projects.Any(p => p.Root == r));
        selectedRows.RemoveWhere(k => !projects.Any(p => p.Sections.Any(s => RowKeys(s).Contains(k))));   // a hidden row, or another row field
        rows = [.. projects.SelectMany((p, n) => BoardModel.Rows(p.Sections, p.Names, p.Root, p.Name, n * 1000))];
        groups = rows.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => (g.First().Column, g.First().Category, g.First().Root));
        if (gridSettings is null && O.ListSettings is string json)
            try { gridSettings = System.Text.Json.JsonSerializer.Deserialize<DataGridSettings>(json); ForgetVisibility(gridSettings); } catch (System.Text.Json.JsonException) { }
        if (grid is null) FillGroups();   // before the grid's first render: grouped from the start, not flat first and regrouped after it (a second pass over every row)
        if (grid is null && gridSettings is not null) gridSettings.Groups = [.. groupDescriptors];   // the grouping saved with the settings was made for the projects open then; the grid would apply it over this one (a column group where a project group is expected stops the page)
        listVersion++;   // the grid draws again for these rows, columns and options
    }

    /// <summary>What the list's grid shows is stamped: Load (rows, columns, options, projects) bumps it, a selection or an examine does not. It is the <see cref="RenderBoundary"/>'s version.</summary>
    private long listVersion;

    private readonly System.Collections.ObjectModel.ObservableCollection<GroupDescriptor> groupDescriptors = [];

    /// <summary>Groups by project (when several), column, then row value (when set); the group panel stays hidden. The grid is given this collection (Groups).</summary>
    private void FillGroups()
    {
        groupedMulti = Multi;
        groupedSwim = Opt.Swimlane;
        groupedCols = Opt.GroupBy != BoardModel.NoSwimlane;
        groupDescriptors.Clear();
        if (Multi) groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Project), Title = "Project" });
        if (groupedCols) groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Group), Title = "Column" });
        if (Opt.Swimlane != BoardModel.NoSwimlane) groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Swim), Title = "Row" });
    }

    /// <summary>The items with a linked test that failed in the newest results file: read once per load (the cards do not read it), none when no item links a test.</summary>
    private static HashSet<int> FailingTests(IReadOnlyList<WorkItem> items)
    {
        if (!items.Any(i => i.Tests is { Count: > 0 })) return [];
        HashSet<(string, string)> failed = [.. TestResultsCache.Get().Results.Where(r => r.Outcome == "failed").Select(r => (r.Suite, r.Test))];
        return [.. items.Where(i => i.Tests is { Count: > 0 } links && links.Any(t => failed.Contains((t.Suite, t.Test)))).Select(i => i.Id)];
    }

    /// <summary>Stamps what the board's cards were made for: a load makes new items, columns and callbacks, so every card is drawn again for it (and only then).</summary>
    private long loadVersion;

    /// <summary>One card's callbacks, kept for the load (a card draws only when its key changes, so the callbacks it holds must stay the ones of its load).</summary>
    private sealed record CardHandlers(EventCallback<MouseEventArgs> Click, EventCallback<WorkItem> DragStart, EventCallback<WorkItem> Drop, EventCallback<WorkItem> Review);

    private readonly Dictionary<(string Root, int Id, string Column, string? Swim), CardHandlers> cardHandlers = [];

    private CardHandlers HandlersOf(Project p, BoardColumn col, WorkItem it, string? swim)
    {
        if (cardHandlers.TryGetValue((p.Root, it.Id, col.Key, swim), out CardHandlers? h)) return h;
        return cardHandlers[(p.Root, it.Id, col.Key, swim)] = new CardHandlers(
            EventCallback.Factory.Create<MouseEventArgs>(this, e => CardClick(p, it, e)),
            EventCallback.Factory.Create<WorkItem>(this, i => StartCardDrag(p, i, col.Key, swim)),
            EventCallback.Factory.Create<WorkItem>(this, (Func<WorkItem, Task>)(t => DropBefore(p, col, t, swim))),
            EventCallback.Factory.Create<WorkItem>(this, i => ReviewItem(p, i)));
    }

    /// <summary>A heading's click that only selects (Shift / Ctrl / ⌘): it draws the page when it did, and a plain click, which changes nothing, draws nothing.</summary>
    private Action<MouseEventArgs> Quiet(Action<MouseEventArgs> select) => EventUtil.AsNonRenderingEventHandler<MouseEventArgs>(e => { if (e.ShiftKey || e.CtrlKey || e.MetaKey) { select(e); StateHasChanged(); } });

    private Project ProjectOf(string root) => projects.First(p => p.Root == root);

    private void Set(BoardOptions options)
    {
        O = options;
        if (Workspace.ViewRoot is string root) Settings.SaveBoard(root, O);
        Load();
    }

    // ── Calendar view: the Roadmap component draws the board's columns as groups, or the rows with the columns inside them ──
    private IReadOnlyList<RoadmapSource> RoadmapProjects => projects.Select(p => new RoadmapSource(p.Root, p.Name, p.Sections, p.Colours)).ToList();
    private void OpenFrom((string Root, int Id) at) => ItemClick(ProjectOf(at.Root), at.Id);
    private void CalendarBackground() { if (!itemClicked) Popups.Examine(null); itemClicked = false; }
    private Action? calendarBackground;
    /// <summary>The calendar's background click: one delegate for the page's life, and it draws nothing itself (letting go of the examined item draws by ExamineChanged).</summary>
    private Action CalendarBackgroundHandler => calendarBackground ??= EventUtil.AsNonRenderingEventHandler(CalendarBackground);

    // ── Analytics view: the Analytics component charts the active project's items (after the contributor, agent and period filters) ──
    private IReadOnlyList<WorkItem> AnalyticsItems => Active is { } p ? BoardModel.Filtered(p.Items, O, Today) : [];
    private IReadOnlyList<WorkItemEvent> AnalyticsEvents(int id) => Active is { } p ? Workspace.In(p.Root, () => ItemsCache.Events(id)) : [];
    private void SetCharts(IReadOnlyList<ChartSpec> charts) => Set(O with { Charts = charts });

    // ── List view: a Radzen DataGrid over the projects' columns ──
    private RadzenDataGrid<GridRow>? grid;
    private IReadOnlyList<GridRow> rows = [];
    private Dictionary<int, (BoardColumn Column, string? Category, string Root)> groups = [];
    private string? groupedSwim;                      // the swimlane field the grid's group descriptors include
    private DataGridSettings? gridSettings;   // column widths, order, visibility, sort and filters, remembered per repository
    private readonly HashSet<string> shutGroups = [];   // collapsed groups, by project and column key (and row value, for a row-value group), kept across re-renders and data reloads
    private bool? groupedMulti;               // whether the grid's group descriptors include the project level
    private bool groupedCols = true;          // ... and the column level (the list's groups; none = flat, or only projects and subgroups)
    private bool? subtasksApplied;            // the ShowSubtasks setting last pushed into the grid's expanded rows

    /// <summary>A column is shown unless hidden by the user, or it repeats what the rows are grouped by.</summary>
    /// <summary>The day by the app's clock.</summary>
    private DateOnly Today => Ui.Today(Time);

    private static string Day(DateTime? d) => d is { } v ? Ui.Day(DateOnly.FromDateTime(v)) : "";
    private static string Stamp(DateTime? d) => d is { } v ? Ui.Stamp(v) : "";   // "15:34. 16 Nov 26"

    private bool Shown(string column) => !BoardModel.Concealed(O,BoardModel.ListColumnKey(column)) && (column == "Status" || (column != BoardModel.GroupedColumn(Opt) && column != BoardModel.SwimColumn(Opt)));   // Status always stays: the organising operators work on it

    // Asks what the grid is grouped by, not Multi: after the project list changes, the grid renders once with its old group
    // descriptors (OnAfterRenderAsync swaps them), and a group keyed by column read as a project group throws a cast in the render.
    private bool IsProjectGroup(Group g) => groupedMulti == true && g.Level == 0;

    /// <summary>With rows set the grid groups project (when several) → column → row value; this is the row-value level, inside a column.</summary>
    private bool IsSwimGroup(Group g) => groupedSwim is not (null or BoardModel.NoSwimlane) && g.Level == (groupedMulti == true ? 1 : 0) + (groupedCols ? 1 : 0);

    /// <summary>The last level of groups, the one that holds the item rows (and gets the "+ Add item" footer).</summary>
    private bool IsLeafGroup(Group g) => !IsProjectGroup(g) && (groupedSwim is null or BoardModel.NoSwimlane || IsSwimGroup(g));

    /// <summary>The list rows under a group (a row-value group's header shows its name and how many items it holds).</summary>
    private List<GridRow> SwimRowsOf(Group g) => [.. LeafRows((GroupResult)g.Data)];

    // a group above the last level holds subgroups, the last level holds the rows
    private static IEnumerable<GridRow> LeafRows(GroupResult r) =>
        r.Items is { } items ? items.OfType<GridRow>() : (r.Subgroups ?? []).SelectMany(LeafRows);

    private string ShutKey(Group g) =>
        IsSwimGroup(g) ? SwimRowsOf(g).FirstOrDefault() is { } r ? r.Root + "|" + r.Column.Key + "|swim|" + (string)g.Data.Key : ""
        : GroupOf(g) is var (col, _, root) ? root + "|" + col.Key : "";

    private (BoardColumn Column, string? Category, string Root) GroupOf(Group g) => groups[Convert.ToInt32((object)g.Data.Key)];

    /// <summary>The row value a drop on a list row lands in (null without rows).</summary>
    private static string? SwimOf(GridRow row) => row.Swim.Length > 0 ? row.Swim : null;

    /// <summary>How many times <see cref="RowRender"/> has run: the grid asks once per row on every pass, so tests read the cost of a click as a delta of this.</summary>
    public int RowRenders { get; private set; }

    /// <summary>Marks the row for the drag script, cell copy and styling (what the data says), and gives it the board's drop handlers.</summary>
    private void RowRender(RowRenderEventArgs<GridRow> args)
    {
        RowRenders++;
        GridRow row = args.Data!;   // Radzen declares it nullable; a row always has one
        args.Expandable = false;   // the row's own chevron expands it
        Project p = ProjectOf(row.Root);
        if (row.Item is not WorkItem it)
        {
            args.Attributes["data-empty"] = "";   // an empty column's placeholder: dropping on it lands in that column
            args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(p, row.Column, SwimOf(row)));
            return;
        }
        args.Attributes["data-id"] = it.Id;
        args.Attributes["data-row"] = row.Key;
        args.Attributes["style"] = $"--band:var({BoardModel.ColourOf(it, O.ColorBy, Today, p.Colours)})";   // the row's colour (app.css), as a card's band
        if (O.ColorBy != "none") args.Attributes["data-tint"] = "";   // a wash of it over the whole row
        if (it.Hidden) args.Attributes["data-hidden"] = "";   // selected, examined and open beside the list are not here: RowMarks marks them, so the grid does not draw again for them
        args.Attributes["onclick"] = EventUtil.AsNonRenderingEventHandler<MouseEventArgs>(e =>   // the board's select click, anywhere on the row; it renders only when it changed something
        {
            if (e.ShiftKey || e.CtrlKey || e.MetaKey) { ToggleSelect(p, it); StateHasChanged(); }
            else RowItemClick(p, it.Id);   // examining redraws by ExamineChanged
        });
        args.Attributes["ondragstart"] = EventCallback.Factory.Create<DragEventArgs>(this, () => StartCardDrag(p, it, row.Column.Key, SwimOf(row)));
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => DropBefore(p, row.Column, it, SwimOf(row)));
    }

    /// <summary>The rows RowMarks marks: the selected ones, the examined one and what it waits on and needs, the one open beside the list, and the selected project headings.</summary>
    private IReadOnlyList<RowMarks.RowMark> ListMarks()
    {
        List<RowMarks.RowMark> marks = [];
        foreach (GridRow row in rows)
        {
            if (row.Item is not WorkItem it) continue;
            Project p = ProjectOf(row.Root);
            RowMarks.Mark m = RowMarks.Mark.None;
            if (selected.Contains((p.Root, it.Id))) m |= RowMarks.Mark.Selected;
            if (Embedded && Current == it.Id && IsActive(p)) m |= RowMarks.Mark.Current;
            m |= ExamineClass(p, it.Id) switch { "ex-on" => RowMarks.Mark.Examined, "ex-waits" => RowMarks.Mark.Waits, "ex-needs" => RowMarks.Mark.Needs, _ => RowMarks.Mark.None };
            if (m != RowMarks.Mark.None) marks.Add(new("data-row", row.Key, m));
        }
        foreach (string root in selectedProjects)
            if (projects.FirstOrDefault(p => p.Root == root) is { } gp) marks.Add(new("data-group", "proj:" + gp.Name, RowMarks.Mark.Selected));
        return marks;
    }

    /// <summary>A group row stands for its column: a drop on it lands at the column's end. A project row (several projects) only heads.</summary>
    private void GroupRowRender(GroupRowRenderEventArgs args)
    {
        if (IsProjectGroup(args.Group!))
        {
            args.Attributes["data-group"] = "proj:" + args.Group!.Data.Key;
            args.Attributes["data-path-proj"] = (string)args.Group!.Data.Key;   // the sticky header's path (app.js _updatePath)
            if (projects.FirstOrDefault(p => p.Name == (string)args.Group!.Data.Key) is { } gp)
            {
                args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => ProjectClick(gp.Root, e));   // Shift-click anywhere in the row selects the project
                if (reopenProjects.Contains(gp.Root)) args.Expanded = true;   // "Expand" from the name's menu: Radzen keeps its own state, so open it once
                else if (Collapsed(gp)) args.Expanded = false;
            }
            return;
        }
        if (IsSwimGroup(args.Group!))   // a row value inside a column: a drop on it lands in that column with that row value
        {
            if (SwimRowsOf(args.Group!).FirstOrDefault() is not { } first) return;
            string value = (string)args.Group!.Data.Key;
            args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(ProjectOf(first.Root), first.Column, value.Length > 0 ? value : null));
            if (value.Length == 0)   // an empty column's placeholder row has no row value: no heading, only the column above it
            {
                args.Attributes["data-group"] = "";
                args.Attributes["style"] = "display:none";
            }
            else
            {
                args.Attributes["data-group"] = "swim:" + value;
                args.Attributes["data-path-swim"] = first.SwimLabel;
                if (shutGroups.Contains(ShutKey(args.Group!))) args.Expanded = false;
            }
            return;
        }
        (BoardColumn col, _, string root) = GroupOf(args.Group!);
        args.Attributes["data-group"] = col.Key;
        args.Attributes["data-path-col"] = col.Label;
        if (Opt.GroupBy == "status") args.Attributes["data-path-fill"] = $"var({Ui.Band(col.Label)})";   // the status pill's colour, as the group header wears it
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(ProjectOf(root), col, null));   // the column's end; the item keeps its row value
        // only ever false: Radzen takes Expanded = true as "keep open" and ignores the chevron; the collapse/expand events keep shutGroups current
        if (shutGroups.Contains(ShutKey(args.Group!))) args.Expanded = false;
    }

    private readonly HashSet<string> reopenProjects = [];   // projects the name's menu just expanded: the list's group row is told to open once

    /// <summary>The name menu's Collapse / Expand: the board's chevron setting, and the list's group row follows it.</summary>
    private void ToggleProjectCollapsed(Project p)
    {
        if (Settings.ProjectCollapsed(p.Root)) reopenProjects.Add(p.Root);
        Settings.ToggleProjectCollapsed(p.Root);
    }

    /// <summary>The list's project chevron keeps the same setting as the board's.</summary>
    private void SetProjectCollapsed(Group g, bool collapsed)
    {
        if (projects.FirstOrDefault(p => p.Name == (string)g.Data.Key) is { } p && Settings.ProjectCollapsed(p.Root) != collapsed) Settings.ToggleProjectCollapsed(p.Root);
    }

    private async Task ToggleRow(GridRow row)
    {
        if (grid is null) return;
        if (grid.IsRowExpanded(row)) await grid.CollapseRows([row]); else await grid.ExpandRow(row);
    }

    /// <summary>Which columns show is the page's own (Shown, from the grouping and the user's hides): a visibility the grid saved with its layout would keep a column hidden after the grouping that dropped it is gone, so the saved one is brought to what Shown says now.</summary>
    private void ForgetVisibility(DataGridSettings? settings)
    {
        foreach (DataGridColumnSettings c in settings?.Columns ?? [])
            c.Visible = c.Property is not string prop || Shown(prop switch { "Id" => "ID", "Modified" => "Updated", "Moved" => "In status since", _ => prop });
    }

    private void SaveGridSettings(DataGridSettings settings)
    {
        ForgetVisibility(settings);
        gridSettings = settings;
        O = O with { ListSettings = System.Text.Json.JsonSerializer.Serialize(settings) };
        if (Workspace.ViewRoot is string root) Settings.SaveBoard(root, O);   // no Load: nothing on the board changed
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        reopenProjects.Clear();   // the list has drawn them open once; from here Radzen's own state keeps them so
        if (grid is null) return;
        bool grouping = Multi || Opt.GroupBy != BoardModel.NoSwimlane || Opt.Swimlane != BoardModel.NoSwimlane;   // nothing to group by: the flat list
        if ((grouping && grid.Groups.Count == 0) || groupedMulti != Multi || groupedSwim != Opt.Swimlane || groupedCols != (Opt.GroupBy != BoardModel.NoSwimlane))   // grouped by project (when several), column, then row value (when set); the group panel stays hidden
        {
            FillGroups();
            await grid.Reload();
            return;
        }
        if (subtasksApplied != O.ShowSubtasks)   // on: every row shows its subtasks; off: only rows opened by hand
        {
            subtasksApplied = O.ShowSubtasks;
            List<GridRow> withSubtasks = rows.Where(r => r.Item is { Subtasks.Count: > 0 }).ToList();
            // Radzen redraws the whole grid for either call, even with nothing to do: ask only when there is something to change, so a list that opens with no row to open or close draws once
            List<GridRow> change = [.. withSubtasks.Where(r => grid.IsRowExpanded(r) == !O.ShowSubtasks)];
            if (change.Count == 0) return;
            if (!O.ShowSubtasks) await grid.CollapseRows(change);
            else await grid.ExpandRows(change);
        }
    }

    private static void Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Remove(value)) set.Add(value);
    }

    /// <summary>Opens a work item's page; one from another project first makes that project the active one.</summary>
    private void OpenItem(Project p, int id)
    {
        if (!IsActive(p)) Workspace.Open(p.Root);
        Nav.NavigateTo($"item/{id}");
    }

    /// <summary>A click opens the card; Shift-click (or Ctrl/⌘-click) adds it to the selection or removes it. A drag fires no click.</summary>
    private void CardClick(Project p, WorkItem it, MouseEventArgs e)
    {
        if (e.ShiftKey || e.CtrlKey || e.MetaKey) ToggleSelect(p, it);
        else ItemClick(p, it.Id);
    }

    // ── Examine: with the side menu expanded a click on a card or row looks at the item (its metadata fills the menu), a second click opens it ──

    private bool itemClicked;   // a plain click on an item is on its way up to the board's BackgroundClick, which must not let go of it

    /// <summary>A plain click on a work item: opens it, or with the menu expanded examines it first (the split view's list keeps opening at once).</summary>
    private void ItemClick(Project p, int id)
    {
        itemClicked = true;
        if (Embedded || !Popups.MenuExpanded || Popups.Examined == (p.Root, id)) OpenItem(p, id);
        else Popups.Examine((p.Root, id));
    }

    /// <summary>A plain click on a list pill (or its picker) is the pill's: it must not examine the row, or let go of the examined one, on its way up.</summary>
    private void PillPressed() => itemClicked = true;

    /// <summary>A plain click anywhere on a list row that its title button did not take: examines the row's item when another one is examined.</summary>
    private void RowItemClick(Project p, int id)
    {
        if (itemClicked) return;
        itemClicked = true;
        if (!Embedded && Popups.MenuExpanded && Popups.Examined != (p.Root, id)) Popups.Examine((p.Root, id));
    }

    private (string Root, int Id)? cachedFor;
    private HashSet<int> waitingOnIt = [], itWaitsOn = [];

    /// <summary>The look of an item while one is examined: ex-on (yellow shadow), ex-waits (red: it waits on the examined item), ex-needs (blue: the examined item waits on it).</summary>
    private string? ExamineClass(Project p, int id)
    {
        if (Popups.Examined is not (string root, int examined) || root != p.Root) return null;
        if (cachedFor != Popups.Examined)
        {
            cachedFor = Popups.Examined;
            WorkItem? item = p.Items.FirstOrDefault(i => i.Id == examined);
            itWaitsOn = [.. item?.DependsOn ?? []];
            waitingOnIt = [.. p.Items.Where(i => i.DependsOn?.Contains(examined) == true).Select(i => i.Id)];
        }
        return id == examined ? "ex-on" : waitingOnIt.Contains(id) ? "ex-waits" : itWaitsOn.Contains(id) ? "ex-needs" : null;
    }

    private string? ExamineOf(string root, int id) => ExamineClass(ProjectOf(root), id);
    private void OnExamineChanged() { cachedFor = null; InvokeAsync(StateHasChanged); }
    private void OnLocation(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) => Popups.Examine(null);   // navigating away lets go

    /// <summary>The agent chip on a card or row: the user has looked, so the item and its agent subtasks stop being flagged.</summary>
    private void ReviewItem(Project p, WorkItem it)
    {
        Workspace.In(p.Root, () =>
        {
            if (it.AgentUnreviewed) WorkItemsApi.MarkReviewed(it.Id);
            foreach (Subtask s in it.Subtasks.Where(s => s.Agent is not null)) WorkItemsApi.MarkSubtaskReviewed(it.Id, s.Number);
        });
        Workspace.Notify();
        Popups.Saved();
    }

    private void ToggleSelect(Project p, WorkItem it)
    {
        selectedProjects.Clear();
        selectedRows.Clear();
        selectedColumns.Clear();
        Toggle(selected, (p.Root, it.Id));
    }

    private void SelectAll(Project p, BoardColumn col) => selected.UnionWith(col.Items.Select(i => (p.Root, i.Id)));

    /// <summary>The shared header's "select all": the column in every project.</summary>
    private void SelectAllProjects(string columnKey)
    {
        foreach (Project p in projects)
            if (p.Sections[0].Columns.FirstOrDefault(c => c.Key == columnKey) is BoardColumn col) SelectAll(p, col);
    }

    /// <summary>False when the user backs out of archiving or the change fails.</summary>
    private async Task<bool> SetStatus(Project p, WorkItem it, string status)
    {
        if (status == "Completed" && !it.Archived)
        {
            if (!await Popups.Confirm("Complete and archive?",
                $"#wi{it.Id} {it.Title} will be archived with the OpenSpec CLI and its delta specs merged into openspec/specs. This can't be undone from the board.",
                yes: "Archive")) return false;
            return Run(await Popups.Busy($"Archiving #wi{it.Id} with the OpenSpec CLI…", () => Workspace.In(p.Root, () => WorkItemsApi.SetStatus(it.Id, status))));
        }
        return Run(Workspace.In(p.Root, () => WorkItemsApi.SetStatus(it.Id, status)));
    }

    // The drag script in wwwroot/js/app.js delivers every drop to what sits right after the gap it opened:
    // a card, a column, or the lane / column / row itself when the gap is at the end.

    private void StartColumnDrag(IReadOnlyList<string> siblings, string key)
    {
        dragging = null;
        draggingColumn = (siblings, key);
    }

    /// <summary>A card drag carries the whole selection of its project when the dragged card is part of it (as in a file manager).</summary>
    private void StartCardDrag(Project p, WorkItem it, string from, string? fromRow)
    {
        draggingColumn = null;
        dragging = (p, selected.Contains((p.Root, it.Id)) && selected.Count(s => s.Root == p.Root) > 1
            ? p.Sections.SelectMany(s => BoardModel.Swimlanes(s).SelectMany(r => r.Columns.Select(c => (Row: s.Rows is null ? null : r.Key, Column: c)))   // board order, row by row
                    .SelectMany(rc => rc.Column.Items.Select(i => (Item: i, From: rc.Column.Key, rc.Row))))
                .Where(t => selected.Contains((p.Root, t.Item.Id)))
                .GroupBy(t => t.Item.Id)   // an item in several columns or rows (features, people) leaves the one dragged from
                .Select(g => g.FirstOrDefault(t => t.From == from && t.Row == fromRow, g.First()))
                .ToList()
            : [(it, from, fromRow)]);
    }

    /// <summary>A header dropped in front of a column in its own row (<paramref name="before"/>), or at the row's end (null).</summary>
    private void DropColumn(IReadOnlyList<string> row, string? before)
    {
        if (draggingColumn is not (IReadOnlyList<string> siblings, string key)) return;
        draggingColumn = null;
        if (row.Contains(key)) Set(BoardModel.Insert(O, siblings, [key], before));
    }

    // swim: the swimlane value of the cell a card is dropped in (null without swimlanes); the card takes it, as well as the column's value
    private Task Drop(Project p, BoardColumn col, string? swim) => DropBefore(p, col, null, swim);

    private async Task DropBefore(Project p, BoardColumn col, WorkItem? before, string? swim)
    {
        if (draggingColumn is (IReadOnlyList<string> siblings, _)) { DropColumn(siblings, BoardModel.ColumnKey(Opt, col.Key)); return; }
        if (dragging is not { } moving) return;
        dragging = null;
        if (moving.From.Root != p.Root)   // a change folder lives in one repository
        {
            Popups.Toast("A work item stays in its repository — drag it within its own project", ToastKind.Info);
            return;
        }
        await Land(p, moving.Cards, col, before?.Id, swim);
    }

    /// <summary>Moves cards into a column, then places the ones that made it at the gap (in front of <paramref name="before"/>, or last).</summary>
    private async Task Land(Project p, IEnumerable<(WorkItem Item, string From, string? FromRow)> moving, BoardColumn col, int? before, string? swim = null)
    {
        List<int> landed = [];
        foreach ((WorkItem it, string from, string? fromRow) in moving)
            if (await MoveInto(p, it, from, fromRow, col, swim)) landed.Add(it.Id);
        // ponytail: "my order" keys cards by number, so two projects' same-numbered cards share a place; per-project keys if that shows
        if (landed.Count > 0) Set(BoardModel.PlaceCards(O, col.Items.Select(i => i.Id).ToList(), landed, before));
    }

    /// <summary>The ids of a cell in the order they are shown: the list's grid may sort or filter them (rows it hides go last), the board shows them as they are.</summary>
    private List<int> ShownIds(Project p, BoardCell cell)
    {
        List<int> ids = [.. cell.Items.Items.Select(i => i.Id)];
        if (View != "list" || grid?.View is not { } view) return ids;
        Dictionary<int, int> at = [];
        foreach (GridRow r in view) if (r.Item is not null && r.Root == p.Root) at.TryAdd(r.Item.Id, at.Count);
        return [.. ids.OrderBy(id => at.GetValueOrDefault(id, int.MaxValue))];
    }

    /// <summary>A column sort of the list's grid would hide the order the arrow keys just made, so arranging by hand drops it (the grid takes the settings back with no sort).</summary>
    private void DropGridSort()
    {
        if (gridSettings?.Columns?.Any(c => c.SortOrder is not null) != true) return;
        DataGridSettings fresh = System.Text.Json.JsonSerializer.Deserialize<DataGridSettings>(System.Text.Json.JsonSerializer.Serialize(gridSettings))!;
        foreach (DataGridColumnSettings c in fresh.Columns!) { c.SortOrder = null; c.SortIndex = null; }
        SaveGridSettings(fresh);
    }

    /// <summary>
    /// Arrow keys move the selection, in the cell it is in (a column's items in one row; the column itself without rows): up/down a step, and at the cell's edge
    /// into the neighbouring cell (<see cref="BoardModel.EdgeMove"/>); left/right into the neighbouring column's cell at the same height.
    /// </summary>
    private async Task KeyDown(KeyboardEventArgs e)
    {
        int dir = e.Key switch { "ArrowUp" => -1, "ArrowDown" => 1, "ArrowLeft" => -2, "ArrowRight" => 2, _ => 0 };
        if (selectedProjects.Count > 0 && Math.Abs(dir) == 1) { Workspace.MoveTabs(selectedProjects, dir); return; }   // projects first: their headings are what is selected
        if (selectedRows.Count > 0 && Math.Abs(dir) == 1) { MoveRows(dir); return; }   // then rows
        if (dir == 0 || selected.Count == 0 || O.Unit == "subtasks") return;
        Project? p = projects.FirstOrDefault(x => selected.All(s => s.Root == x.Root));
        List<int> picked = [.. selected.Select(s => s.Id)];
        IReadOnlyList<BoardCell> cells = p?.Sections.SelectMany(BoardModel.Cells).ToList() ?? [];
        if (p is null || BoardModel.CellOf(cells, picked) is not { } at) { Popups.Toast("Select cards in one column to move them with the arrow keys", ToastKind.Info); return; }
        List<int> ids = ShownIds(p, at);
        bool Picked(int id) => selected.Contains((p.Root, id));
        IEnumerable<(WorkItem, string, string?)> Moving() => at.Items.Items.Where(i => Picked(i.Id)).Select(i => (i, at.Items.Key, at.Swim));
        if (Math.Abs(dir) == 1 && BoardModel.EdgeMove(cells, at, picked, dir, View == "list") is { } edge)   // at the cell's edge the cards move on, as a drop there
        {
            await Land(p, Moving(), edge.Target.Items, edge.Before, edge.Target.Swim);
            return;
        }
        if (Math.Abs(dir) == 1) { DropGridSort(); Set(BoardModel.Step(O, ids, ids.Where(Picked).ToList(), dir)); return; }

        if (BoardModel.CellBeside(cells, at, Math.Sign(dir)) is not { } target) return;
        int row = ids.FindIndex(Picked);
        await Land(p, Moving(), target.Items, row < target.Items.Items.Count ? target.Items.Items[row].Id : null, target.Swim);
    }

    /// <summary>
    /// Sets the field the column stands for and, when dropped in a row's cell, the field the row stands for, both through <see cref="SetField"/>;
    /// false when the card can't move to the column.
    /// </summary>
    private async Task<bool> MoveInto(Project p, WorkItem it, string from, string? fromRow, BoardColumn col, string? swim)
    {
        bool ok = from == col.Key || await SetField(p, it, Opt.GroupBy, from, col.Key);
        if (ok && swim is not null && swim != fromRow)   // dropped into another row: the card takes that row's value too
        {
            string toValue = swim == BoardModel.NoRow ? BoardModel.Unassigned : swim;
            if (!HasValue(it, Opt.Swimlane, toValue)) await SetField(p, it, Opt.Swimlane, fromRow is null or BoardModel.NoRow ? BoardModel.Unassigned : fromRow, toValue);
        }
        return ok;
    }

    /// <summary>Whether a work item already has a value of a field (<see cref="BoardModel.Unassigned"/>: none).</summary>
    private static bool HasValue(WorkItem it, string field, string value) => field switch
    {
        "feature" => value == BoardModel.Unassigned ? it.Features.Count == 0 : it.Features.Any(f => f.Path == value),
        "label" => (it.Label ?? "") == value,
        "contributor" => value == BoardModel.Unassigned ? it.Contributors.Count == 0 : it.Contributors.Contains(value),
        "priority" => (it.Priority == "high") == (value == "high"),
        "status" => it.Status == value,
        _ => false,
    };

    /// <summary>
    /// Makes a work item take a value of a field, leaving <paramref name="from"/> (a contributor drop replaces the person it came from).
    /// Status, feature, label, contributor and priority can be set; the due and start periods can't (a period is not a date), so a drop
    /// into another period tells the user and changes nothing. False when the change did not happen.
    /// </summary>
    private async Task<bool> SetField(Project p, WorkItem it, string field, string from, string value)
    {
        switch (field)
        {
            case "feature": return Run(Workspace.In(p.Root, () => WorkItemsApi.MoveFeature(it.Id, value == BoardModel.Unassigned ? null : value)));
            case "label": return Run(Workspace.In(p.Root, () => WorkItemsApi.SetLabel(it.Id, value == BoardModel.Unassigned ? null : value)));
            case "contributor": return Run(Workspace.In(p.Root, () => WorkItemsApi.SetContributors(it.Id, it.Contributors.Where(x => x != from).Append(value).Where(x => x != BoardModel.Unassigned))));
            case "priority": return Run(Workspace.In(p.Root, () => WorkItemsApi.SetPriority(it.Id, value)));
            case "due" or "started":
                Popups.Toast("A due or start period can't be set by dropping; change the date on the work item", ToastKind.Info);
                return false;
            default:
                string? status = await StatusIn(p, value);
                return status is not null && await SetStatus(p, it, status);
        }
    }

    /// <summary>
    /// The status a status column stands for in a project: its own name, or for a merged column the one of the merge's statuses the project has (asked when it has
    /// several). Null, with a message, when the project has none, or when the user backs out. Without a project (the open one) the column's name.
    /// </summary>
    private async Task<string?> StatusIn(Project? p, string columnKey)
    {
        if (p is null) return columnKey;
        IReadOnlyList<string> options = BoardModel.StatusesFor(O, p.Statuses, columnKey);
        if (options.Count == 1) return options[0];
        if (options.Count == 0)
        {
            Popups.Toast($"{p.Name} has no \"{columnKey}\" status", ToastKind.Info);
            return null;
        }
        return await Popups.Choose($"Which status in {p.Name}?", $"{p.Name} has {options.Count} of the merged statuses.", [.. options.Select(s => (s, s))]);
    }

    /// <summary>In a project's column, or (the shared header row) in the project the user picks.</summary>
    private async Task NewItem(Project? p, string? columnKey)
    {
        if (p is null && Multi)
        {
            if (await Workspace.PickProject("New work item in…") is not string root) return;
            p = ProjectOf(root);
        }
        string status = "Backlog";
        if (Opt.GroupBy == "status" && columnKey is not null)
        {
            if (await StatusIn(p, columnKey) is not string chosen) return;
            status = chosen;
        }
        string[]? feature = Opt.GroupBy == "feature" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        string[]? who = Opt.GroupBy == "contributor" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        (bool ok, string message, int id) = Workspace.CreateItem(status, feature, who, p?.Root);
        if (!ok) { Popups.Toast(message, ToastKind.Warning); return; }
        Popups.Toast($"Created #wi{id}", ToastKind.Success);
        if (p is not null) OpenItem(p, id); else Nav.NavigateTo($"item/{id}");
    }

    private int Linked(Project p, string path) => Workspace.In(p.Root, () => ItemsCache.All(includeHidden: true).Count(i => !i.Archived && i.Features.Any(f => f.Path == path)));

    private static string Moving(int n) => n switch
    {
        0 => "Nothing is linked to it.",
        1 => "Its 1 work item moves to Unassigned.",
        _ => $"Its {n} work items move to Unassigned.",
    };

    private async Task DeleteFeature(Project p, Feature f)
    {
        string where = f.Custom ? "It is removed from openspec/config.yaml." : $"Its spec folder openspec/specs/{f.Path} is deleted (git can bring it back).";
        IReadOnlyList<int> owners = Workspace.In(p.Root, () => WorkItemsApi.FeatureSpecOwners(f.Path));
        string specs = owners.Count switch
        {
            0 => "",
            1 => $" #wi{owners[0]} still describes it in its specs; that spec file is deleted too.",
            _ => $" {string.Join(", ", owners.Take(owners.Count - 1).Select(i => $"#wi{i}"))} and #wi{owners[^1]} still describe it in their specs; those spec files are deleted too.",
        };
        if (!await Popups.Confirm($"Delete {f.Name}?", $"{Moving(Linked(p, f.Path))} {where}{specs}", yes: "Delete")) return;
        Run(Workspace.In(p.Root, () => WorkItemsApi.DeleteFeature(f.Path)));
    }

    private async Task RenameFeature(Project p, Feature f)
    {
        if (await Popups.Prompt($"Rename {f.Name}", f.Name, "Rename", f.Name) is not string name || name.Trim() == f.Name) return;
        (bool ok, string message, string? newPath) = Workspace.In(p.Root, () => WorkItemsApi.RenameFeature(f.Path, name));
        if (!Run((ok, message)) || newPath is null) return;
        string oldKey = BoardModel.FeatureKey(f.Path), newKey = BoardModel.FeatureKey(newPath);   // the column keeps its place and its hidden state
        Set(O with { Order = O.Order?.Select(k => k == oldKey ? newKey : k).ToList(), Hidden = O.Hidden?.Select(k => k == oldKey ? newKey : k).ToList() });
    }

    private Task<string?> Ask(string title, string placeholder, string action) => Popups.Prompt(title, placeholder, action);

    private async Task AddFeature(Project p)
    {
        if (await Ask("New feature", "e.g. Exports", "Create") is not string name) return;
        (bool ok, string message, Feature? _) = Workspace.In(p.Root, () => FeaturesApi.CreateFeature(FeaturesApi.Ungrouped, name));
        Run((ok, message));
    }

    /// <summary>The value of the "All projects" choice: a "*" cannot be in a folder path, so it is never a project's root.</summary>
    private const string AllProjects = "*all projects*";

    /// <summary>"+ status": a new status column before Completed in one project's list; the shared header row (<paramref name="p"/> null) first asks which project, or all of them.</summary>
    private async Task AddStatus(Project? p)
    {
        const string all = AllProjects;
        List<Project> targets = p is null ? [] : [p];
        if (p is null)
        {
            List<(string, string)> choices = [.. projects.Select(x => (x.Root, x.Name)), (all, "All projects")];
            if (await Popups.Choose("Add a status to…", "The columns of the projects are shown together; pick the project whose list gets the new status.", choices) is not string pick) return;
            targets = pick == all ? [.. projects] : [.. projects.Where(x => x.Root == pick)];
        }
        if (await Ask("New status", "e.g. Review ready", "Add") is not string name) return;
        List<string> refused = [];
        int added = 0;
        foreach (Project t in targets)
        {
            (bool ok, string message) = Workspace.In(t.Root, () => WorkItemsApi.AddStatus(name, beforeCompleted: true));
            if (ok) { Workspace.Do((true, message)); added++; }
            else refused.Add(targets.Count > 1 ? $"{t.Name}: {message}" : message);
        }
        if (added > 0) Popups.Saved();
        if (refused.Count > 0) Popups.Toast(string.Join(" ", refused), ToastKind.Warning);
    }

    private async Task Merge()
    {
        if (selected.Select(s => s.Root).Distinct().Count() != 1) { Popups.Toast("Select work items of one project to merge them", ToastKind.Info); return; }
        string root = selected.First().Root;
        List<int> ids = selected.Select(s => s.Id).Order().ToList();
        List<(string, string)> choices = Workspace.In(root, () => ids.Select(i => (i.ToString(), $"#wi{i} {ItemsCache.Get(i)?.Title}")).ToList());
        string? into = await Popups.Choose("Merge into…", "The item you pick keeps its title and description; the others' descriptions become notes on it.", choices);
        if (into is null) return;
        (bool ok, string message, int id) = Workspace.In(root, () => WorkItemsApi.Merge(ids, int.Parse(into)));
        if (!ok) { Popups.Toast(message, ToastKind.Warning); return; }
        selected.Clear();
        Workspace.Notify();
        Popups.Toast($"Merged into #wi{id}", ToastKind.Success);
    }

    private void HideSelected()
    {
        List<string> failed = [];
        foreach ((string root, int id) in selected.OrderBy(s => s.Root).ThenBy(s => s.Id).ToList())
        {
            (bool ok, string message) = Workspace.In(root, () => WorkItemsApi.Hide(id));
            if (!ok) failed.Add($"#wi{id}: {message}");
        }
        selected.Clear();
        Workspace.Notify();
        if (failed.Count > 0) Popups.Toast(string.Join("; ", failed), ToastKind.Warning);
    }

    public override void Dispose()
    {
        base.Dispose();
        Settings.Changed -= OnSettings;
        Popups.ExamineChanged -= OnExamineChanged;
        Nav.LocationChanged -= OnLocation;
    }
}
