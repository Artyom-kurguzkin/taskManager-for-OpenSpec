## ADDED Requirements

### Requirement: List column headers show their controls only on hover
A list column header SHALL show only the column's name at rest, plus a mark for what is on (sort arrow, filled funnel). While the pointer is over it, a control in it has keyboard focus, or its filter panel is open, the name SHALL hide and the filter, sort and hide controls SHALL share its width, each at least 24 by 24 pixels with a gap. The header SHALL show a grab or drag cursor only during a drag.

#### Scenario: A header at rest
- **WHEN** the pointer is not over a list column header and no sort or filter is on for that column
- **THEN** the header shows only the column's name

#### Scenario: A sorted column at rest
- **WHEN** the list is sorted by a column and the pointer is elsewhere
- **THEN** that header shows its name and the sort arrow

#### Scenario: Pointing at a header
- **WHEN** the pointer is over a column header
- **THEN** the name is hidden and the filter, sort and hide controls are spread across the header, each at least 24 by 24 pixels
- **AND** the pointer shows no grab or drag cursor

#### Scenario: Clicking a header and moving on
- **WHEN** the user clicks a column header and then moves the pointer without pressing
- **THEN** the cursor stays the normal one over the page and no drag state is set
