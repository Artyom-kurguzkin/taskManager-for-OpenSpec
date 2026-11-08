using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>The span of time the roadmap shows: whole months (a column per day) or whole weeks (a column per week).</summary>
public sealed record RoadmapWindow(DateOnly Start, DateOnly End, string Zoom)
{
    /// <summary>Days per column: 1 at month zoom, 7 at quarter zoom.</summary>
    public int Unit => Zoom == "quarter" ? 7 : 1;
    public int Columns => (End.DayNumber - Start.DayNumber + 1) / Unit;
    public int ColumnOf(DateOnly day) => (day.DayNumber - Start.DayNumber) / Unit;
}

/// <summary>A work item drawn on the roadmap: columns from the window's start; the flags say it continues past an edge.</summary>
public sealed record RoadmapBar(int Col, int Span, bool StartsBefore, bool EndsAfter);

/// <summary>A project's board sections, the roadmap's source (its columns are the groups).</summary>
public sealed record RoadmapSource(string Root, string Name, IReadOnlyList<BoardSection> Sections);

public sealed record RoadmapRow(string Root, WorkItem Item);

/// <summary>A column inside a row's group: its items.</summary>
public sealed record RoadmapSubgroup(string Key, string Name, IReadOnlyList<RoadmapRow> Rows);

/// <summary>
/// One roadmap group (with the project's name in front when several projects are open): a board column and its items, or, when the board
/// has rows, a row with its columns as <see cref="Subgroups"/> (<see cref="Rows"/> then holds the row's items, each once).
/// </summary>
public sealed record RoadmapGroup(string Key, string Name, IReadOnlyList<RoadmapRow> Rows, IReadOnlyList<RoadmapSubgroup>? Subgroups = null);

/// <summary>Pure layout for the calendar view, a roadmap (no IO, unit-tested directly).</summary>
public static class RoadmapModel
{
    /// <summary>The whole weeks (Monday to Sunday) that cover a month (the date-range picker's grid).</summary>
    public static IReadOnlyList<IReadOnlyList<DateOnly>> Weeks(DateOnly month)
    {
        DateOnly first = new(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
        List<IReadOnlyList<DateOnly>> weeks = [];
        for (DateOnly monday = Monday(first); monday <= last; monday = monday.AddDays(7))
            weeks.Add(Enumerable.Range(0, 7).Select(monday.AddDays).ToList());
        return weeks;
    }

    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Three months at month zoom, two quarters at quarter zoom (widened to whole weeks), from the 1st of the anchor's month.</summary>
    public static RoadmapWindow Window(DateOnly anchor, string zoom)
    {
        DateOnly first = new(anchor.Year, anchor.Month, 1);
        if (zoom != "quarter") return new(first, first.AddMonths(3).AddDays(-1), "month");
        DateOnly last = first.AddMonths(6).AddDays(-1);
        return new(Monday(first), last.AddDays(6 - (((int)last.DayOfWeek + 6) % 7)), "quarter");
    }

    /// <summary>The anchor one step on: a month at month zoom, a quarter at quarter zoom.</summary>
    public static DateOnly Move(DateOnly anchor, string zoom, int dir) => anchor.AddMonths(dir * (zoom == "quarter" ? 3 : 1));

    private static (DateOnly Start, DateOnly End)? Range(WorkItem it)
    {
        if ((it.Started ?? it.Due) is not DateOnly s) return null;
        DateOnly e = it.Due ?? s;
        return e < s ? (e, s) : (s, e);
    }

    /// <summary>The bar from the start date to the due date (one day when only one is set), clipped to the window; null when undated or outside it.</summary>
    public static RoadmapBar? Bar(WorkItem item, RoadmapWindow w)
    {
        return Range(item) is { } r ? Clip(r.Start, r.End, w) : null;
    }

    /// <summary>A start-to-end span (a group's, say) as a bar clipped to the window; null when it lies outside it.</summary>
    public static RoadmapBar? Clip(DateOnly s, DateOnly e, RoadmapWindow w)
    {
        if (e < w.Start || s > w.End) return null;
        int col = w.ColumnOf(s > w.Start ? s : w.Start), end = w.ColumnOf(e < w.End ? e : w.End);
        return new(col, end - col + 1, s < w.Start, e > w.End);
    }

    /// <summary>The earliest start and latest due date of the dated items, or null when none has a date.</summary>
    public static (DateOnly Start, DateOnly End)? GroupSpan(IEnumerable<WorkItem> items)
    {
        List<(DateOnly Start, DateOnly End)> ranges = items.Select(Range).OfType<(DateOnly, DateOnly)>().ToList();
        return ranges.Count == 0 ? null : (ranges.Min(r => r.Start), ranges.Max(r => r.End));
    }

    /// <summary>
    /// The board's columns as roadmap groups (an item in several columns is in each); with rows, a group per row that has items,
    /// the columns that have items inside it as subgroups.
    /// </summary>
    public static IReadOnlyList<RoadmapGroup> Groups(IEnumerable<RoadmapSource> sources, bool multi)
    {
        List<RoadmapGroup> groups = [];
        foreach (RoadmapSource p in sources)
        {
            string Name(string label) => multi ? p.Name + " · " + label : label;
            List<RoadmapRow> Of(IEnumerable<WorkItem> items) => items.Select(i => new RoadmapRow(p.Root, i)).ToList();
            foreach (BoardSection s in p.Sections)
                if (s.Rows is null)
                    groups.AddRange(s.Columns.Select(c => new RoadmapGroup(p.Root + "|" + c.Key, Name(c.Label), Of(c.Items))));
                else
                    groups.AddRange(s.Rows.Where(r => r.Items.Count > 0).Select(r => new RoadmapGroup(p.Root + "|" + r.Key, Name(r.Label), Of(r.Items),
                        [.. r.Columns.Where(c => c.Items.Count > 0).Select(c => new RoadmapSubgroup(p.Root + "|" + r.Key + "|" + c.Key, c.Label, Of(c.Items)))])));
        }
        return groups;
    }
}
