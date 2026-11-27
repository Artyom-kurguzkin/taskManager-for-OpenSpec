using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TaskManager.Components.Pages;
using WorkItems.PublicDTOs;
using TaskManager.Helpers;

namespace TaskManager.Components.Pages;

public partial class Roadmap
{
    // A click draws the calendar only for what it changed: the handlers below say so (StateHasChanged). A click on a bar or row is the board's (it draws, and the
    // calendar with it as its parameters are set); the controls stop the click, so it is not taken for one on the background, which lets go of the examined item.
    protected override bool Strict => true;

    /// <summary>The open projects' boards: each board column is a group.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<RoadmapSource> Projects { get; set; } = [];

    /// <summary>Several projects are open: group names carry the project's name.</summary>
    [Parameter] public bool Multi { get; set; }

    /// <summary>The field the bars and dots are coloured by (the board's colour option).</summary>
    [Parameter] public string ColorBy { get; set; } = "status";

    /// <summary>A row or bar was clicked: the project's root, the work item's id and the click (its modifiers and count).</summary>
    [Parameter] public EventCallback<(string Root, int Id, MouseEventArgs E)> OnOpen { get; set; }
    /// <summary>Whether an item is selected, as the board's cards wear it.</summary>
    [Parameter] public Func<string, int, bool>? SelectedOf { get; set; }
    /// <summary>A click that did not come from an item (the board lets go of the examined one).</summary>
    [Parameter] public EventCallback OnBackground { get; set; }
    /// <summary>The examine mark of an item (ex-on, ex-waits, ex-needs) or null, as the board's cards wear it.</summary>
    [Parameter] public Func<string, int, string?>? ExamineOf { get; set; }

    /// <summary>The menu of a column's group (project root, column key): the board's column actions. Null leaves the group names plain text.</summary>
    [Parameter] public RenderFragment<(string Root, string Column)>? GroupMenu { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>The day by the app's clock.</summary>
    private DateOnly Today => Ui.Today(Ui.ClockOf(Services));
    private DateOnly? anchored;   // the month shown; the clock's day until a move
    private DateOnly anchor { get => anchored ?? Today; set => anchored = value; }
    private string zoom = "month";
    private readonly HashSet<string> collapsed = [];   // groups folded shut, by key

    // Worked out once per set of parameters (the groups) and once per move or zoom (the window), not each time the markup asks.
    private RoadmapWindow? window;
    private DateOnly windowAnchor;
    private string windowZoom = "";
    private RoadmapWindow Window
    {
        get
        {
            if (window is null || windowAnchor != anchor || windowZoom != zoom) (window, windowAnchor, windowZoom) = (RoadmapModel.Window(anchor, zoom), anchor, zoom);
            return window;
        }
    }
    private int Cw => zoom == "quarter" ? 44 : 26;   // column width in px
    private IReadOnlyList<RoadmapGroup>? groups;
    private IReadOnlyList<RoadmapGroup> Groups => groups ??= RoadmapModel.Groups(Projects, Multi);

    protected override void OnParametersSet() => groups = null;

    private void Zoom(string key) { zoom = key; StateHasChanged(); }
    private void Go(DateOnly to) { anchor = to; StateHasChanged(); }
    private string Colour(RoadmapRow r)
    {
        RoadmapSource? p = Projects.FirstOrDefault(p => p.Root == r.Root);
        return BoardModel.ColourOf(r.Item, ColorBy, Today, p?.Colours);
    }

    private string RangeLabel
    {
        get
        {
            DateOnly first = new(anchor.Year, anchor.Month, 1);
            return Ui.Month(first) + " – " + Ui.Month(first.AddMonths(zoom == "quarter" ? 5 : 2));
        }
    }

    /// <summary>The month names across the header: each starts at its first column and runs to the next month's.</summary>
    private IEnumerable<(string Label, int Col, int Span)> Months
    {
        get
        {
            RoadmapWindow w = Window;
            DateOnly first = new(anchor.Year, anchor.Month, 1);
            int count = zoom == "quarter" ? 6 : 3;
            for (int i = 0; i < count; i++)
            {
                DateOnly m = first.AddMonths(i);
                int col = i == 0 ? 0 : w.ColumnOf(m), next = i == count - 1 ? w.Columns : w.ColumnOf(m.AddMonths(1));
                yield return (zoom == "quarter" ? Ui.MonthName(m) : Ui.MonthLong(m), col, next - col);
            }
        }
    }

    private (RoadmapBar Bar, string Text)? GroupBar(IReadOnlyList<RoadmapRow> rows)
    {
        if (RoadmapModel.GroupSpan(rows.Select(r => r.Item)) is not { } span) return null;
        (DateOnly s, DateOnly e) = span;
        return RoadmapModel.Clip(s, e, Window) is { } bar ? (bar, $"{Ui.DayWithWeekday(s)} - {Ui.DayWithWeekday(e)}") : null;
    }

    private static string ToggleLabel(string name, bool shut) => (shut ? "Expand " : "Collapse ") + name;

    private void Toggle(string key)
    {
        if (!collapsed.Remove(key)) collapsed.Add(key);
        StateHasChanged();
    }

    private static string Describe(WorkItem it) =>
        $"#wi{it.Id} {it.Title} · {it.Status}" + (it.Started is DateOnly s ? $" · started {Ui.Day(s)}" : "") + (it.Due is DateOnly d ? $" · due {Ui.Day(d)}" : "");
}
