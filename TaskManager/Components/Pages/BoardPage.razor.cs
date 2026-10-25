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
    [Inject] private Workspace Workspace { get; set; } = default!;
    [Inject] private SettingsStore Settings { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private UiService Popups { get; set; } = default!;

    private BoardOptions O = new();
    private IReadOnlyList<WorkItem> items = [];
    private IReadOnlyList<Feature> features = [];
    private IReadOnlyList<BoardSection> Sections = [];
    private readonly HashSet<int> selected = [];



    private IReadOnlyList<(WorkItem Item, string From)>? dragging;   // the dragged card, or every selected card
    private (IReadOnlyList<string> Siblings, string Key)? draggingColumn;   // a column header or group name being dragged

    internal static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List")];
    internal static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    internal static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("bucket", "bucket"), ("label", "label"), ("contributor", "contributor")];
    internal static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("label", "label"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id"), ("manual", "my order")];
    private static readonly IReadOnlyList<(string, string)> Priorities = [("high", "high"), ("low", "lower")];
    private static readonly IReadOnlyList<(string, string)> Statuses = WorkItemsApi.Statuses.Select(s => (s, s)).ToList();
    private static readonly IReadOnlyList<(string Key, string Label)> Columns =
        [("id", "ID"), ("title", "Title"), ("status", "Status"), ("label", "Label"), ("feature", "Features"), ("who", "Contributors"), ("priority", "Priority"), ("progress", "Progress"), ("modified", "Updated")];

    private IReadOnlyList<(string, string)> People =>
        [("all", "everyone"), .. items.SelectMany(i => i.Contributors).Distinct().Order().Select(p => (p, p))];

    private static readonly IReadOnlyList<(string, string)> LabelOptions = [("", "none"), .. WorkItemsApi.Labels.Select(l => (l, l))];

    private IEnumerable<(string? Category, BoardColumn Column)> Flat => Sections.SelectMany(s => s.Columns.Select(c => (s.Category, c)));

    protected override void OnInitialized()
    {
        Workspace.Changed += OnChanged;
        Settings.Changed += OnSettings;
        if (Workspace.Root is string root) O = Settings.Board(root);
        Load();
    }

    private void OnChanged() => InvokeAsync(() => { Load(); StateHasChanged(); });

    // The side menu edits the view options through the settings store.
    private void OnSettings() => InvokeAsync(() =>
    {
        if (Workspace.Root is string root && Settings.Board(root) is var o && o != O) { O = o; Load(); StateHasChanged(); }
    });

    private void Load()
    {
        items = WorkItemsApi.GetAll(includeHidden: O.ShowHidden);
        features = FeaturesApi.GetAll();
        Sections = BoardModel.Build(items, features, WorkItemsApi.GetLanes(), O);
        selected.IntersectWith(items.Select(i => i.Id));
        rows = BoardModel.Rows(Sections, Workspace.FeatureNames);
        groups = rows.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => (g.First().Column, g.First().Category));
        if (gridSettings is null && O.ListSettings is string json)
            try { gridSettings = System.Text.Json.JsonSerializer.Deserialize<DataGridSettings>(json); } catch (System.Text.Json.JsonException) { }
    }

    private void Set(BoardOptions options)
    {
        O = options;
        if (Workspace.Root is string root) Settings.SaveBoard(root, O);
        Load();
    }

    private void SortBy(string key) => Set(O with { SortBy = key, SortDir = O.SortBy == key ? -O.SortDir : 1 });

    // ── List view: a Radzen DataGrid over the board's columns ──
    private RadzenDataGrid<GridRow>? grid;
    private IReadOnlyList<GridRow> rows = [];
    private Dictionary<int, (BoardColumn Column, string? Category)> groups = [];
    private DataGridSettings? gridSettings;   // column widths, order, visibility, sort and filters, remembered per repository
    private readonly HashSet<string> shutGroups = [];   // collapsed groups, by column key (kept across re-renders and data reloads)

    private bool Shown(string column) => !BoardModel.IsHidden(O, BoardModel.ListColumnKey(column));

    private bool? compactApplied;             // the compact setting last pushed into the grid's expanded rows

    private (BoardColumn Column, string? Category) GroupOf(Group g) => groups[Convert.ToInt32(g.Data.Key)];

    /// <summary>Marks the row for the drag script, cell copy and styling, and gives it the board's drop handlers.</summary>
    private void RowRender(RowRenderEventArgs<GridRow> args)
    {
        GridRow row = args.Data!;   // Radzen declares it nullable; a row always has one
        args.Expandable = false;   // the row's own chevron expands it
        if (row.Item is not WorkItem it)
        {
            args.Attributes["data-empty"] = "";   // an empty column's placeholder: dropping on it lands in that column
            args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(row.Column, null));
            return;
        }
        args.Attributes["data-id"] = it.Id;
        args.Attributes["data-row"] = row.Key;
        if (selected.Contains(it.Id)) args.Attributes["data-selected"] = "";
        if (it.Hidden) args.Attributes["data-hidden"] = "";
        args.Attributes["onclick"] = EventCallback.Factory.Create<MouseEventArgs>(this, e => { if (e.ShiftKey || e.CtrlKey || e.MetaKey) ToggleSelect(it); });   // the board's select click, anywhere on the row
        args.Attributes["ondragstart"] = EventCallback.Factory.Create<DragEventArgs>(this, () => StartCardDrag(it, row.Column.Key));
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => DropBefore(row.Column, null, it));
    }

    /// <summary>A group row stands for its column: a drop on it lands at the column's end.</summary>
    private void GroupRowRender(GroupRowRenderEventArgs args)
    {
        BoardColumn col = GroupOf(args.Group!).Column;
        args.Attributes["data-group"] = col.Key;
        args.Attributes["ondrop"] = EventCallback.Factory.Create<DragEventArgs>(this, () => Drop(col, null));
        args.Expanded = !shutGroups.Contains(col.Key);
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
        if (grid.Groups.Count == 0)   // the grid always groups by the board column; the group panel stays hidden (AllowGrouping is off)
        {
            grid.Groups.Add(new GroupDescriptor { Property = nameof(GridRow.Group), Title = "Column" });
            await grid.Reload();
            return;
        }
        if (compactApplied != O.Compact)   // compact off: every row shows its subtasks; on: only rows opened by hand
        {
            compactApplied = O.Compact;
            List<GridRow> withSubtasks = rows.Where(r => r.Item is { Subtasks.Count: > 0 }).ToList();
            if (O.Compact) await grid.CollapseRows(withSubtasks.Where(grid.IsRowExpanded));
            else await grid.ExpandRows(withSubtasks.Where(r => !grid.IsRowExpanded(r)));
        }
    }

    private static void Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Remove(value)) set.Add(value);
    }

    /// <summary>Shows a module command's failure as a toast; successes refresh through Workspace.Changed.</summary>
    private bool Run((bool Success, string Message) result)
    {
        if (Workspace.Do(result).Success) return true;
        Popups.Toast(result.Message, ToastKind.Warning);
        return false;
    }

    private void OpenItem(int id) => Nav.NavigateTo($"item/{id}");

    /// <summary>A click opens the card; Shift-click (or Ctrl/⌘-click) adds it to the selection or removes it. A drag fires no click.</summary>
    private void CardClick(WorkItem it, MouseEventArgs e)
    {
        if (e.ShiftKey || e.CtrlKey || e.MetaKey) ToggleSelect(it);
        else OpenItem(it.Id);
    }

    private void ToggleSelect(WorkItem it) => Toggle(selected, it.Id);

    private void SelectAll(BoardColumn col) => selected.UnionWith(col.Items.Select(i => i.Id));

    /// <summary>False when the user backs out of archiving or the change fails.</summary>
    private async Task<bool> SetStatus(WorkItem it, string status)
    {
        if (status == "Completed" && !it.Archived)
        {
            if (!await Popups.Confirm("Complete and archive?",
                $"#wi{it.Id} {it.Title} will be archived with the OpenSpec CLI and its delta specs merged into openspec/specs. This can't be undone from the board.",
                yes: "Archive")) return false;
            return Run(await Popups.Busy($"Archiving #wi{it.Id} with the OpenSpec CLI…", () => WorkItemsApi.SetStatus(it.Id, status)));
        }
        return Run(WorkItemsApi.SetStatus(it.Id, status));
    }

    // The drag script in wwwroot/js/app.js delivers every drop to what sits right after the gap it opened:
    // a card, a column, or the lane / column / row itself when the gap is at the end.

    private void StartColumnDrag(IReadOnlyList<string> siblings, string key)
    {
        dragging = null;
        draggingColumn = (siblings, key);
    }

    /// <summary>A card drag carries the whole selection when the dragged card is part of it (as in a file manager).</summary>
    private void StartCardDrag(WorkItem it, string from)
    {
        draggingColumn = null;
        dragging = selected.Contains(it.Id) && selected.Count > 1
            ? Flat.SelectMany(f => f.Column.Items.Select(i => (Item: i, From: f.Column.Key)))   // board order
                .Where(t => selected.Contains(t.Item.Id))
                .GroupBy(t => t.Item.Id)   // an item in several columns (features, people) leaves the one dragged from
                .Select(g => g.FirstOrDefault(t => t.From == from, g.First()))
                .ToList()
            : [(it, from)];
    }

    /// <summary>A header dropped in front of a column in its own row (<paramref name="before"/>), or at the row's end (null).</summary>
    private void DropColumn(IReadOnlyList<string> row, string? before)
    {
        if (draggingColumn is not (IReadOnlyList<string> siblings, string key)) return;
        draggingColumn = null;
        if (row.Contains(key)) Set(BoardModel.Insert(O, siblings, [key], before));
    }

    private Task Drop(BoardColumn col, string? lane) => DropBefore(col, lane, null);

    private async Task DropBefore(BoardColumn col, string? lane, WorkItem? before)
    {
        if (draggingColumn is (IReadOnlyList<string> siblings, _)) { DropColumn(siblings, BoardModel.ColumnKey(O, col.Key)); return; }
        if (dragging is not { } moving) return;
        dragging = null;
        await Land(moving, col, lane, before?.Id);
    }

    /// <summary>Moves cards into a column, then places the ones that made it at the gap (in front of <paramref name="before"/>, or last).</summary>
    private async Task Land(IEnumerable<(WorkItem Item, string From)> moving, BoardColumn col, string? lane, int? before)
    {
        List<int> landed = [];
        foreach ((WorkItem it, string from) in moving)
            if (await MoveInto(it, from, col, lane)) landed.Add(it.Id);
        if (landed.Count > 0) Set(BoardModel.PlaceCards(O, col.Items.Select(i => i.Id).ToList(), landed, before));
    }

    /// <summary>Arrow keys move the selection: up/down a step in its column, left/right into the neighbouring column at the same height.</summary>
    private async Task KeyDown(KeyboardEventArgs e)
    {
        int dir = e.Key switch { "ArrowUp" => -1, "ArrowDown" => 1, "ArrowLeft" => -2, "ArrowRight" => 2, _ => 0 };
        if (dir == 0 || selected.Count == 0 || O.Unit == "subtasks") return;
        List<BoardColumn> columns = Flat.Select(f => f.Column).ToList();
        BoardColumn? col = columns.FirstOrDefault(c => selected.All(id => c.Items.Any(i => i.Id == id)));
        if (col is null) { Popups.Toast("Select cards in one column to move them with the arrow keys", ToastKind.Info); return; }
        List<int> ids = col.Items.Select(i => i.Id).ToList();
        if (Math.Abs(dir) == 1) { Set(BoardModel.Step(O, ids, ids.Where(selected.Contains).ToList(), dir)); return; }

        int to = columns.IndexOf(col) + Math.Sign(dir);
        if (to < 0 || to >= columns.Count) return;
        BoardColumn target = columns[to];
        int row = ids.FindIndex(selected.Contains);
        await Land(col.Items.Where(i => selected.Contains(i.Id)).Select(i => (i, col.Key)), target, null, row < target.Items.Count ? target.Items[row].Id : null);
    }

    /// <summary>Sets the dimension the column stands for (and its lane); false when the card can't move there.</summary>
    private async Task<bool> MoveInto(WorkItem it, string from, BoardColumn col, string? lane)
    {
        bool ok = from == col.Key || O.GroupBy switch
        {
            "bucket" or "feature" when it.Features.Any(f => f.Path == from && f.Locked) => Locked(),
            "bucket" or "feature" => Run(WorkItemsApi.SetManualFeatures(it.Id, it.Features.Select(f => f.Path).Where(p => p != from).Append(col.Key).Where(p => p != BoardModel.Unassigned))),
            "label" => Run(WorkItemsApi.SetLabel(it.Id, col.Key == BoardModel.Unassigned ? null : col.Key)),
            "contributor" => Run(WorkItemsApi.SetContributors(it.Id, it.Contributors.Where(p => p != from).Append(col.Key).Where(p => p != BoardModel.Unassigned))),
            _ => await SetStatus(it, col.Key),
        };
        if (ok && lane is not null) Run(WorkItemsApi.SetLane(it.Id, col.Key, lane));
        return ok;
    }

    private bool Locked()
    {
        Popups.Toast("This feature comes from the change's specs/ folder", ToastKind.Info);
        return false;
    }

    private async Task NewItem(string? columnKey)
    {
        string status = O.GroupBy == "status" && columnKey is not null ? columnKey : "Backlog";
        string[]? feature = O.GroupBy is "bucket" or "feature" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        string[]? who = O.GroupBy == "contributor" && !string.IsNullOrEmpty(columnKey) ? [columnKey] : null;
        (bool ok, string message, int id) = Workspace.CreateItem(status, feature, who);
        if (!ok) { Popups.Toast(message, ToastKind.Warning); return; }
        Popups.Toast($"Created #wi{id}", ToastKind.Success);
        OpenItem(id);
        await Task.CompletedTask;
    }

    private int Linked(string path) => WorkItemsApi.GetAll(includeHidden: true).Count(i => !i.Archived && i.Features.Any(f => f.Path == path));

    private static string Moving(int n) => n switch
    {
        0 => "Nothing is linked to it.",
        1 => "Its 1 work item moves to Unassigned.",
        _ => $"Its {n} work items move to Unassigned.",
    };

    private async Task DeleteFeature(Feature f)
    {
        if (!await Popups.Confirm($"Delete {f.Name}?", $"{Moving(Linked(f.Path))} It is removed from openspec/config.yaml.", yes: "Delete")) return;
        Run(WorkItemsApi.DeleteFeature(f.Path));
    }

    private Task<string?> Ask(string title, string placeholder, string action) => Popups.Prompt(title, placeholder, action);

    private async Task AddLane(string column, string label)
    {
        if (await Ask($"Add a subgroup to {label}", "e.g. Needs review", "Add") is string lane) Run(WorkItemsApi.AddLane(column, lane));
    }

    private async Task AddFeature(string bucket)
    {
        if (await Ask(bucket == FeaturesApi.Ungrouped ? "New feature" : $"New {bucket} feature", "e.g. Exports", "Create") is not string name) return;
        (bool ok, string message, Feature? _) = FeaturesApi.CreateFeature(bucket, name);
        Run((ok, message));
    }

    private void Merge()
    {
        (bool ok, string message, int id) = WorkItemsApi.Merge(selected.Order().ToList());
        if (!ok) { Popups.Toast(message, ToastKind.Warning); return; }
        selected.Clear();
        Workspace.Notify();
        Popups.Toast($"Merged into #wi{id}", ToastKind.Success);
    }

    private void HideSelected()
    {
        List<string> failed = [];
        foreach (int id in selected.Order().ToList())
        {
            (bool ok, string message) = WorkItemsApi.Hide(id);
            if (!ok) failed.Add($"#wi{id}: {message}");
        }
        selected.Clear();
        Workspace.Notify();
        if (failed.Count > 0) Popups.Toast(string.Join("; ", failed), ToastKind.Warning);
    }

    public void Dispose()
    {
        Workspace.Changed -= OnChanged;
        Settings.Changed -= OnSettings;
    }
}
