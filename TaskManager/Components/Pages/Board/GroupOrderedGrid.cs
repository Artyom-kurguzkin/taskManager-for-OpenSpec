using Radzen.Blazor;

namespace TaskManager.Components.Pages;

/// <summary>
/// The list's grid with its groups kept in the board's order. Radzen groups the sorted view by first occurrence, so a header sort would put the groups in the order
/// of their first rows; this view is stably ordered by group (then subgroup) after the header sort, so the sort only orders the rows inside each group (#wi153).
/// A Radzen subclass, not a component of ours: it cannot derive from RtmComponent, so it must stay markup-free (no render tree of its own).
/// </summary>
public sealed class GroupOrderedGrid : RadzenDataGrid<GridRow>
{
    public override IQueryable<GridRow> View => base.View.OrderBy(r => r.Group).ThenBy(r => r.SwimIndex);
}
