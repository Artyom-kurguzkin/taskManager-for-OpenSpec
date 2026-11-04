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
        IReadOnlyDictionary<string, string> Names, IReadOnlyList<BoardSection> Sections)
    {
        public IEnumerable<(string? Category, BoardColumn Column)> Flat => Sections.SelectMany(s => s.Columns.Select(c => (s.Category, c)));
    }

    private BoardOptions O = new();
    private List<Project> projects = [];   // the visible projects (open tabs not hidden), in tab order
    private readonly HashSet<(string Root, int Id)> selected = [];   // a number is only unique within its project
    private readonly HashSet<string> selectedProjects = [];          // project headings Shift-clicked, to move with the arrow keys

    /// <summary>Shift / Ctrl / ⌘-click on a project's heading selects it, as a card is selected; the arrow keys then move it.</summary>
    private void ProjectClick(string root, MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) return;
        selected.Clear();   // projects or cards, never both: the arrow keys move one kind
        Toggle(selectedProjects, root);
    }

    /// <summary>A plain click on the board or list (not on a project heading) lets go of the selected projects.</summary>
    private void BackgroundClick(MouseEventArgs e)
    {
        if (!(e.ShiftKey || e.CtrlKey || e.MetaKey)) selectedProjects.Clear();
    }

    private (Project From, IReadOnlyList<(WorkItem Item, string Column)> Cards)? dragging;   // the dragged card, or every selected card of its project
    private (IReadOnlyList<string> Siblings, string Key)? draggingColumn;   // a column header or group name being dragged

    internal static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List"), ("calendar", "Calendar")];
    internal static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    internal static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("feature", "feature"), ("label", "label"), ("contributor", "contributor"), ("due", "due date"), ("started", "start date")];
    internal static readonly IReadOnlyList<(string, string)> Periods = [("all", "any time"), ("overdue", "overdue"), ("today", "today"), ("week", "this week"), ("month", "this month"), ("none", "no due date")];
    internal static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("label", "label"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id"), ("manual", "my order")];

    private IReadOnlyList<(string, string)> People =>
        [("all", "everyone"), .. projects.SelectMany(p => p.Items).SelectMany(i => i.Contributors).Distinct().Order().Select(p => (p, p))];

    private bool Multi => projects.Count > 1;

    /// <summary>Several projects with the same columns (grouped by status or label): one header row above serves them all.</summary>
    private bool Aligned => Multi && projects.All(p => p.Sections.Count == 1)
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

    protected override void Load()
    {
        projects = Workspace.Projects.Where(r => !Settings.ProjectHidden(r)).Select(r => Workspace.In(r, () =>
        {
            IReadOnlyList<WorkItem> items = WorkItemsApi.GetAll(includeHidden: O.ShowHidden);
            IReadOnlyList<Feature> features = FeaturesApi.GetAll();
            return new Project(r, Path.GetFileName(r), items, features, Workspace.FeatureNames, BoardModel.Build(items, features, WorkItemsApi.GetLanes(), O));
        })).ToList();
        selected.RemoveWhere(s => !projects.Any(p => p.Root == s.Root && p.Items.Any(i => i.Id == s.Id)));
        selectedProjects.RemoveWhere(r => !Multi || !projects.Any(p => p.Root == r));
        rows = [.. projects.SelectMany((p, n) => BoardModel.Rows(p.Sections, p.Names, p.Root, p.Name, n * 1000))];
        groups = rows.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => (g.First().Column, g.First().Category, g.First().Root));
        if (gridSettings is null && O.ListSettings is string json)
            try { gridSettings = System.Text.Json.JsonSerializer.Deserialize<DataGridSettings>(json); } catch (System.Text.Json.JsonException) { }
    }

    private Project ProjectOf(string root) => projects.First(p => p.Root == root);

    private void Set(BoardOptions options)
    {
        O = options;
        if (Workspace.Root is string root) Settings.SaveBoard(root, O);
        Load();
    }

    // ── Calendar view ──
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);
    private DateOnly calMonth = ThisMonth;

    /// <summary>Every work item on the board (the same filters), each once, with its project; the calendar draws these.</summary>
    private Dictionary<WorkItem, Project> CalItems =>
        projects.SelectMany(p => p.Flat.SelectMany(f => f.Column.Items).DistinctBy(i => i.Id).Select(i => (Item: i, Project: p))).ToDictionary(x => x.Item, x => x.Project);

    private List<(Project, WorkItem)> Unscheduled => CalendarModel.Unscheduled(CalItems.Keys).Select(i => (CalItems[i], i)).ToList();

    private static string BarTitle(WorkItem it) =>
        $"#wi{it.Id} {it.Title} · {it.Status}" + (it.Started is DateOnly s ? $" · started {s:d MMM}" : "") + (it.Due is DateOnly d ? $" · due {d:d MMM}" : "");

    // ── List view: a Radzen DataGrid over the projects' columns ──
    private RadzenDataGrid<GridRow>? grid;
    private IReadOnlyList<GridRow> rows = [];
    private Dictionary<int, (BoardColumn Column, string? Category, string Root)> groups = [];
    private DataGridSettings? gridSettings;   // column widths, order, visibility, sort and filters, remembered per repository
    private readonly HashSet<string> shutGroups = [];   // collapsed groups, by project and column key (kept across re-renders and data reloads)
    private bool? groupedMulti;               // whether the grid's group descriptors include the project level
    private bool? subtasksApplied;            // the ShowSubtasks setting last pushed into the grid's expanded rows

    /// <summary>A column is shown unless hidden by the user, or it repeats what the rows are grouped by.</summary>
    private bool Shown(string column) => !BoardModel.IsHidden(O, BoardModel.ListColumnKey(column)) && column != BoardModel.GroupedColumn(O);

    // Asks what the grid is grouped by, not Multi: after the project list changes, the grid renders once with its old group
    // descriptors (OnAfterRenderAsync swaps them), and a group keyed by column read as a project group throws a cast in the render.
    private bool IsProjectGroup(Group g) => groupedMulti == true && g.Level == 0;

    private string ShutKey(Group g) => GroupOf(g) is var (col, _, root) ? root + "|" + col.Key : "";

    private (BoardColumn Column, string? Category, string Root) GroupOf(Group g) => groups[Convert.ToInt32(g.Data.Key)];

    /// <summary>Marks the row for the drag script, cell copy and styling, and gives it the board's drop handlers.</summary>
    private void RowRender(RowRenderEventArgs<GridRow> args)
    {
        GridRow row = args.Data!;   // Radzen declares it nullable; a row always has one
        args.Expandable = false;   // the row's own chevron expands it
        Project p = ProjectOf(row.Root);
        if (row.Item is not WorkItem it)
        {
            args.Attributes["data-empty"] = "";   // an empty column's placeholder: dropping on it lands in that column
            args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(p, row.Column, null));
            return;
        }
        args.Attributes["data-id"] = it.Id;
        args.Attributes["data-row"] = row.Key;
        if (selected.Contains((p.Root, it.Id))) args.Attributes["data-selected"] = "";
        if (it.Hidden) args.Attributes["data-hidden"] = "";
        args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => { if (e.ShiftKey || e.CtrlKey || e.MetaKey) ToggleSelect(p, it); });   // the board's select click, anywhere on the row
        args.Attributes["ondragstart"] = EventCallback.Factory.Create<DragEventArgs>(this, () => StartCardDrag(p, it, row.Column.Key));
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => DropBefore(p, row.Column, null, it));
    }

    /// <summary>A group row stands for its column: a drop on it lands at the column's end. A project row (several projects) only heads.</summary>
    private void GroupRowRender(GroupRowRenderEventArgs args)
    {
        if (IsProjectGroup(args.Group!))
        {
            args.Attributes["data-group"] = "proj:" + args.Group!.Data.Key;
            if (projects.FirstOrDefault(p => p.Name == (string)args.Group!.Data.Key) is { } gp)
            {
                if (selectedProjects.Contains(gp.Root)) args.Attributes["data-selected"] = "";
                args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => ProjectClick(gp.Root, e));   // Shift-click anywhere in the row selects the project
                if (Collapsed(gp)) args.Expanded = false;
            }
            return;
        }
        (BoardColumn col, _, string root) = GroupOf(args.Group!);
        args.Attributes["data-group"] = col.Key;
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(ProjectOf(root), col, null));
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
        if (grid.Groups.Count == 0 || groupedMulti != Multi)   // grouped by project (when several), then by column; the group panel stays hidden
        {
            groupedMulti = Multi;
            grid.Groups.Clear();
            if (Multi) grid.Groups.Add(new GroupDescriptor { Property = nameof(GridRow.Project), Title = "Project" });
            grid.Groups.Add(new GroupDescriptor { Property = nameof(GridRow.Group), Title = "Column" });
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
    private void StartCardDrag(Project p, WorkItem it, string from)
    {
        draggingColumn = null;
        dragging = (p, selected.Contains((p.Root, it.Id)) && selected.Count(s => s.Root == p.Root) > 1
            ? p.Flat.SelectMany(f => f.Column.Items.Select(i => (Item: i, From: f.Column.Key)))   // board order
                .Where(t => selected.Contains((p.Root, t.Item.Id)))
                .GroupBy(t => t.Item.Id)   // an item in several columns (features, people) leaves the one dragged from
                .Select(g => g.FirstOrDefault(t => t.From == from, g.First()))
                .ToList()
            : [(it, from)]);
    }

    /// <summary>A header dropped in front of a column in its own row (<paramref name="before"/>), or at the row's end (null).</summary>
    private void DropColumn(IReadOnlyList<string> row, string? before)
    {
        if (draggingColumn is not (IReadOnlyList<string> siblings, string key)) return;
        draggingColumn = null;
        if (row.Contains(key)) Set(BoardModel.Insert(O, siblings, [key], before));
    }

    private Task Drop(Project p, BoardColumn col, string? lane, string? priority = null) => DropBefore(p, col, lane, null, priority);

    private async Task DropBefore(Project p, BoardColumn col, string? lane, WorkItem? before, string? priority = null)
    {
        if (draggingColumn is (IReadOnlyList<string> siblings, _)) { DropColumn(siblings, BoardModel.ColumnKey(O, col.Key)); return; }
        if (dragging is not { } moving) return;
        dragging = null;
        if (moving.From.Root != p.Root)   // a change folder lives in one repository
        {
            Popups.Toast("A work item stays in its repository — drag it within its own project", ToastKind.Info);
            return;
        }
        await Land(p, moving.Cards, col, lane, before?.Id, priority);
    }

    /// <summary>Moves cards into a column, then places the ones that made it at the gap (in front of <paramref name="before"/>, or last).</summary>
    private async Task Land(Project p, IEnumerable<(WorkItem Item, string From)> moving, BoardColumn col, string? lane, int? before, string? priority = null)
    {
        List<int> landed = [];
        foreach ((WorkItem it, string from) in moving)
            if (await MoveInto(p, it, from, col, lane, priority)) landed.Add(it.Id);
        // ponytail: "my order" keys cards by number, so two projects' same-numbered cards share a place; per-project keys if that shows
        if (landed.Count > 0) Set(BoardModel.PlaceCards(O, col.Items.Select(i => i.Id).ToList(), landed, before));
    }

    /// <summary>Arrow keys move the selection: up/down a step in its column, left/right into the neighbouring column at the same height.</summary>
    private async Task KeyDown(KeyboardEventArgs e)
    {
        int dir = e.Key switch { "ArrowUp" => -1, "ArrowDown" => 1, "ArrowLeft" => -2, "ArrowRight" => 2, _ => 0 };
        if (selectedProjects.Count > 0 && Math.Abs(dir) == 1) { Workspace.MoveTabs(selectedProjects, dir); return; }   // projects first: their headings are what is selected
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
        await Land(p, col.Items.Where(i => Picked(i.Id)).Select(i => (i, col.Key)), target, null, row < target.Items.Count ? target.Items[row].Id : null);
    }

    /// <summary>Sets the dimension the column stands for (and its lane); false when the card can't move there.</summary>
    private async Task<bool> MoveInto(Project p, WorkItem it, string from, BoardColumn col, string? lane, string? priority = null)
    {
        bool ok = from == col.Key || O.GroupBy switch
        {
            "feature" => Run(Workspace.In(p.Root, () => WorkItemsApi.MoveFeature(it.Id, col.Key == BoardModel.Unassigned ? null : col.Key))),
            "label" => Run(Workspace.In(p.Root, () => WorkItemsApi.SetLabel(it.Id, col.Key == BoardModel.Unassigned ? null : col.Key))),
            "contributor" => Run(Workspace.In(p.Root, () => WorkItemsApi.SetContributors(it.Id, it.Contributors.Where(x => x != from).Append(col.Key).Where(x => x != BoardModel.Unassigned)))),
            _ => await SetStatus(p, it, col.Key),
        };
        if (ok && lane is not null) Run(Workspace.In(p.Root, () => WorkItemsApi.SetLane(it.Id, col.Key, lane)));
        if (ok && priority is not null && (it.Priority == "high" ? "high" : "low") != priority)   // dropped on a priority subgroup
            Run(Workspace.In(p.Root, () => WorkItemsApi.SetPriority(it.Id, priority)));
        return ok;
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

    private async Task AddLane(Project p, string column, string label)
    {
        if (await Ask($"Add a subgroup to {label}", "e.g. Needs review", "Add") is string lane) Run(Workspace.In(p.Root, () => WorkItemsApi.AddLane(column, lane)));
    }

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
