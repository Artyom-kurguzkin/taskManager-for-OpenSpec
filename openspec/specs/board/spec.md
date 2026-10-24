# board Specification

## Purpose
The home screen of the app: shows the repository's work items grouped, sorted and filtered, and lets the user move, create and merge them directly on the board.

## Requirements

### Requirement: Group work items
The board SHALL group work items by a category: status (one column per status, in workflow order), bucket (features organized by their bucket, see below), label (one column per label value) or contributor (one column per person). Each column is a group, and a column MAY be divided into subgroups. A work item with several features or contributors SHALL appear in each matching column.

#### Scenario: Group by bucket
- **WHEN** the user picks "group by bucket"
- **THEN** one section per bucket appears (Functional, Non-Functional), each with its feature columns, and a card linked to Availability and Security shows in both Non-Functional columns

#### Scenario: Group by label
- **WHEN** the user picks "group by label"
- **THEN** one column per known label appears (bug, feature, enhancement), plus Unlabeled for items without a label

#### Scenario: Cards show the other dimension
- **WHEN** the board is grouped by bucket
- **THEN** each expanded card shows its status chip instead of its feature list

### Requirement: Unassigned columns
Work items with no feature (when grouped by bucket) or no contributor (when grouped by contributor) SHALL appear in a first column of their own, Unassigned or No contributors, shown only when it has items. Items without a label SHALL appear in an Unlabeled column when grouped by label.

#### Scenario: Unassigned
- **WHEN** a work item has no feature and the board is grouped by bucket
- **THEN** it shows in the Unassigned column at the front, and dragging a card there removes its manual feature

### Requirement: Sort and filter
The board SHALL sort the cards within each column by priority, feature, contributor, date modified, date created, status, title, id or the user's own order, and SHALL filter them by contributor. The chosen grouping, sort, filter and view SHALL be remembered per repository.

#### Scenario: Contributor board sorted by feature
- **WHEN** the board is grouped by contributor and sorted by feature
- **THEN** each contributor's column lists their work items ordered by their first feature name

#### Scenario: Filter by contributor
- **WHEN** the user filters by Tom
- **THEN** only work items whose contributors include Tom are shown, and column counts update

### Requirement: Board and list views
The side menu's layout dropdown SHALL switch between Board and List; it is one control, not two buttons. The view options (group by, sort by, contributor filter, work items or subtasks, compact cards, show hidden) SHALL be in the expanded side menu. "Compact cards" SHALL show only the title band. The collapsed side menu SHALL also have a compact-cards button, highlighted while compact cards are on and disabled outside the board view.

#### Scenario: Switch to list
- **WHEN** the user opens the layout dropdown in the side menu and picks List
- **THEN** the list table replaces the board and the dropdown reads "List"

#### Scenario: Compact cards
- **WHEN** the user ticks "compact cards"
- **THEN** each card shows only its coloured title band

#### Scenario: Compact from the collapsed menu
- **WHEN** the side menu is collapsed and the user clicks the compact-cards button on the board
- **THEN** the cards show only their title bands, and the button stays highlighted until clicked again

### Requirement: Subtasks as cards
The board SHALL offer "show: work items | subtasks". In subtask mode every tasks.md checkbox SHALL be its own card, placed in the column (and sub-category) of its work item, showing its parent `#wi` and title, with a tick box that writes to tasks.md.

#### Scenario: Tick from the board
- **WHEN** the board shows subtasks and the user ticks "Page-load budget test"
- **THEN** that line in #wi123's tasks.md becomes `- [x]` and the column counts subtasks, not work items

### Requirement: List table
The list view SHALL be a table with columns ID, Title, Status, Label, Features, Contributors, Priority, Progress and Updated. Clicking a column header SHALL sort by it and clicking again SHALL reverse the order. Rows SHALL be grouped by the current grouping, with collapsible group headers showing a count and an "+ Add item" row.

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

### Requirement: In Review status
The board SHALL include "In Review" as a status between Blocked and Completed in workflow order. Items in In Review can be moved back to any earlier status. Moving an item to Completed still triggers the archive flow.

#### Scenario: Move to In Review
- **WHEN** the user drops #wi106 on In Review
- **THEN** `x-rtm.status` becomes In Review and the card appears in the In Review column

### Requirement: Complete by archiving
Dropping a card on Completed SHALL ask for confirmation and then archive the change with the OpenSpec CLI. Cards SHALL NOT be dragged out of Completed.

#### Scenario: Archive
- **WHEN** the user drops #wi123 on Completed and confirms
- **THEN** the change folder moves to `openspec/changes/archive/<date>-<name>/`, its delta specs merge into `openspec/specs/`, and the card shows under Completed

#### Scenario: Archive fails
- **WHEN** the CLI reports an error, for example unfinished tasks or invalid deltas
- **THEN** the card returns to its column and the CLI's message is shown

### Requirement: Edit archived items
Archived (Completed) work items SHALL allow editing all metadata — label, priority, title, features, contributors, description, subtasks, linked commits and tests — except status, which stays locked at Completed. Archived items SHALL be mergeable with other archived items; the merged result stays in the archive.

#### Scenario: Change label on archived item
- **WHEN** the user picks "bug" in the label dropdown of a completed #wi124
- **THEN** `x-rtm.label` in its `.openspec.yaml` is updated to bug

#### Scenario: Merge archived items
- **WHEN** the user selects two completed items and merges them
- **THEN** a new work item is created in `openspec/changes/archive/`, combining their proposals and subtasks, and the source folders are removed

### Requirement: Column header actions
Clicking a column header SHALL open a menu with: add a work item in this column, select all cards in the column, add a subgroup, remove an existing subgroup, hide the column, and (for features created from the board) delete the feature.

#### Scenario: Add from header
- **WHEN** the user picks "Add work item here" on the Blocked column
- **THEN** a new change is created with status Blocked and its detail page opens

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘/Shift-click) and merge them. The merged work item SHALL combine the proposals, tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. Merging SHALL be refused when two sources change the same requirement.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105 and picks "Merge into one work item"
- **THEN** one new change holds both proposals' text and all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items

### Requirement: Bucket grouping
When grouped by bucket, the board SHALL show one section per bucket (the first folder segment of a feature path: Functional, Non-Functional, or Ungrouped), each section containing its feature columns. All buckets SHALL be visible at once; there is no side filter. Section headers SHALL display the bucket name.

#### Scenario: All buckets visible
- **WHEN** features exist under both Functional and Non-Functional
- **THEN** both sections appear with their feature columns, and each section has a heading

### Requirement: Reorder columns
The user SHALL reorder columns in any grouping by dragging a column header along its row: the column lands at the gap the other columns open, in front of the column after the gap, or at the end of the row. The order SHALL be remembered per repository and grouping in the user's settings file, not in the repository.

#### Scenario: Move Backlog to the end
- **WHEN** the user drags the Backlog header past the Completed column
- **THEN** the columns read Planned, Confirmed, In Progress, Blocked, In Review, Completed, Backlog, and stay so after a restart

### Requirement: Hide columns
"Hide column" in a column's header menu SHALL remove it from the board and the list. The expanded side menu SHALL list only the hidden columns of the current grouping, each with a control to show it again. Hidden columns SHALL be remembered like the column order.

#### Scenario: Hide and restore
- **WHEN** the user picks "Hide column" on Blocked, then clicks Show next to Blocked in the side menu
- **THEN** Blocked disappears, is listed under Hidden, and comes back

### Requirement: Create and delete features
When grouped by bucket, "+ feature" next to a bucket heading SHALL add a feature to that bucket, listed in `openspec/config.yaml` under `x-rtm.features` (OpenSpec only accepts a capability spec once it has requirements). Only features added this way SHALL offer "Delete feature…", which asks for confirmation, removes the entry from `x-rtm.features` and unlinks the feature from active work items, which then show as Unassigned. Features with a spec under `openspec/specs/` SHALL only be hideable.

#### Scenario: New feature
- **WHEN** the user adds feature "Exports" under the Functional bucket heading
- **THEN** `x-rtm.features` lists `functional/exports` and an empty Exports column appears in the Functional section

#### Scenario: Delete own feature
- **WHEN** the user deletes "Payments", which #wi21 links to, and confirms
- **THEN** the Payments column is gone, `functional/payments` is removed from config.yaml, and #wi21 shows under Unassigned

### Requirement: Hide selected work items
The selection action bar SHALL offer "Delete (hide)", enabled with one or more cards selected, which hides every selected item. This is the only way to hide an item; the work item page offers no hide control of its own. Hiding SHALL clear the selection.

#### Scenario: Hide several
- **WHEN** the user selects three cards and picks "Delete (hide)"
- **THEN** all three items gain `x-rtm.hidden: true`, disappear from the board unless "show hidden" is on, and the selection clears

### Requirement: Select cards by clicking
A click on a card SHALL open its work item. Shift-click (or Ctrl/⌘-click) SHALL add the card to the selection or remove it, without opening it. Cards SHALL have no selection tick box. Holding Shift SHALL show that select mode is on before any click. Releasing a drag SHALL not count as a click.

#### Scenario: Select with Shift
- **WHEN** the user Shift-clicks a card, then Shift-clicks another
- **THEN** both are outlined as selected and the action bar shows "2 selected"

#### Scenario: Plain click opens
- **WHEN** a card is selected and the user clicks it, or any other card, without Shift
- **THEN** that work item opens

#### Scenario: Holding Shift
- **WHEN** the user presses and holds Shift over the board
- **THEN** a bar says select mode is on, and cards under the pointer show they can be picked, before anything is clicked

### Requirement: Drop preview
A card or column header SHALL be dragged with the pointer: the real card or column follows it, the items it passes slide aside to close the hole it left and open a gap its size where it will land, and the drop lands in that gap. After the drop, and after an arrow-key move, cards and columns SHALL slide from their old place to their new one rather than jump. Escape SHALL cancel a drag, sliding everything back. Users who prefer reduced motion SHALL see the changes without animation.

#### Scenario: Gap between cards
- **WHEN** the user drags a card over the lower half of the second card in a column
- **THEN** the third and later cards slide down, opening a gap below the second, and the card settles into it on release

#### Scenario: Cancel
- **WHEN** the user presses Escape while dragging
- **THEN** the card and the cards that moved aside slide back to where they were

### Requirement: Move with the arrow keys
With cards selected, the up and down arrows SHALL move them one step inside their column, and the left and right arrows SHALL move them into the neighbouring column at the same height, changing its status (or feature, label, contributor) as a drop there would. The selection SHALL stay, so the next key moves it again.

#### Scenario: Step up
- **WHEN** the user selects the third Planned card and presses the up arrow
- **THEN** it becomes the second

#### Scenario: Into the next column
- **WHEN** a Planned card is selected and the user presses the right arrow
- **THEN** it moves to Confirmed at the same row, or last if Confirmed is shorter

### Requirement: Move a group with the arrow keys
Moving with the arrow keys SHALL require the selected cards to share a column; otherwise a message says so. A scattered selection SHALL first gather into a block, at its top card when moving up or its bottom card when moving down, and move as a block from then on.

#### Scenario: Gather, then move
- **WHEN** the first and third Planned cards are selected and the user presses the down arrow twice
- **THEN** after the first press they sit together as the second and third cards, and after the second press as the last two

#### Scenario: Different columns
- **WHEN** a Planned card and an In Progress card are selected and the user presses an arrow
- **THEN** nothing moves and a message asks for cards in one column

### Requirement: Reorder cards
A dragged card SHALL land at the gap: in front of the card after it, or at the end of the column, moving to that column first if it came from another one. The first drop that changes an order SHALL switch the sort to "my order"; dropping a card back where it was SHALL change nothing.

#### Scenario: Move a card to the top
- **WHEN** the user drags the last Planned card above the first
- **THEN** it is now first and the sort reads "my order"

### Requirement: Drag selected cards
Dragging a selected card SHALL move every selected card, landing together at the gap in their board order. Dragging a card that is not selected SHALL move only that card.

#### Scenario: Move two selected cards
- **WHEN** the user selects both Backlog cards and drags one in front of the first In Progress card
- **THEN** both become In Progress and sit above it, in their previous order

### Requirement: My order
In "my order", cards never placed SHALL follow in priority order. The order SHALL be remembered per repository in the user's settings file, not in the repository. Picking another sort SHALL ignore it but keep it.

#### Scenario: Remembered
- **WHEN** the user arranges the Planned cards and restarts the app
- **THEN** the cards keep that order

#### Scenario: Back to another sort
- **WHEN** the user picks sort by id, then "my order" again
- **THEN** cards show by id, then in the order the user arranged

### Requirement: Archiving shows progress
While the OpenSpec CLI archives a work item, a panel with a spinner SHALL say "Archiving #wi<id> with the OpenSpec CLI…" and block other input until it finishes. The CLI SHALL run from npx's cached copy when one exists at the pinned version, skipping npx's package resolution.

#### Scenario: Archive from the confirm dialog
- **WHEN** the user picks Completed and clicks Archive
- **THEN** the busy panel appears at once, and closes when the item shows as Completed
