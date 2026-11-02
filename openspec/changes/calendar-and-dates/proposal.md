## Why

Work items had no dates. There was no way to see when work started, when it is due, or what is due this week, and nothing like Planner's schedule.

## What Changes

- Two dates per work item in `x-rtm`: `started` (stamped the first time an item moves to In Progress, kept after that) and `due`. Both are set on the work item page with native date pickers; clearing one removes it.
- A **Calendar** view next to Board and List: a month in whole weeks from Monday, each dated item a bar from its start to its due date (one day when only one is set), coloured by status, stacked without overlap, continuing across weeks with dashed ends; open items without dates in a "No dates" list at the side. ‹ › move a month, Today comes back. A bar opens its item.
- Board and list group by **due date** (Overdue, Today, This week, Next week, Later, No due date) and by **start date** (Earlier, This week, Next week, Later, Not started).
- A **period** filter in the side menu (any time, overdue, today, this week, this month, no due date) on the due date, in every view.
- Cards show "due d MMM", filled red when overdue; the list has Started and Due columns.
- `openspec/REPO-TASKS.md` tells agents about the two keys (guide version 2).

## Capabilities

### Modified Capabilities

- `organise-work-items`: calendar view, period grouping and filter, dates on cards and rows.
- `work-item-details`: start and due dates.
