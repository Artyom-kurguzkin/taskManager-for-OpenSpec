# Spec Delta

## Purpose

The home screen of the app: shows the repository's work items grouped, sorted and filtered, and lets the user move, create and merge them directly on the board.

## ADDED Requirements

### Requirement: Group work items
The board SHALL group work items by status (one column per status, in workflow order), by contributor (one column per person), or by feature (one section per feature group, one column per feature). A work item with several features or contributors SHALL appear in each matching column.

#### Scenario: Group by feature
- **WHEN** the user picks "group by feature"
- **THEN** sections Functional and Non-Functional appear, each with one column per feature, and a card linked to Availability and Security shows in both columns

#### Scenario: Cards show the other dimension
- **WHEN** the board is grouped by feature
- **THEN** each expanded card shows its status chip instead of its feature list

### Requirement: Feature group switcher
When grouped by feature, the board SHALL show a switcher with "All groups" and one entry per feature group (with its item count), so the user can show a single group instead of scrolling past the others. "+ sub-group" SHALL act on the selected group.

#### Scenario: Show one group
- **WHEN** the user picks Functional in the switcher
- **THEN** only the Functional feature columns are shown, and the choice is remembered with the other view settings

### Requirement: Sort and filter
The board SHALL sort the cards within each column by priority, feature, contributor, date modified, date created, status, title or id, and SHALL filter them by contributor. The chosen grouping, sort, filter and view SHALL be remembered per repository.

#### Scenario: Contributor board sorted by feature
- **WHEN** the board is grouped by contributor and sorted by feature
- **THEN** each contributor's column lists their work items ordered by their first feature name

#### Scenario: Filter by contributor
- **WHEN** the user filters by Tom
- **THEN** only work items whose contributors include Tom are shown, and column counts update

### Requirement: Board and list views
The work view SHALL switch between Board and List. The board SHALL offer a "compact cards" option that shows only the title band.

#### Scenario: Compact cards
- **WHEN** the user ticks "compact cards"
- **THEN** each card shows only its coloured title band

### Requirement: Subtasks as cards
The board SHALL offer "show: work items | subtasks". In subtask mode every tasks.md checkbox SHALL be its own card, placed in the column (and sub-category) of its work item, showing its parent `#wi` and title, with a tick box that writes to tasks.md.

#### Scenario: Tick from the board
- **WHEN** the board shows subtasks and the user ticks "Page-load budget test"
- **THEN** that line in #wi123's tasks.md becomes `- [x]` and the column counts subtasks, not work items

### Requirement: List table
The list view SHALL be a table with columns ID, Title, Status, Features, Contributors, Priority, Progress and Updated. Clicking a column header SHALL sort by it and clicking again SHALL reverse the order. Rows SHALL be grouped by the current grouping, with collapsible group headers showing a count and an "+ Add item" row.

#### Scenario: Sort by header
- **WHEN** the user clicks the Updated header twice
- **THEN** rows are sorted by date modified, first newest-first, then oldest-first, and the header shows the direction

### Requirement: Expand rows in the list
Each list row with subtasks SHALL have an expand control that shows its subtasks as indented child rows with tick boxes. "Expand all" and "Collapse all" SHALL act on every row. Status and priority SHALL be editable directly in their cells.

#### Scenario: Expand one row
- **WHEN** the user expands #wi123
- **THEN** its five subtasks appear under it as rows 123.1 to 123.5, and ticking one updates tasks.md

### Requirement: Custom sub-categories
Any column SHALL accept user-defined sub-categories (lanes) from its header menu, and SHALL allow removing them. Lane names per column SHALL be stored in `openspec/config.yaml` under `x-rtm.lanes`, and a work item's lane per column in its `x-rtm.lanes`. Items without a valid lane SHALL show in the column's first lane.

#### Scenario: Add a sub-category
- **WHEN** the user adds "Needs review" to the In Progress column
- **THEN** In Progress shows a "Needs review" lane and `x-rtm.lanes` in config.yaml lists it

#### Scenario: Drop into lane
- **WHEN** the user drops #wi105 into the "Needs review" lane of In Progress
- **THEN** its status becomes In Progress and its `x-rtm.lanes` maps In Progress to "Needs review"

### Requirement: Move by drag and drop
Dropping a card on another column SHALL set the dimension that column stands for: the status, or replace the source feature or contributor with the target one. Features derived from delta specs SHALL NOT be moved.

#### Scenario: Change status
- **WHEN** the user drags #wi106 from Planned to Confirmed
- **THEN** `x-rtm.status` in its `.openspec.yaml` becomes Confirmed and the card moves

#### Scenario: Locked feature
- **WHEN** the user drags a card out of a feature that comes from the change's delta specs
- **THEN** the card stays and the message "This feature comes from the change's specs/ folder" appears

### Requirement: Complete by archiving
Dropping a card on Completed SHALL ask for confirmation and then archive the change with the OpenSpec CLI. Cards SHALL NOT be dragged out of Completed.

#### Scenario: Archive
- **WHEN** the user drops #wi123 on Completed and confirms
- **THEN** the change folder moves to `openspec/changes/archive/<date>-<name>/`, its delta specs merge into `openspec/specs/`, and the card shows under Completed

#### Scenario: Archive fails
- **WHEN** the CLI reports an error, for example unfinished tasks or invalid deltas
- **THEN** the card returns to its column and the CLI's message is shown

### Requirement: Column header actions
Clicking a column header SHALL open a menu with: add a work item in this column, select all cards in the column, add a sub-category, and remove an existing sub-category.

#### Scenario: Add from header
- **WHEN** the user picks "Add work item here" on the Blocked column
- **THEN** a new change is created with status Blocked and its detail page opens

### Requirement: Create a sub-group
When grouped by feature, each feature group SHALL offer "+ sub-group", which creates a new capability `openspec/specs/<group>/<name>/spec.md` containing a Purpose placeholder.

#### Scenario: New feature
- **WHEN** the user adds sub-group "Exports" to Functional
- **THEN** `openspec/specs/functional/exports/spec.md` exists and an empty Exports column appears

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘-click or the card checkbox) and merge them. The merged work item SHALL combine the proposals, tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. Merging SHALL be refused when two sources change the same requirement.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105 and picks "Merge into one work item"
- **THEN** one new change holds both proposals' text and all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items
