using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using TaskManager.Components.Pages;
using WorkItems.PublicDTOs;
using TaskManager.Helpers;

namespace TaskManager.Components.Pages;

public partial class Analytics
{
    /// <summary>The work items to chart: the board's, after its contributor, agent and period filters.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<WorkItem> Items { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<Feature> Features { get; set; } = [];

    /// <summary>The worklog events of a work item (the charts over time replay its status moves).</summary>
    [Parameter, EditorRequired] public Func<int, IReadOnlyList<WorkItemEvent>> Events { get; set; } = _ => [];

    /// <summary>The board's charts, as saved.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartSpec> Charts { get; set; } = [];

    /// <summary>Every change (edit, add, delete) as the whole new list, to be saved in the board's options.</summary>
    [Parameter] public EventCallback<IReadOnlyList<ChartSpec>> ChartsChanged { get; set; }

    /// <summary>The project the charts are of, named when several are open.</summary>
    [Parameter] public string Project { get; set; } = "";

    private string? editing;   // the chart whose editor is open

    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>How many charts have been worked out from the items (a test counts them).</summary>
    public int Builds { get; private set; }

    // A chart shows its spec, the items, the features and the day (the weeks run to it). The board draws this component whenever anything on it
    // changes, with new lists of the same items: the data of a chart is kept by (spec, items version) and worked out again only when one of them moves.
    private long version;   // bumped when the items or the features are not the ones the charts were last worked out from
    private IReadOnlyList<WorkItem> seenItems = [];
    private IReadOnlyList<Feature> seenFeatures = [];
    private DateOnly seenDay;
    private readonly Dictionary<string, (ChartSpec Spec, ChartData Data)> data = [];

    private static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) => ReferenceEquals(a, b) || (a.Count == b.Count && a.Zip(b).All(p => ReferenceEquals(p.First, p.Second)));

    protected override void OnParametersSet()
    {
        DateOnly day = Ui.Today(Ui.ClockOf(Services));
        if (Same(Items, seenItems) && Same(Features, seenFeatures) && day == seenDay) return;
        (seenItems, seenFeatures, seenDay) = (Items, Features, day);
        version++;
        data.Clear();
    }

    /// <summary>The parameter-set draw waits for what the page shows: the charts' specs, the project's name, and the version of the items.</summary>
    protected override object? RenderKey => (version, Project, string.Join("|", Charts.Select(c => c.ToString())));

    private ChartData DataOf(ChartSpec c)
    {
        if (data.TryGetValue(c.Id, out var hit) && hit.Spec == c) return hit.Data;
        Builds++;
        ChartData built = AnalyticsModel.Build(c, Items, Events, Features, Ui.Now(Ui.ClockOf(Services)));
        data[c.Id] = (c, built);
        return built;
    }

    private Task Change(ChartSpec changed) => ChartsChanged.InvokeAsync([.. Charts.Select(c => c.Id == changed.Id ? changed : c)]);

    private Task Retitle(ChartSpec c, ChangeEventArgs e) => Change(c with { Title = e.Value?.ToString() is { Length: > 0 } t ? t : c.Title });   // a cleared title keeps the old one

    private Task Add()
    {
        ChartSpec added = AnalyticsModel.NewChart();
        editing = added.Id;
        return ChartsChanged.InvokeAsync([.. Charts, added]);
    }

    private Task Delete(ChartSpec gone)
    {
        editing = null;
        return ChartsChanged.InvokeAsync([.. Charts.Where(c => c.Id != gone.Id)]);
    }
}
