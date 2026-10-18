using Features;
using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
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
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;

    private BoardOptions O = new();
    private IReadOnlyList<WorkItem> items = [];
    private IReadOnlyList<Feature> features = [];
    private IReadOnlyList<BoardSection> Sections = [];
    private readonly HashSet<int> selected = [];
    private readonly HashSet<int> openRows = [];
    private readonly HashSet<string> shutGroups = [];
    private bool expandAll;
    private (WorkItem Item, string From)? dragging;
    private string? dropTarget;

    private static readonly IReadOnlyList<(string, string)> Views = [("board", "Board"), ("list", "List")];
    private static readonly IReadOnlyList<(string, string)> Units = [("tasks", "Work items"), ("subtasks", "Subtasks")];
    private static readonly IReadOnlyList<(string, string)> GroupBys = [("status", "status"), ("feature", "feature"), ("contributor", "contributor")];
    private static readonly IReadOnlyList<(string, string)> SortBys =
        [("priority", "priority"), ("feature", "feature"), ("who", "contributor"), ("modified", "date modified"), ("created", "date created"), ("status", "status"), ("title", "title"), ("id", "id")];
    private static readonly IReadOnlyList<(string, string)> Priorities = [("high", "high"), ("low", "lower")];
    private static readonly IReadOnlyList<(string, string)> Statuses = WorkItemsApi.Statuses.Select(s => (s, s)).ToList();
    private static readonly IReadOnlyList<(string Key, string Label)> Columns =
        [("id", "ID"), ("title", "Title"), ("status", "Status"), ("feature", "Features"), ("who", "Contributors"), ("priority", "Priority"), ("progress", "Progress"), ("modified", "Updated")];

    private IReadOnlyList<(string, string)> People =>
        [("all", "everyone"), .. items.SelectMany(i => i.Contributors).Distinct().Order().Select(p => (p, p))];

    private string CurrentCategory => BoardModel.Categories(features).Contains(O.Category) ? O.Category : "all";
    private IReadOnlyList<(string, string)> CategoryOptions => [("all", "All groups"), .. BoardModel.Categories(features).Select(c => (c, c))];
    private IReadOnlyDictionary<string, int> CategoryCounts =>
        features.GroupBy(f => f.Group).ToDictionary(g => g.Key, g => items.Count(i => i.Features.Any(f => g.Any(x => x.Path == f.Path))));

    private IEnumerable<(string? Category, BoardColumn Column)> Flat => Sections.SelectMany(s => s.Columns.Select(c => (s.Category, c)));

    protected override void OnInitialized()
    {
        Workspace.Changed += OnChanged;
        if (Workspace.Root is string root) O = Settings.Board(root);
        Load();
    }

    private void OnChanged() => InvokeAsync(() => { Load(); StateHasChanged(); });

    private void Load()
    {
        items = WorkItemsApi.GetAll(includeHidden: O.ShowHidden);
        features = FeaturesApi.GetAll();
        Sections = BoardModel.Build(items, features, WorkItemsApi.GetLanes(), O with { Category = CurrentCategory });
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
        if (!Workspace.Do(result).Success) Snackbar.Add(result.Message, Severity.Warning);
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
            bool? ok = await Dialogs.ShowMessageBoxAsync("Complete and archive?",
                $"#wi{it.Id} {it.Title} will be archived with the OpenSpec CLI and its delta specs merged into openspec/specs. This can't be undone from the board.",
                yesText: "Archive", cancelText: "Cancel");
            if (ok != true) return;
        }
        Run(WorkItemsApi.SetStatus(it.Id, status));
    }

    private async Task Drop(BoardColumn col, string? lane)
    {
        dropTarget = null;
        if (dragging is not (WorkItem it, string from)) return;
        dragging = null;

        if (O.GroupBy == "status" && from != col.Key) await SetStatus(it, col.Key);
        else if (O.GroupBy == "feature" && from != col.Key)
        {
            if (it.Features.Any(f => f.Path == from && f.Locked)) { Snackbar.Add("This feature comes from the change's specs/ folder", Severity.Info); return; }
            Run(WorkItemsApi.SetManualFeatures(it.Id, it.Features.Select(f => f.Path).Where(p => p != from).Append(col.Key)));
        }
        else if (O.GroupBy == "contributor" && from != col.Key)
            Run(WorkItemsApi.SetContributors(it.Id, it.Contributors.Where(p => p != from).Append(col.Key)));

        if (lane is not null) Run(WorkItemsApi.SetLane(it.Id, col.Key, lane));
    }

    private async Task NewItem(string? columnKey)
    {
        string status = O.GroupBy == "status" && columnKey is not null ? columnKey : "Backlog";
        string[]? feature = O.GroupBy == "feature" && columnKey is not null ? [columnKey] : null;
        string[] who = O.GroupBy == "contributor" && columnKey is not null ? [columnKey] : [Workspace.Me];
        (bool ok, string message, int id) = WorkItemsApi.Create("New work item", status, feature, who);
        if (!ok) { Snackbar.Add(message, Severity.Warning); return; }
        Workspace.Notify();
        Snackbar.Add($"Created #wi{id}", Severity.Success);
        OpenItem(id);
        await Task.CompletedTask;
    }

    private async Task<string?> Ask(string title, string placeholder, string action)
    {
        IDialogReference d = await Dialogs.ShowAsync<TextPrompt>(title, new DialogParameters<TextPrompt>
        {
            { p => p.Title, title }, { p => p.Placeholder, placeholder }, { p => p.Action, action },
        });
        DialogResult? r = await d.Result;
        return r is { Canceled: false, Data: string s } ? s : null;
    }

    private async Task AddLane(string column)
    {
        if (await Ask($"Add a sub-category to {column}", "e.g. Needs review", "Add") is string lane) Run(WorkItemsApi.AddLane(column, lane));
    }

    private async Task AddSubgroup()
    {
        string group = CurrentCategory == "all" ? BoardModel.Categories(features).FirstOrDefault(c => c != FeaturesApi.Ungrouped) ?? FeaturesApi.Ungrouped : CurrentCategory;
        if (await Ask($"New sub-group in {group}", "e.g. Exports", "Create") is not string name) return;
        (bool ok, string message, Feature? _) = FeaturesApi.CreateSubgroup(group, name);
        Run((ok, message));
    }

    private void Merge()
    {
        (bool ok, string message, int id) = WorkItemsApi.Merge(selected.Order().ToList());
        if (!ok) { Snackbar.Add(message, Severity.Warning); return; }
        selected.Clear();
        Workspace.Notify();
        Snackbar.Add($"Merged into #wi{id}", Severity.Success);
    }

    public void Dispose() => Workspace.Changed -= OnChanged;
}
