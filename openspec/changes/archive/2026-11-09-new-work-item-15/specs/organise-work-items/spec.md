## ADDED Requirements

### Requirement: Roadmap view
The Calendar view SHALL be a roadmap. The left pane SHALL list the work items in the board's groups (group by), each group with a collapsible header showing its name and count, each item with its title and `#wiN`. The right pane SHALL be a timeline: at Month zoom one column per day over three months, at Quarter zoom one column per week over two quarters, with month names and date ticks in a sticky header and a line on today. Clicking a row or a bar SHALL open the item.

#### Scenario: Quarter and next
- **WHEN** the user picks Quarter and clicks next
- **THEN** the columns are weeks and the window starts a quarter later; Today brings it back

### Requirement: Roadmap bars
Every item with a start or due date SHALL be a bar from its start to its due date (one day when only one is set), coloured by status, clipped with a dashed end at the window's edge; an undated item SHALL keep its row with no bar. Each group SHALL show a bar spanning its earliest start to its latest due with that range as text. Today SHALL bring the window back to today; previous and next SHALL move it by a month at Month zoom and a quarter at Quarter zoom.

#### Scenario: Bars in the board's groups
- **WHEN** the board groups by status and #wi123 (Backlog, 3 to 9 Nov) and #wi107 (In Progress, due 12 Nov) are shown at Month zoom
- **THEN** Backlog lists #wi123 with a seven-day bar and In Progress lists #wi107 with a one-day bar on 12 Nov, each group with a bar spanning its items' dates
