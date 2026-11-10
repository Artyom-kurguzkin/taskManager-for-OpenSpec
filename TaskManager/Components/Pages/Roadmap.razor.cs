using System.Globalization;
using Microsoft.AspNetCore.Components;
using TaskManager.Board;
using WorkItems.PublicDTOs;

namespace TaskManager.Components.Pages;

public partial class Roadmap
{
    /// <summary>The open projects' boards: each board column is a group.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<RoadmapSource> Projects { get; set; } = [];

    /// <summary>Several projects are open: group names carry the project's name.</summary>
    [Parameter] public bool Multi { get; set; }

    /// <summary>The field the bars and dots are coloured by (the board's colour option).</summary>
    [Parameter] public string ColorBy { get; set; } = "status";

    /// <summary>A row or bar was clicked: the project's root and the work item's id.</summary>
    [Parameter] public EventCallback<(string Root, int Id)> OnOpen { get; set; }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private DateOnly anchor = Today;
    private string zoom = "month";
    private readonly HashSet<string> collapsed = [];   // groups folded shut, by key

    private RoadmapWindow Window => RoadmapModel.Window(anchor, zoom);
    private int Cw => zoom == "quarter" ? 44 : 26;   // column width in px
    private IReadOnlyList<RoadmapGroup> Groups => RoadmapModel.Groups(Projects, Multi);
    private string Colour(RoadmapRow r)
    {
        RoadmapSource? p = Projects.FirstOrDefault(p => p.Root == r.Root);
        return BoardModel.ColourOf(r.Item, ColorBy, null, p?.Colours);
    }

    private string RangeLabel
    {
        get
        {
            DateOnly first = new(anchor.Year, anchor.Month, 1);
            return first.ToString("MMM yyyy") + " – " + first.AddMonths(zoom == "quarter" ? 5 : 2).ToString("MMM yyyy");
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
                yield return (m.ToString(zoom == "quarter" ? "MMMM" : "MMMM yyyy"), col, next - col);
            }
        }
    }

    private (RoadmapBar Bar, string Text)? GroupBar(IReadOnlyList<RoadmapRow> rows)
    {
        if (RoadmapModel.GroupSpan(rows.Select(r => r.Item)) is not { } span) return null;
        (DateOnly s, DateOnly e) = span;
        return RoadmapModel.Clip(s, e, Window) is { } bar ? (bar, $"{s.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} - {e.ToString("ddd, MMM d", CultureInfo.CurrentCulture)}") : null;
    }

    private static string ToggleLabel(string name, bool shut) => (shut ? "Expand " : "Collapse ") + name;

    private void Toggle(string key)
    {
        if (!collapsed.Remove(key)) collapsed.Add(key);
    }

    private static string Describe(WorkItem it) =>
        $"#wi{it.Id} {it.Title} · {it.Status}" + (it.Started is DateOnly s ? $" · started {s:d MMM}" : "") + (it.Due is DateOnly d ? $" · due {d:d MMM}" : "");
}
