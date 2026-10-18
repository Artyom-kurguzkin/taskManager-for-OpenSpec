# board Specification

## Purpose
The home screen of the app: shows the repository's work items grouped, sorted and filtered, and lets the user move, create and merge them directly on the board.

## Requirements

### Requirement: Group work items
The board SHALL group work items by a category: status (one column per status, in workflow order), contributor (one column per person) or feature (one column per feature). Each column is a group, and a column MAY be divided into subgroups. A work item with several features or contributors SHALL appear in each matching column. Work items with no feature (when grouped by feature) or no contributor (when grouped by contributor) SHALL appear in a first column of their own, Unassigned or No contributors, shown only when it has items.

#### Scenario: Group by feature
- **WHEN** the user picks "group by feature"
- **THEN** one row of feature columns appears, with no headings over them, and a card linked to Availability and Security shows in both columns

#### Scenario: Unassigned
- **WHEN** a work item has no feature and the board is grouped by feature
- **THEN** it shows in the Unassigned column at the front, and dragging a card there removes its manual feature

#### Scenario: Cards show the other dimension
- **WHEN** the board is grouped by feature
- **THEN** each expanded card shows its status chip instead of its feature list

### Requirement: Sort and filter
The board SHALL sort the cards within each column by priority, feature, contributor, date modified, date created, status, title or id, and SHALL filter them by contributor. The chosen grouping, sort, filter and view SHALL be remembered per repository.

#### Scenario: Contributor board sorted by feature
- **WHEN** the board is grouped by contributor and sorted by feature
- **THEN** each contributor's column lists their work items ordered by their first feature name

#### Scenario: Filter by contributor
- **WHEN** the user filters by Tom
- **THEN** only work items whose contributors include Tom are shown, and column counts update

### Requirement: Board and list views
The side menu's layout dropdown SHALL switch between Board and List; it is one control, not two buttons. The view options (group by, sort by, contributor filter, work items or subtasks, compact cards, show hidden) SHALL be in the expanded side menu. "Compact cards" SHALL show only the title band.

#### Scenario: Switch to list
- **WHEN** the user opens the layout dropdown in the side menu and picks List
- **THEN** the list table replaces the board and the dropdown reads "List"

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
Each list row with subtasks SHALL have an expand control that shows its subtasks as indented child rows with tick boxes. A chevron in the table header SHALL expand or collapse every row. Status and priority SHALL be editable directly in their cells. Cells other than the title SHALL stay on one line.

#### Scenario: Expand one row
- **WHEN** the user expands #wi123
- **THEN** its five subtasks appear under it as rows 123.1 to 123.5, and ticking one updates tasks.md

#### Scenario: Expand all
- **WHEN** the user clicks the chevron in the table header
- **THEN** every row with subtasks shows them

### Requirement: Custom sub-categories
Any column SHALL accept user-defined subgroups (lanes) from its header menu ("+ Add subgroup…"), and SHALL allow removing them. Subgroup names per column SHALL be stored in `openspec/config.yaml` under `x-rtm.lanes`, and a work item's subgroup per column in its `x-rtm.lanes`. Items without a valid subgroup SHALL show in the column's first subgroup.

#### Scenario: Add a sub-category
- **WHEN** the user adds "Needs review" to the In Progress column
- **THEN** In Progress shows a "Needs review" subgroup and `x-rtm.lanes` in config.yaml lists it

#### Scenario: Drop into lane
- **WHEN** the user drops #wi105 into the "Needs review" subgroup of In Progress
- **THEN** its status becomes In Progress and its `x-rtm.lanes` maps In Progress to "Needs review"

### Requirement: Move by drag and drop
Dropping a card on another column SHALL set the dimension that column stands for: the status, or replace the source feature or assigned contributor with the target one. Features derived from delta specs SHALL NOT be moved. Commit authors stay contributors wherever the card is dropped.

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
Clicking a column header SHALL open a menu with: add a work item in this column, select all cards in the column, add a subgroup, remove an existing subgroup, hide the column, and (for features created from the board) delete the feature.

#### Scenario: Add from header
- **WHEN** the user picks "Add work item here" on the Blocked column
- **THEN** a new change is created with status Blocked and its detail page opens

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘-click or the card checkbox) and merge them. The merged work item SHALL combine the proposals, tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. Merging SHALL be refused when two sources change the same requirement.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105 and picks "Merge into one work item"
- **THEN** one new change holds both proposals' text and all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items

### Requirement: Feature side filter
Features under `openspec/specs/functional/` and `openspec/specs/non-functional/` (and Ungrouped, for features with no such folder) SHALL be a filter, not a level: when grouped by feature, a dropdown SHALL choose which side's features are shown, one side at a time, named sides first. The dropdown SHALL be hidden when only one side exists. The choice SHALL be remembered with the view settings.

#### Scenario: Switch side
- **WHEN** the board shows Functional features and the user picks Non-Functional in the dropdown
- **THEN** only Availability and Security columns (plus Unassigned) are shown

### Requirement: Reorder columns
The user SHALL reorder columns in any grouping by dragging a column header onto another column: onto a later column places it after, onto an earlier one before. The order SHALL be remembered per repository and grouping in the user's settings file, not in the repository.

#### Scenario: Move Backlog to the end
- **WHEN** the user drags the Backlog header onto the Completed column
- **THEN** the columns read Planned, Confirmed, In Progress, Blocked, Completed, Backlog, and stay so after a restart

### Requirement: Hide columns
"Hide column" in a column's header menu SHALL remove it from the board and the list. The expanded side menu SHALL list only the hidden columns of the current grouping, each with a control to show it again. Hidden columns SHALL be remembered like the column order.

#### Scenario: Hide and restore
- **WHEN** the user picks "Hide column" on Blocked, then clicks Show next to Blocked in the side menu
- **THEN** Blocked disappears, is listed under Hidden, and comes back

### Requirement: Create and delete features
When grouped by feature, "+ feature" SHALL add a feature to the side being shown, listed in `openspec/config.yaml` under `x-rtm.features` (OpenSpec only accepts a capability spec once it has requirements). Only features added this way SHALL offer "Delete feature…", which asks for confirmation, removes the entry from `x-rtm.features` and unlinks the feature from active work items, which then show as Unassigned. Features with a spec under `openspec/specs/` SHALL only be hideable.

#### Scenario: New feature
- **WHEN** the user adds feature "Exports" while Functional is shown
- **THEN** `x-rtm.features` lists `functional/exports` and an empty Exports column appears

#### Scenario: Delete own feature
- **WHEN** the user deletes "Payments", which #wi21 links to, and confirms
- **THEN** the Payments column is gone, `functional/payments` is removed from config.yaml, and #wi21 shows under Unassigned
