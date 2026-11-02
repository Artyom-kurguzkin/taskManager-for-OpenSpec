using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>A work item drawn across one week of the calendar: columns 0–6 from Monday, stacked in lanes so bars never overlap.</summary>
public sealed record CalendarBar(WorkItem Item, int Col, int Span, bool StartsBefore, bool EndsAfter, int Lane);

/// <summary>Pure month layout for the calendar view (no IO, unit-tested directly).</summary>
public static class CalendarModel
{
    /// <summary>The whole weeks (Monday to Sunday) that cover a month.</summary>
    public static IReadOnlyList<IReadOnlyList<DateOnly>> Weeks(DateOnly month)
    {
        DateOnly first = new(month.Year, month.Month, 1), last = first.AddMonths(1).AddDays(-1);
        List<IReadOnlyList<DateOnly>> weeks = [];
        for (DateOnly monday = Monday(first); monday <= last; monday = monday.AddDays(7))
            weeks.Add(Enumerable.Range(0, 7).Select(monday.AddDays).ToList());
        return weeks;
    }

    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Bars for the items that touch a week: from the start date to the due date (one day when only one is set).</summary>
    public static IReadOnlyList<CalendarBar> Bars(IEnumerable<WorkItem> items, IReadOnlyList<DateOnly> week)
    {
        DateOnly a = week[0], z = week[^1];
        List<CalendarBar> bars = [];
        foreach (WorkItem it in items)
        {
            if ((it.Started ?? it.Due) is not DateOnly s) continue;
            DateOnly e = it.Due ?? s;
            if (e < s) (s, e) = (e, s);
            if (e < a || s > z) continue;
            int col = (s > a ? s : a).DayNumber - a.DayNumber, end = (e < z ? e : z).DayNumber - a.DayNumber;
            bars.Add(new(it, col, end - col + 1, s < a, e > z, 0));
        }
        // first-fit lanes: longest bars first at each start column, each in the first lane that is free by then
        List<int> laneEnd = [];
        List<CalendarBar> placed = [];
        foreach (CalendarBar b in bars.OrderBy(b => b.Col).ThenByDescending(b => b.Span).ThenBy(b => b.Item.Id))
        {
            int lane = laneEnd.FindIndex(end => end < b.Col);
            if (lane < 0) { lane = laneEnd.Count; laneEnd.Add(0); }
            laneEnd[lane] = b.Col + b.Span - 1;
            placed.Add(b with { Lane = lane });
        }
        return placed;
    }

    /// <summary>Open items with neither a start nor a due date: they have no place on the calendar yet. Completed ones are done, not unscheduled.</summary>
    public static IReadOnlyList<WorkItem> Unscheduled(IEnumerable<WorkItem> items) => items.Where(i => i.Started is null && i.Due is null && !i.Archived).ToList();
}
