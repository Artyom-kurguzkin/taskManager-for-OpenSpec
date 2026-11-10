using Features;
using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Radzen;
using Radzen.Blazor;
using TaskManager.Board;
using TaskManager.Components.Shared;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Components.Pages;

public partial class BoardPage
{
    [Inject] private SettingsStore Settings { get; set; } = default!;

    /// <summary>One open repository on the board: its items and features, and the columns they fall into under the current options.</summary>
    private sealed record Project(string Root, string Name, IReadOnlyList<WorkItem> Items, IReadOnlyList<Feature> Features,
        IReadOnlyDictionary<string, string> Names, IReadOnlyList<BoardSection> Sections, IReadOnlyDictionary<string, string> Colours)
    {
        public IEnumerable<(string? Category, BoardColumn Column)> Flat => Sections.SelectMany(s => s.Columns.Select(c => (s.Category, c)));
    }

    private BoardOptions O = new();

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
        Toggle(selectedProjects, root);
    }

    /// <summary>A plain click on the board or list (not on a project heading) lets go of the selected projects and rows.</summary>
    private void BackgroundClick(MouseEventArgs e)
    {
        if (e.ShiftKey || e.CtrlKey || e.MetaKey) return;
        selectedProjects.Clear();
        selectedRows.Clear();
    }

    private readonly HashSet<string> selectedRows = [];   // swimlane headers Shift-clicked (BoardModel.RowKey), to move with the arrow keys
    private (IReadOnlyList<string> Siblings, IReadOnlyList<string> Moving)? draggingRow;   // a swimlane header being dragged, or every selected one

    private List<string> RowKeys(BoardSection sec) => [.. BoardModel.Swimlanes(sec).Select(r => BoardModel.RowKey(O.Swimlane, r.Key))];

    /// <summary>Shift / Ctrl / ⌘-click on a swimlane header selects it, as on a project heading; the arrow keys then move it.</summary>
    private void RowClick(string key, MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) return;   // a plain click is the board's (BackgroundClick): it lets go
        selected.Clear();
        selectedProjects.Clear();
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

    internal static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List"), ("calendar", "Calendar")];
    internal static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    // One field list for the three Organise options: columns take any of them, rows and colour also "none".
    internal static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("feature", "feature"), ("label", "label"), ("priority", "priority"), ("contributor", "contributor"), ("due", "due date"), ("started", "start date")];
    internal static readonly IReadOnlyList<(string, string)> Swimlanes = [("none", "none"), .. GroupBys];
    internal static readonly IReadOnlyList<(string, string)> ColorBys = [.. GroupBys, ("none", "none")];
    internal static readonly IReadOnlyList<(string, string)> Periods =[("all", "any time"), ("overdue", "overdue"), ("today", "today"), ("week", "this week"), ("month", "this month"), ("none", "no due date")];
    internal static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("label", "label"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id"), ("manual", "my order")];

    private IReadOnlyList<(string, string)> People =>
        [("all", "everyone"), .. projects.SelectMany(p => p.Items).SelectMany(i => i.Contributors).Distinct().Order().Select(p => (p, p))];

    private bool Multi => projects.Count > 1;

    /// <summary>Several projects with the same columns (grouped by status or label): one header row above serves them all.</summary>
    private bool Aligned => Multi && O.Swimlane == BoardModel.NoSwimlane && projects.All(p => p.Sections.Count == 1)
        && projects.Select(p => string.Join("|", p.Sections[0].Columns.Select(c => c.Key))).Distinct().Count() == 1;

    private IReadOnlyList<string> HeadKeys => projects[0].Sections[0].Columns.Select(c => BoardModel.ColumnKey(O, c.Key)).ToList();

    private bool Collapsed(Project p) => Settings.ProjectCollapsed(p.Root);
    private bool IsActive(Project p) => Workspace.Root is string r && SettingsStore.Key(r) == SettingsStore.Key(p.Root);
    /// <summary>The project a shared control (the header row's "+ Add work item") acts on: the active one, else the first shown.</summary>
    private Project? Active => projects.FirstOrDefault(IsActive) ?? projects.FirstOrDefault();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Settings.Changed += OnSettings;
        if (Workspace.Root is string root) O = Settings.Board(root);
        projectState = ProjectState;
    }

    // The side menu edits the view options, and the hidden / collapsed projects, through the settings store. Other settings
    // (the grid's own layout, the theme) change nothing here, so they cause no reload.
    private string projectState = "";
    private string ProjectState => string.Join(";", Settings.Current.OpenRepos ?? []) + "|" + string.Join(";", Settings.Current.HiddenProjects ?? []) + "|" + string.Join(";", Settings.Current.CollapsedProjects ?? []);   // tab order, hidden, collapsed

    private void OnSettings() => InvokeAsync(() =>
    {
        BoardOptions o = Workspace.Root is string root ? Settings.Board(root) : O;
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
        projects = Workspace.Projects.Where(r => !Settings.ProjectHidden(r)).Select(r => Workspace.In(r, () =>
        {
            IReadOnlyList<WorkItem> items = WorkItemsApi.GetAll(includeHidden: O.ShowHidden);
            IReadOnlyList<Feature> features = FeaturesApi.GetAll();
            return new Project(r, Path.GetFileName(r), items, features, Workspace.FeatureNames, BoardModel.Build(items, features, WorkItemsApi.GetLanes(), O), BoardModel.ColourMap(O.ColorBy, items, features));
        })).ToList();
        selected.RemoveWhere(s => !projects.Any(p => p.Root == s.Root && p.Items.Any(i => i.Id == s.Id)));
        selectedProjects.RemoveWhere(r => !Multi || !projects.Any(p => p.Root == r));
        selectedRows.RemoveWhere(k => !projects.Any(p => p.Sections.Any(s => RowKeys(s).Contains(k))));   // a hidden row, or another row field
        rows = [.. projects.SelectMany((p, n) => BoardModel.Rows(p.Sections, p.Names, p.Root, p.Name, n * 1000))];
        groups = rows.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => (g.First().Column, g.First().Category, g.First().Root));
        if (gridSettings is null && O.ListSettings is string json)
            try { gridSettings = System.Text.Json.JsonSerializer.Deserialize<DataGridSettings>(json); } catch (System.Text.Json.JsonException) { }
        if (grid is null) FillGroups();   // before the grid's first render: grouped from the start, not flat first and regrouped after it (a second pass over every row)
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<GroupDescriptor> groupDescriptors = [];

    /// <summary>Groups by project (when several), column, then row value (when set); the group panel stays hidden. The grid is given this collection (Groups).</summary>
    private void FillGroups()
    {
        groupedMulti = Multi;
        groupedSwim = O.Swimlane;
        groupDescriptors.Clear();
        if (Multi) groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Project), Title = "Project" });
        groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Group), Title = "Column" });
        if (O.Swimlane != BoardModel.NoSwimlane) groupDescriptors.Add(new GroupDescriptor { Property = nameof(GridRow.Swim), Title = "Row" });
    }

    private Project ProjectOf(string root) => projects.First(p => p.Root == root);

    private void Set(BoardOptions options)
    {
        O = options;
        if (Workspace.Root is string root) Settings.SaveBoard(root, O);
        Load();
    }

    // ── Calendar view: the Roadmap component draws the board's columns as groups, or the rows with the columns inside them ──
    private IReadOnlyList<RoadmapSource> RoadmapProjects => projects.Select(p => new RoadmapSource(p.Root, p.Name, p.Sections, p.Colours)).ToList();
    private void OpenFrom((string Root, int Id) at) => OpenItem(ProjectOf(at.Root), at.Id);

    // ── List view: a Radzen DataGrid over the projects' columns ──
    private RadzenDataGrid<GridRow>? grid;
    private IReadOnlyList<GridRow> rows = [];
    private Dictionary<int, (BoardColumn Column, string? Category, string Root)> groups = [];
    private string? groupedSwim;                      // the swimlane field the grid's group descriptors include
    private DataGridSettings? gridSettings;   // column widths, order, visibility, sort and filters, remembered per repository
    private readonly HashSet<string> shutGroups = [];   // collapsed groups, by project and column key (and row value, for a row-value group), kept across re-renders and data reloads
    private bool? groupedMulti;               // whether the grid's group descriptors include the project level
    private bool? subtasksApplied;            // the ShowSubtasks setting last pushed into the grid's expanded rows

    /// <summary>A column is shown unless hidden by the user, or it repeats what the rows are grouped by.</summary>
    private static string Day(DateTime? d) => d is { } v ? v.ToString("d MMM") : "";

    private bool Shown(string column) => !BoardModel.IsHidden(O, BoardModel.ListColumnKey(column)) && column != BoardModel.GroupedColumn(O) && column != BoardModel.SwimColumn(O);

    // Asks what the grid is grouped by, not Multi: after the project list changes, the grid renders once with its old group
    // descriptors (OnAfterRenderAsync swaps them), and a group keyed by column read as a project group throws a cast in the render.
    private bool IsProjectGroup(Group g) => groupedMulti == true && g.Level == 0;

    /// <summary>With rows set the grid groups project (when several) → column → row value; this is the row-value level, inside a column.</summary>
    private bool IsSwimGroup(Group g) => groupedSwim is not (null or BoardModel.NoSwimlane) && g.Level == (groupedMulti == true ? 2 : 1);

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

    /// <summary>Marks the row for the drag script, cell copy and styling, and gives it the board's drop handlers.</summary>
    private void RowRender(RowRenderEventArgs<GridRow> args)
    {
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
        args.Attributes["style"] = $"--band:var({BoardModel.ColourOf(it, O.ColorBy, null, p.Colours)})";   // the row's colour stripe (app.css), as a card's band
        if (Embedded && Current == it.Id && IsActive(p)) args.Attributes["class"] = "cur";
        if (selected.Contains((p.Root, it.Id))) args.Attributes["data-selected"] = "";
        if (it.Hidden) args.Attributes["data-hidden"] = "";
        args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => { if (e.ShiftKey || e.CtrlKey || e.MetaKey) ToggleSelect(p, it); });   // the board's select click, anywhere on the row
        args.Attributes["ondragstart"] = EventCallback.Factory.Create<DragEventArgs>(this, () => StartCardDrag(p, it, row.Column.Key, SwimOf(row)));
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => DropBefore(p, row.Column, it, SwimOf(row)));
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
                if (selectedProjects.Contains(gp.Root)) args.Attributes["data-selected"] = "";
                args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => ProjectClick(gp.Root, e));   // Shift-click anywhere in the row selects the project
                if (Collapsed(gp)) args.Expanded = false;
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
        if (O.GroupBy == "status") args.Attributes["data-path-fill"] = $"var({Ui.Band(col.Label)})";   // the status pill's colour, as the group header wears it
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(ProjectOf(root), col, null));   // the column's end; the item keeps its row value
        // only ever false: Radzen takes Expanded = true as "keep open" and ignores the chevron; the collapse/expand events keep shutGroups current
        if (shutGroups.Contains(ShutKey(args.Group!))) args.Expanded = false;
    }

    /// <summary>The list's project chevron keeps the same setting as the board's.</summary>
    private void SetProjectCollapsed(Group g, bool collapsed)
    {
        if (projects.FirstOrDefault(p => p.Name == (string)g.Data.Key) is { } p && Collapsed(p) != collapsed) Settings.ToggleProjectCollapsed(p.Root);
    }

    private async Task ToggleRow(GridRow row)
    {
        if (grid is null) return;
        if (grid.IsRowExpanded(row)) await grid.CollapseRows([row]); else await grid.ExpandRow(row);
    }

    private void SaveGridSettings(DataGridSettings settings)
    {
        gridSettings = settings;
        O = O with { ListSettings = System.Text.Json.JsonSerializer.Serialize(settings) };
        if (Workspace.Root is string root) Settings.SaveBoard(root, O);   // no Load: nothing on the board changed
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (grid is null) return;
        if (grid.Groups.Count == 0 || groupedMulti != Multi || groupedSwim != O.Swimlane)   // grouped by project (when several), column, then row value (when set); the group panel stays hidden
        {
            FillGroups();
            await grid.Reload();
            return;
        }
        if (subtasksApplied != O.ShowSubtasks)   // on: every row shows its subtasks; off: only rows opened by hand
        {
            subtasksApplied = O.ShowSubtasks;
            List<GridRow> withSubtasks = rows.Where(r => r.Item is { Subtasks.Count: > 0 }).ToList();
            if (!O.ShowSubtasks) await grid.CollapseRows(withSubtasks.Where(grid.IsRowExpanded));
            else await grid.ExpandRows(withSubtasks.Where(r => !grid.IsRowExpanded(r)));
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
        else OpenItem(p, it.Id);
    }

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
        if (draggingColumn is (IReadOnlyList<string> siblings, _)) { DropColumn(siblings, BoardModel.ColumnKey(O, col.Key)); return; }
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

    /// <summary>Arrow keys move the selection: up/down a step in its column, left/right into the neighbouring column at the same height.</summary>
    private async Task KeyDown(KeyboardEventArgs e)
    {
        int dir = e.Key switch { "ArrowUp" => -1, "ArrowDown" => 1, "ArrowLeft" => -2, "ArrowRight" => 2, _ => 0 };
        if (selectedProjects.Count > 0 && Math.Abs(dir) == 1) { Workspace.MoveTabs(selectedProjects, dir); return; }   // projects first: their headings are what is selected
        if (selectedRows.Count > 0 && Math.Abs(dir) == 1) { MoveRows(dir); return; }   // then rows
        if (dir == 0 || selected.Count == 0 || O.Unit == "subtasks") return;
        Project? p = projects.FirstOrDefault(x => selected.All(s => s.Root == x.Root));
        List<BoardColumn> columns = p?.Flat.Select(f => f.Column).ToList() ?? [];
        BoardColumn? col = columns.FirstOrDefault(c => selected.All(s => c.Items.Any(i => i.Id == s.Id)));
        if (p is null || col is null) { Popups.Toast("Select cards in one column to move them with the arrow keys", ToastKind.Info); return; }
        List<int> ids = col.Items.Select(i => i.Id).ToList();
        bool Picked(int id) => selected.Contains((p.Root, id));
        if (Math.Abs(dir) == 1) { Set(BoardModel.Step(O, ids, ids.Where(Picked).ToList(), dir)); return; }

        int to = columns.IndexOf(col) + Math.Sign(dir);
        if (to < 0 || to >= columns.Count) return;
        BoardColumn target = columns[to];
        int row = ids.FindIndex(Picked);
        await Land(p, col.Items.Where(i => Picked(i.Id)).Select(i => (i, col.Key, (string?)null)), target, row < target.Items.Count ? target.Items[row].Id : null);
    }

    /// <summary>
    /// Sets the field the column stands for and, when dropped in a row's cell, the field the row stands for, both through <see cref="SetField"/>;
    /// false when the card can't move to the column.
    /// </summary>
    private async Task<bool> MoveInto(Project p, WorkItem it, string from, string? fromRow, BoardColumn col, string? swim)
    {
        bool ok = from == col.Key || await SetField(p, it, O.GroupBy, from, col.Key);
        if (ok && swim is not null && swim != fromRow)   // dropped into another row: the card takes that row's value too
        {
            string toValue = swim == BoardModel.NoRow ? BoardModel.Unassigned : swim;
            if (!HasValue(it, O.Swimlane, toValue)) await SetField(p, it, O.Swimlane, fromRow is null or BoardModel.NoRow ? BoardModel.Unassigned : fromRow, toValue);
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
            default: return await SetStatus(p, it, value);
        }
    }

    /// <summary>In a project's column, or (the shared header row) in the project the user picks.</summary>
    private async Task NewItem(Project? p, string? columnKey)
    {
        if (p is null && Multi)
        {
            if (await Workspace.PickProject("New work item in…") is not string root) return;
            p = ProjectOf(root);
        }
        string status = O.GroupBy == "status" && columnKey is not null ? columnKey : "Backlog";
        string[]? feature = O.GroupBy == "feature" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        string[]? who = O.GroupBy == "contributor" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        (bool ok, string message, int id) = Workspace.CreateItem(status, feature, who, p?.Root);
        if (!ok) { Popups.Toast(message, ToastKind.Warning); return; }
        Popups.Toast($"Created #wi{id}", ToastKind.Success);
        if (p is not null) OpenItem(p, id); else Nav.NavigateTo($"item/{id}");
    }

    private int Linked(Project p, string path) => Workspace.In(p.Root, () => WorkItemsApi.GetAll(includeHidden: true).Count(i => !i.Archived && i.Features.Any(f => f.Path == path)));

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

    private async Task Merge()
    {
        if (selected.Select(s => s.Root).Distinct().Count() != 1) { Popups.Toast("Select work items of one project to merge them", ToastKind.Info); return; }
        string root = selected.First().Root;
        List<int> ids = selected.Select(s => s.Id).Order().ToList();
        List<(string, string)> choices = Workspace.In(root, () => ids.Select(i => (i.ToString(), $"#wi{i} {WorkItemsApi.Get(i)?.Title}")).ToList());
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
    }
}
