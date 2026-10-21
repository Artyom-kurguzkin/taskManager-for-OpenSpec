using Features;
using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    private readonly HashSet<int> openRows = [];
    private readonly HashSet<string> shutGroups = [];
    private bool expandAll;
    private (WorkItem Item, string From)? dragging;
    private (IReadOnlyList<string> Siblings, string Key)? draggingColumn;   // a column header or group name being dragged
    private string? dropTarget;

    internal static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List")];
    internal static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    internal static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("bucket", "bucket"), ("label", "label"), ("contributor", "contributor")];
    internal static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("label", "label"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id")];
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
    }

    private void Set(BoardOptions options)
    {
        O = options;
        if (Workspace.Root is string root) Settings.SaveBoard(root, O);
        Load();
    }

    private void SortBy(string key) => Set(O with { SortBy = key, SortDir = O.SortBy == key ? -O.SortDir : 1 });

    private void ToggleExpandAll()
    {
        expandAll = !expandAll;
        if (!expandAll) openRows.Clear();
    }

    private static void Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Remove(value)) set.Add(value);
    }

    /// <summary>Shows a module command's failure as a toast; successes refresh through Workspace.Changed.</summary>
    private void Run((bool Success, string Message) result)
    {
        if (!Workspace.Do(result).Success) Popups.Toast(result.Message, ToastKind.Warning);
    }

    private void OpenItem(int id) => Nav.NavigateTo($"item/{id}");

    private void CardClick(WorkItem it, MouseEventArgs e)
    {
        if (e.CtrlKey || e.MetaKey || e.ShiftKey || selected.Count > 0) ToggleSelect(it);
        else OpenItem(it.Id);
    }

    private void ToggleSelect(WorkItem it) => Toggle(selected, it.Id);

    private void SelectAll(BoardColumn col) => selected.UnionWith(col.Items.Select(i => i.Id));

    private async Task SetStatus(WorkItem it, string status)
    {
        if (status == "Completed" && !it.Archived)
        {
            if (!await Popups.Confirm("Complete and archive?",
                $"#wi{it.Id} {it.Title} will be archived with the OpenSpec CLI and its delta specs merged into openspec/specs. This can't be undone from the board.",
                yes: "Archive")) return;
        }
        Run(WorkItemsApi.SetStatus(it.Id, status));
    }

    private void StartColumnDrag(IReadOnlyList<string> siblings, string key)
    {
        dragging = null;
        draggingColumn = (siblings, key);
    }

    /// <summary>A header dropped onto another column takes that one's place, if they are siblings.</summary>
    private void DropOn(string targetKey)
    {
        dropTarget = null;
        if (draggingColumn is not (IReadOnlyList<string> siblings, string key)) return;
        draggingColumn = null;
        if (siblings.Contains(targetKey)) Set(BoardModel.MoveTo(O, siblings, key, targetKey));
    }

    private async Task Drop(BoardColumn col, string? lane)
    {
        if (draggingColumn is not null) { DropOn(BoardModel.ColumnKey(O, col.Key)); return; }
        dropTarget = null;
        if (dragging is not (WorkItem it, string from)) return;
        dragging = null;

        if (O.GroupBy == "status" && from != col.Key) await SetStatus(it, col.Key);
        else if (O.GroupBy is "bucket" or "feature" && from != col.Key)
        {
            if (it.Features.Any(f => f.Path == from && f.Locked)) { Popups.Toast("This feature comes from the change's specs/ folder", ToastKind.Info); return; }
            Run(WorkItemsApi.SetManualFeatures(it.Id, it.Features.Select(f => f.Path).Where(p => p != from).Append(col.Key).Where(p => p != BoardModel.Unassigned)));
        }
        else if (O.GroupBy == "label" && from != col.Key)
        {
            Run(WorkItemsApi.SetLabel(it.Id, col.Key == BoardModel.Unassigned ? null : col.Key));
        }
        else if (O.GroupBy == "contributor" && from != col.Key)
        {
            // only assignments move; commit authors stay contributors whatever column the card is dropped on
            Run(WorkItemsApi.SetContributors(it.Id, (it.Assigned ?? it.Contributors).Where(p => p != from).Append(col.Key).Where(p => p != BoardModel.Unassigned)));
            if (from != BoardModel.Unassigned && !(it.Assigned ?? []).Contains(from)) Popups.Toast($"{from} has commits on #wi{it.Id}, so they stay a contributor", ToastKind.Info);
        }

        if (lane is not null) Run(WorkItemsApi.SetLane(it.Id, col.Key, lane));
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
