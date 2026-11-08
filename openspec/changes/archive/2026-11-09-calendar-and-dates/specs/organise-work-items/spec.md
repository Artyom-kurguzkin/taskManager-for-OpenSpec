## ADDED Requirements

### Requirement: Calendar view
The layout menu SHALL offer a Calendar view beside Board and List. It SHALL show one month in whole weeks from Monday, with ‹ › moving a month and Today returning to the current one, and the current day marked.

#### Scenario: Next month and back
- **WHEN** the user clicks › and then Today
- **THEN** the heading reads the next month, then the current month again, whose current day is marked

### Requirement: Calendar bars
Every work item on the board (the same filters) with a start or due date SHALL be a bar from its start date to its due date, one day when only one is set, coloured by status, stacked so bars never overlap, with a dashed end where it continues into the week before or after. Clicking a bar SHALL open the item.

#### Scenario: A bar across a week boundary
- **WHEN** #wi123 started on Wednesday and is due the next Tuesday
- **THEN** one bar runs Wednesday to Sunday with a dashed right end, and another Monday to Tuesday with a dashed left end

#### Scenario: Only a due date
- **WHEN** #wi107 has a due date and no start date
- **THEN** it is a one-day bar on its due date

### Requirement: Items without dates
Open work items with neither a start nor a due date SHALL be listed beside the calendar under "No dates" with their count; completed items without dates SHALL NOT be listed. Clicking one SHALL open it.

#### Scenario: No dates
- **WHEN** #wi107 has no dates and #wi124 is completed without dates
- **THEN** "No dates" lists #wi107 and not #wi124

### Requirement: Group by time period
The board and list SHALL group by due date (Overdue, Today, This week, Next week, Later, No due date) and by start date (Earlier, This week, Next week, Later, Not started), seen from today, weeks running Monday to Sunday. Grouped by due date the list SHALL hide the Due column, grouped by start date the Started column.

#### Scenario: Due next week
- **WHEN** today is Wednesday 4 November and #wi124 is due 12 November
- **THEN** grouped by due date it is in Next week; grouped by start date without a start it is in Not started

### Requirement: Period filter
The expanded side menu SHALL have a period filter on the due date: any time, overdue, today, this week (today to Sunday), this month (today to the end of the month), no due date. It SHALL apply to the board, the list and the calendar, under any grouping, and be remembered per repository like the other view options.

#### Scenario: Overdue only
- **WHEN** the user picks "overdue"
- **THEN** only items due before today remain in every column

### Requirement: Dates on cards and rows
A card SHALL show "due d MMM" as a tag, filled red while the item is overdue and not completed. The list SHALL have Started and Due columns.

#### Scenario: Overdue card
- **WHEN** #wi123 was due yesterday
- **THEN** its card's due tag is red; an item due in two days has a plain due tag; an item without a due date has none

### Requirement: Show or hide a project from the project switcher
With several projects open, each project in the side menu's project switcher SHALL have an eye that hides it from the board, list and calendar, or shows it again, without switching to it and without closing the menu (the same setting as "Hide project" in the project's menu, so a hidden one is also listed under Hidden). There SHALL be no second list of projects in the view options.

#### Scenario: Hide from the switcher
- **WHEN** two projects are open and the user opens the switcher and clicks the eye next to the second
- **THEN** the board shows only the first, the first stays the active project, the eye shows crossed out, and clicking it again brings the second back

## MODIFIED Requirements

### Requirement: Board and list views
The side menu's layout dropdown SHALL switch between Board, List and Calendar; it is one control, not three buttons. The view options (group by, sort by, contributor filter, period filter, work items or subtasks, compact cards, show hidden) SHALL be in the expanded side menu. "Compact cards" SHALL show only the title band. The collapsed side menu SHALL also have a compact-cards button, highlighted while compact cards are on and disabled outside the board view.

#### Scenario: Switch to list
- **WHEN** the user opens the layout dropdown in the side menu and picks List
- **THEN** the list table replaces the board and the dropdown reads "List"

#### Scenario: Switch to calendar
- **WHEN** the user picks Calendar
- **THEN** the month calendar replaces the board and the dropdown reads "Calendar"

#### Scenario: Compact cards
- **WHEN** the user ticks "compact cards"
- **THEN** each card shows only its coloured title band

#### Scenario: Compact from the collapsed menu
- **WHEN** the side menu is collapsed and the user clicks the compact-cards button on the board
- **THEN** the cards show only their title bands, and the button stays highlighted until clicked again

### Requirement: List table
The list view SHALL be a data grid with columns ID, Title, Status, Label, Feature, Contributors, Priority, Progress, Updated, Started and Due, over the same rows the board shows, grouped by the current grouping with collapsible group headers showing a count and an "+ Add item" row; an empty column SHALL keep its group with a "No items" row. The user SHALL be able to sort by any column (clicking a header sorts, again reverses, a third click clears; several columns at once), filter a column from the funnel in its header (operator and value, optionally a second pair joined by and/or), resize columns, and reorder them by dragging the grip at the left of a header. A column SHALL be hidden from the × in its header and listed under Hidden in the side menu, where the eye brings it back, exactly as a board column is. The grid SHALL carry no toolbar or filter row of its own. All of this SHALL be remembered per repository.

#### Scenario: Sort by header
- **WHEN** the user clicks the Updated header twice
- **THEN** rows are sorted by date modified, first newest-first, then oldest-first, and the header shows the direction

#### Scenario: Filter
- **WHEN** the user opens the Title header's funnel, types "github" and applies
- **THEN** only rows whose title contains it remain, in their groups, and the other groups disappear until the filter is cleared

#### Scenario: Hide a column
- **WHEN** the user clicks the × in the Progress header and widens Title, then reopens the repository
- **THEN** Progress stays hidden, the side menu lists it under Hidden with an eye that brings it back, and Title keeps its width
