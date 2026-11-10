using Features.PublicDTOs;
using Microsoft.AspNetCore.Components;
using TaskManager.Board;
using WorkItems.PublicDTOs;

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
    private readonly Dictionary<int, IReadOnlyList<WorkItemEvent>> log = [];   // read once per item per render, whatever the number of charts

    protected override void OnParametersSet() => log.Clear();

    private ChartData DataOf(ChartSpec c) =>
        AnalyticsModel.Build(c, Items, id => log.TryGetValue(id, out IReadOnlyList<WorkItemEvent>? e) ? e : log[id] = Events(id), Features, DateTime.Now);

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
