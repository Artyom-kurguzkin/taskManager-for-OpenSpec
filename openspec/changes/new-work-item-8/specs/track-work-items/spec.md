## ADDED Requirements

### Requirement: Analytics view
The board SHALL offer an Analytics view beside Board, List and Calendar that shows the board's saved charts, two by default: "Items by status" and "Status over time". Charts SHALL be drawn in the board's colours, follow the board's contributor and period filters, and chart the active project.

#### Scenario: Open Analytics
- **WHEN** the user picks Analytics from the Layout menu on a board with no saved charts
- **THEN** two charts show: a column per status with its count, and twelve weeks of stacked status counts with a legend

### Requirement: Customise a chart
A chart SHALL be defined by its layout (column, bar, line, stacked column), X-axis (status, feature, label, priority, contributor, or time by week), an optional group by, and a count of work items or of subtasks. The user SHALL edit a chart in place, add one and delete one, and the charts SHALL be remembered per board. A chart over time SHALL be computed from each item's worklog (its creation and status changes).

#### Scenario: By feature, grouped by status
- **WHEN** the user adds a chart and sets it to bar, X-axis feature, group by status
- **THEN** the chart shows one group of bars per feature with a bar per status, and it is still there after the app restarts

#### Scenario: Subtasks
- **WHEN** the user sets a chart's Y-axis to subtasks
- **THEN** each mark counts subtasks instead of work items
