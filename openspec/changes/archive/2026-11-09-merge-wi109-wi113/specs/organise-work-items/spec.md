## ADDED Requirements

### Requirement: Organise by columns, rows and colour
The side menu SHALL have an "Organise" block with three independent choices over the same fields (status, feature, label, priority, contributor, due date, start date): columns, rows (or none) and colour (or none). The board SHALL show the columns across and, with a row field, one row per value under the column headers, a multi-valued field putting an item in each of its rows; empty rows SHALL stay so a card can be dropped there.

#### Scenario: Rows by feature
- **WHEN** columns are by status and rows by feature
- **THEN** the board shows the status columns once on top and a row per feature, each with a cell per status

### Requirement: Dropping sets both fields
Dropping a card into a cell SHALL set the column's field and, when it differs, the row's field. A due-date or start-date period SHALL NOT be set by a drop: the card stays where it was for that dimension and a message says why.

#### Scenario: Drop across rows
- **WHEN** columns are by status and rows by feature, and the user drags an unassigned Backlog card into the Maintainability row of Planned
- **THEN** the card's status is Planned and its feature Maintainability

### Requirement: Colour by any field
Every card band, list row stripe and roadmap bar SHALL take its colour from the colour field: status, label and priority by their own colours, due and start dates by period, feature and contributor by a stable colour per value, none in a neutral colour. A legend under the colour choice SHALL name the colours shown.

#### Scenario: Colour by label
- **WHEN** colour is by label
- **THEN** bug cards are peach, feature cards sky and enhancement cards sage, and the legend lists them

### Requirement: The same organisation in every view
The list SHALL group its rows by the row field, then by the column field, and the roadmap SHALL group its items the same way (a row header with column subgroups), both coloured by the colour field. With rows none they group by the columns alone.

#### Scenario: List and roadmap by feature and status
- **WHEN** rows are by feature and columns by status, and the user opens the list and then the calendar
- **THEN** both show a group per feature holding a group per status

## MODIFIED Requirements

### Requirement: Column header actions
Clicking a column header SHALL open a menu with: add a work item in this column, select all cards in the column, hide the column, and (for features created from the board) delete the feature.

#### Scenario: Add from header
- **WHEN** the user picks "Add work item here" on the Blocked column
- **THEN** a new change is created with status Blocked and its detail page opens

## REMOVED Requirements

### Requirement: Custom sub-categories
**Reason**: swimlanes replace per-column subgroups.

### Requirement: Priority subgroups
**Reason**: swimlanes by priority replace the automatic priority subgroups.
