# board Specification

## Purpose
The home screen of the app: shows the repository's work items grouped, sorted and filtered, and lets the user move, create and merge them directly on the board.

## Requirements

### Requirement: Unassigned columns
Work items with no feature (when grouped by feature) or no contributor (when grouped by contributor) SHALL appear in a first column of their own, Unassigned or No contributors, shown only when it has items. Items without a label SHALL appear in an Unlabeled column when grouped by label.

#### Scenario: Unassigned
- **WHEN** a work item has no feature and the board is grouped by feature
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

### Requirement: Subtasks as cards
The board SHALL offer "show: work items | subtasks". In subtask mode every tasks.md checkbox SHALL be its own card, placed in the column (and sub-category) of its work item, showing its parent `#wi` and title, with a tick box that writes to tasks.md.

#### Scenario: Tick from the board
- **WHEN** the board shows subtasks and the user ticks "Page-load budget test"
- **THEN** that line in #wi123's tasks.md becomes `- [x]` and the column counts subtasks, not work items

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

### Requirement: Expand rows in the list
Each list row with subtasks SHALL have an expand control that shows its subtasks as indented child rows with tick boxes; rows start folded. The subtasks button, selected, SHALL show every row's subtasks and keep the work items and groups as they are; unselected, only rows opened by hand show theirs. There SHALL be no other control for this. Status and priority SHALL be editable directly in their cells. Cells other than the title SHALL stay on one line.

#### Scenario: Expand one row
- **WHEN** the user expands #wi123
- **THEN** its five subtasks appear under it as rows 123.1 to 123.5, and ticking one updates tasks.md

#### Scenario: Expand all
- **WHEN** the subtasks button is selected, then unselected
- **THEN** first every row with subtasks shows them, then every work item stays but no subtask shows

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
Dropping a card on Completed SHALL ask for confirmation and then archive the change with the OpenSpec CLI. Before the CLI runs, the app SHALL refresh the change's delta specs: every MODIFIED requirement SHALL get, appended verbatim from the current main spec, the scenarios it lacks, so a requirement that another archive changed since the delta was written never makes the archive fail. Cards SHALL NOT be dragged out of Completed.

#### Scenario: Archive
- **WHEN** the user drops #wi123 on Completed and confirms
- **THEN** the change folder moves to `openspec/changes/archive/<date>-<name>/`, its delta specs merge into `openspec/specs/`, and the card shows under Completed

#### Scenario: Stale modified requirement
- **WHEN** #wi107 modifies "Send money" with one scenario and the main spec has since gained "Fee shown" and "Limit"
- **THEN** both are appended to #wi107's block before the archive, the archive succeeds and the main spec keeps all three with #wi107's text

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
Clicking a column header SHALL open a menu with: add a work item in this column, select all cards in the column, hide the column, and (for features created from the board) delete the feature.

#### Scenario: Add from header
- **WHEN** the user picks "Add work item here" on the Blocked column
- **THEN** a new change is created with status Blocked and its detail page opens

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘/Shift-click) and merge them. Merge SHALL ask which selected item is the main one ("Merge into…"): the merged work item keeps that item's title and description, and every other source's description becomes a note "Merged from #wi<N> <title>" on it. It SHALL combine the tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. No information SHALL be lost: it SHALL keep the sources' notes and other files, the label, linked commits and tests, lanes and dependencies, the task groups under headings naming their source, and the numbers they came from. Items that waited on a source SHALL wait on the merged item. Merging SHALL be refused when two active sources change the same requirement. Archived items SHALL always be mergeable, since their specs are already merged into `openspec/specs`: with only archived sources the result is archived and keeps all their specs; with an active source the result is active and keeps the archived specs under `from-wi<id>/specs/`, not in its own `specs/`, so archiving it cannot apply them twice.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105, picks Merge and chooses #wi105 as the main one
- **THEN** one new change has #wi105's title and description, a note "Merged from #wi107" with #wi107's description, all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items

#### Scenario: Notes and links survive
- **WHEN** #wi107 has a note, a linked commit and a linked test and is merged with #wi123
- **THEN** the merged item shows that note in its change log, lists that commit and test, and lists commits that mention #wi107

#### Scenario: Archived items that changed the same requirement
- **WHEN** two completed items both changed requirement "Keyboard hints" and the user merges them
- **THEN** the merge succeeds, the result is archived, and both texts of the requirement are kept in its spec

#### Scenario: Archived item merged into an active one
- **WHEN** a completed item that changed "Session timeout" is merged with an active item
- **THEN** the result is active, its own specs/ folder does not contain that change, and the text is kept in `from-wi<id>/specs/`

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
When grouped by feature, "+ feature" at the end of the row of columns SHALL add a feature, listed in `openspec/config.yaml` under `x-rtm.features` (OpenSpec only accepts a capability spec once it has requirements). Every feature column SHALL offer "Delete feature…", which asks for confirmation and unlinks the feature from active work items, which then show as Unassigned. A feature listed in `x-rtm.features` is removed from there; a feature with a spec under `openspec/specs/` is deleted with its spec folder. When open changes still have a delta spec for the feature, the confirmation SHALL name them and their delta folders for it SHALL be deleted too, leaving each change valid; archived changes SHALL NOT be touched. Deleting SHALL be refused when the feature's folder holds other features.

#### Scenario: New feature
- **WHEN** the user adds feature "Exports" with "+ feature"
- **THEN** `x-rtm.features` lists `exports` and an empty Exports column appears at the end of the row

#### Scenario: Delete own feature
- **WHEN** the user deletes "Payments", which #wi21 links to, and confirms
- **THEN** the Payments column is gone, `payments` is removed from config.yaml, and #wi21 shows under Unassigned

#### Scenario: Delete a spec-backed feature
- **WHEN** the user deletes "Test links", which has the folder `openspec/specs/test-links`, and confirms
- **THEN** the dialog said the folder goes, the folder is deleted and the column leaves the board

#### Scenario: An open change still has a spec for it
- **WHEN** the user deletes a feature for which #wi123 has a delta spec, and confirms
- **THEN** the dialog said "#wi123 still describes it in its specs", that delta folder is deleted with the feature, and #wi123 still validates

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

### Requirement: One vocabulary on every screen
A feature SHALL be a dashed chip wherever it is shown (a card, a list row, the item page); a status, label or priority SHALL be a pill filled with its colour wherever it can be set (a list row, the item page); and every control that adds something ("+ Add item", "+ feature", "+ link commit", "+ Add entry") SHALL be the same quiet text button.

#### Scenario: Feature chips
- **WHEN** a work item with the feature "Board" is shown on the board and in the list
- **THEN** both show "Board" as a dashed chip

#### Scenario: Pills
- **WHEN** a work item's status is shown in the list and on its page
- **THEN** both show it as a pill filled with the status colour, and clicking either opens the same list of statuses, navigable with the arrow keys

### Requirement: Card contents
An expanded card SHALL show its title band, its feature as a chip (its status instead when grouped by feature, since the column is the feature), a plain meta row `<date> · <contributors> · <done>/<total>` without icons (the count only when the item has subtasks), and its tags: `#wi`, the label filled with the label's colour, the due date, and warnings. It SHALL NOT show a priority tag: priority is a subgroup of the column.

#### Scenario: Features on cards
- **WHEN** the board is sorted by feature and a card is linked to Board
- **THEN** its expanded form shows a "Board" chip

#### Scenario: Meta row and label
- **WHEN** a card's work item has 2 of 4 subtasks done, was updated on 5 Nov by Artyom and is labelled feature
- **THEN** the meta row reads `5 Nov · Artyom · 2/4` with no icons, and the label tag is filled sky blue

#### Scenario: No subtask count
- **WHEN** a card's work item has no subtasks
- **THEN** the meta row ends with the contributors and shows no count

### Requirement: Hidden column counts
Each hidden column listed in the expanded side menu SHALL show how many items it holds (subtasks when the board shows subtasks), under the current contributor filter and "show hidden" setting, so the user can tell whether a hidden column is worth bringing back.

#### Scenario: Count of a hidden column
- **WHEN** Blocked, which holds one work item, is hidden
- **THEN** the side menu lists Blocked with the number 1 before its show control

### Requirement: Features in a list row
In the list view, a work item's features SHALL stay on one line in the Features cell: the first as a dashed chip (as on a card) and the rest as a "+N" count, with every feature named in the cell's tooltip, so a row keeps its height and the column its width.

#### Scenario: Three features
- **WHEN** a work item is linked to App shell, Board and Work item editor
- **THEN** its Features cell shows "App shell" as a dashed chip and "+2", and its tooltip names all three

### Requirement: Move rows in the list
Each list row SHALL have a drag grip. Dragging it in front of another row of its group SHALL place it there and switch the sort to "my order"; dragging it onto another group SHALL move it there as a drop on that board column would. A selected row SHALL carry every selected row. While dragging, the rows passed SHALL slide aside, and after the drop the rows SHALL slide to their new places.

#### Scenario: Reorder
- **WHEN** the user drags the third Planned row's grip in front of the first
- **THEN** it becomes the first Planned row and the sort reads "my order"

#### Scenario: Change group
- **WHEN** the user drags a Planned row below the last In Progress row
- **THEN** its status becomes In Progress and it is the last row of that group

### Requirement: Move rows with the keyboard
With rows or cards selected (Shift-click), the arrow keys SHALL move them, whatever has the keyboard focus, as long as the user is not typing in a field. They SHALL step inside the cell the selection is in: a group's rows in one subgroup in the list (the whole group without subgroups), a column's cards in one row on the board (the whole column without rows); the neighbours are the items of that cell only. Up and Down SHALL swap the selection with its neighbour one place per press, and the sort order SHALL become my order, as a drag does. In the list, when the selection sits together at the cell's edge in the key's direction, Up and Down SHALL carry it into the neighbouring subgroup that has rows (the end of it going up, the start going down) and set the subgroup field; past the group's last (first) subgroup into the neighbouring group (setting the group field), entering its nearest subgroup and setting that field too, or, when that group has no rows, keeping the selection's own subgroup value; with nothing beyond, nothing moves. On the board Up and Down at the edge of a cell SHALL carry the cards into the neighbouring row's cell in the same column (empty or not), and stop at the first and last row and column end. Left and Right on the board SHALL carry the cards into the neighbouring column's cell in the same row at the same height. The selection and the keyboard focus SHALL stay on what moved, and the moved row or card SHALL be scrolled into view, clear of the list's sticky header, the board's sticky column header and the selection bar.

#### Scenario: Arrow after selecting a row
- **WHEN** the user Shift-clicks a row and presses the up arrow
- **THEN** the row moves up one place and the page does not scroll

#### Scenario: Arrow inside a subgroup
- **WHEN** the list is grouped by status with subgroups by priority and the user Shift-clicks the second row of a Lower priority subgroup and presses Up
- **THEN** it is the first row of that subgroup, with its status and priority unchanged

#### Scenario: Arrow at the top of a subgroup
- **WHEN** the user presses Up again
- **THEN** its priority becomes high and it is the last row of the High priority subgroup of the same group

#### Scenario: Arrow at the top of a group
- **WHEN** the selected row is the first of the group's first subgroup and the user presses Up
- **THEN** its status becomes that of the group above and it is the last row of that group's last subgroup that has rows

#### Scenario: Arrow down comes back
- **WHEN** the row is the last of its group's last subgroup and the user presses Down
- **THEN** it becomes the first row of the first subgroup that has rows in the group below

#### Scenario: No group beyond
- **WHEN** the user selects the first row of the first group and presses Up
- **THEN** nothing changes

#### Scenario: Several rows at the edge
- **WHEN** the user selects the last two rows of a group without subgroups and presses Down
- **THEN** both move to the start of the next group, in the order they had

#### Scenario: Board card with rows
- **WHEN** the board has rows by priority and the user selects the second card of a lower-priority cell and presses Up
- **THEN** it is the first card of that cell and still selected; one more press moves it to the end of the same column's cell in the high-priority row, and another to the top of that cell; at the top of the first row it stays

#### Scenario: Selection follows
- **WHEN** a move takes the selected card or row out of the visible part of the scrolling area
- **THEN** the area scrolls just enough to show it again

### Requirement: Select cells in the list
Dragging across the cells of work item rows, from any cell, SHALL select the rectangle between the first and the last cell. Esc, or a click outside the table, SHALL clear it. Ctrl+C SHALL copy the rectangle as tab-separated text, one line per row. The table's text SHALL NOT be selectable as plain text.

#### Scenario: A still click is the cell's own
- **WHEN** the user presses on a status cell and releases without moving
- **THEN** the status menu opens and nothing is selected; a press that moves to another cell selects cells and opens nothing

#### Scenario: Shift-click selects the row
- **WHEN** the user Shift-clicks (or Ctrl/⌘-clicks) a cell
- **THEN** the row is selected as a card is on the board, and no cells are

#### Scenario: Copy a column of ids
- **WHEN** the user drags from the ID cell of the first Backlog row to the ID cell of the fourth and presses Ctrl+C
- **THEN** the clipboard holds the four ids, one per line, and nothing else

### Requirement: Groups and subtasks in the list
Each group header in the list, and each project heading when several projects are open, SHALL have a chevron that collapses it to its heading and opens it again; a project shares the board's collapsed setting, and a collapsed group stays so through reloads, in its own project only. Subtasks SHALL start folded; the rail's subtasks button, selected, SHALL show every row's subtasks, and unselected fold them. It SHALL NOT collapse groups; there SHALL be no "collapse all" control.

#### Scenario: Collapse one group
- **WHEN** the user clicks the chevron of the Planned group
- **THEN** Planned collapses to its heading; the other groups stay as they were

#### Scenario: Collapse a project
- **WHEN** two projects are open and the user collapses Backlog in the first, then folds and unfolds that project
- **THEN** the project folds to its heading and back, its Backlog is still collapsed, and the second project's Backlog never was

#### Scenario: Subtasks follow the subtasks button
- **WHEN** the list shows no subtasks (the default) and the user selects the subtasks button
- **THEN** every row's subtasks show while every work item row and group stays; unselected again, they fold away

### Requirement: List layout
The list SHALL be laid out like the list views of task managers (Asana, Linear, ClickUp): no frame, one-line rows of about 40px, a sticky header whose controls stay hidden until hovered, a drag grip that appears on hover, compact group headers set apart by space, a faint "+ Add item" row under each group, cells in their colours, and a pointer that stays an arrow until a drag is under way.

#### Scenario: Colour
- **WHEN** a row shows its status, label and priority
- **THEN** each is a pill filled with its colour (the same as on the work item page), and a status group's name in its header is filled with the status band

#### Scenario: Resize a column
- **WHEN** the user hovers a column boundary in the header
- **THEN** a line marks the boundary and the header shows nothing else; dragging it draws the line down the whole table until release, and the column takes the new width

#### Scenario: Pointer
- **WHEN** the user hovers a header, a row's grip or a board column's header
- **THEN** the pointer stays an arrow; it turns to a grabbing hand only once a drag is under way

#### Scenario: One-line rows
- **WHEN** a title is longer than its column
- **THEN** it is cut with an ellipsis and shown whole in a tooltip; the status, label and priority are pills in their colours; features show the first by name and the rest as a count, all named in a tooltip

#### Scenario: Quiet header
- **WHEN** the user hovers the Title header
- **THEN** its sort mark, funnel and hide × appear; the other headers show only their name, except the sort mark on a sorted column

#### Scenario: Row controls on hover
- **WHEN** the user hovers a row
- **THEN** its drag grip appears and the row is tinted; other rows show neither. There are no tick boxes: Shift-click selects a row as it selects a card

### Requirement: Grouping is not repeated
Whatever the board or list is grouped by SHALL NOT be shown again inside a group, except the list's Status column: the list SHALL hide Label when grouped by label, Feature by feature and Contributors by contributor, and SHALL always show Status, whatever the grouping (and with subgroups), since the organising operators work on it, unless the user hides that column themselves from its header. A grid layout saved while the column was dropped SHALL NOT keep it hidden. A card SHALL leave out its label when grouped by label and its contributors when grouped by contributor.

#### Scenario: List grouped by status
- **WHEN** the list is grouped by status
- **THEN** it still has a Status column; grouped by label it has a Status column and no Label column

#### Scenario: Status column after a saved layout
- **WHEN** the list was last used grouped by status and its saved layout has Status hidden, and the user opens it with groups none
- **THEN** the Status column shows

#### Scenario: The user hides it
- **WHEN** the user clicks the × in the Status header
- **THEN** the column is hidden and listed under Hidden in the side menu

#### Scenario: Card grouped by label
- **WHEN** the board is grouped by label
- **THEN** a card shows no label tag; grouped by status it shows it

### Requirement: Group by category
The board SHALL group work items by a category: status (one column per status, in workflow order), feature (one flat row of feature columns, with no supergroup headings), label (one column per label value) or contributor (one column per person). Each column is a group, and a column MAY be divided into subgroups. A work item SHALL appear in its one feature's column, and in each of its contributors' columns.

#### Scenario: Group by feature
- **WHEN** the user picks "group by feature"
- **THEN** one column per feature appears in a single row with no heading above it, and each card shows in exactly one of them (or Unassigned)

#### Scenario: Group by label
- **WHEN** the user picks "group by label"
- **THEN** one column per known label appears (bug, feature, enhancement), plus Unlabeled for items without a label

#### Scenario: Cards show the other dimension
- **WHEN** the board is grouped by feature
- **THEN** each expanded card shows its status chip instead of its feature

### Requirement: Any item moves between features
Grouped by feature, any card, open or completed, SHALL drag to another feature column or to Unassigned, which becomes its feature. Features that come from the change's own specs/ folder are unlinked (`x-rtm.unlinkedFeatures`, the spec stays) and relinked when the card is dragged back.

#### Scenario: Move a completed item
- **WHEN** the user drags completed #wi124, whose archived spec is for Security, from Security to Availability
- **THEN** it shows only under Availability, `unlinkedFeatures` lists Security, and dragging it back links Security again

#### Scenario: Move an open item off its spec feature
- **WHEN** the user drags open #wi123, whose own spec is for Availability, to Banking
- **THEN** it shows under Banking, `unlinkedFeatures` lists Availability and its spec file is unchanged

### Requirement: One feature per work item
A work item SHALL have at most one feature. A feature picked by hand (`x-rtm.features`) SHALL win over the change's specs/ folder; otherwise the item's feature is the first capability its specs/ folder has a delta for that is not unlinked. The work item page SHALL show it in one "feature" dropdown listing every feature and "no feature"; picking one SHALL make it the item's feature the same way a drag does. The list SHALL show it in a Feature column.

#### Scenario: Picked by hand wins
- **WHEN** #wi123 has a spec for Availability and Security picked by hand
- **THEN** its feature is Security, and it shows in the Security column only

#### Scenario: Pick on the item page
- **WHEN** the user picks Banking, then "no feature", in #wi123's feature dropdown
- **THEN** its feature is Banking, then none (Unassigned), and its Availability spec file is unchanged

### Requirement: Rename features
"Rename feature…" in a feature column's menu SHALL ask for the new name, starting from the current one, and rename the feature's folder: its spec folder under `openspec/specs/` (or its `x-rtm.features` entry), every change's delta folder for it, archived ones included, and the links in each work item's `x-rtm`. The column SHALL keep its place and hidden state. A name already taken SHALL be refused, and so SHALL a feature whose folder holds other features.

#### Scenario: Rename a spec feature
- **WHEN** the user renames Security to "Safety"
- **THEN** `openspec/specs/non-functional/security` becomes `.../safety`, archived #wi124's delta folder moves with it, and #wi123's manual link reads `non-functional/safety`

#### Scenario: Name taken
- **WHEN** the user renames a feature to the name of another one
- **THEN** nothing changes and a message says it exists

### Requirement: Several projects on one board
With several repositories open as tabs, the board SHALL stack a block per project, named after its folder, with a chevron that collapses it and a menu that hides it. A lone visible project SHALL show no project header. When every project has the same columns, one header row above SHALL serve them all; otherwise each block keeps its own column headers.

#### Scenario: Two projects, status grouping
- **WHEN** two repositories are open and the board is grouped by status
- **THEN** one row of the seven status headers sits above two named blocks of cards

#### Scenario: Collapse and hide
- **WHEN** the user clicks a project's chevron, then chooses Hide project from its menu
- **THEN** the block folds to its heading, then disappears; the side menu lists it under Hidden and its eye brings it back

### Requirement: Projects in the list
With several repositories open, the list SHALL group rows by project first, then by column.

#### Scenario: Two projects in the list
- **WHEN** two repositories are open and the list is shown
- **THEN** a group row per project heads its column groups, and a work item number present in both appears once under each

### Requirement: Cards stay in their project
A card SHALL move only within its own project: a drop into another project's column SHALL leave it where it was and say so. Selection and the arrow keys SHALL act within the project of the selected cards. Opening a card of another project SHALL make that project the active repository, then open the card.

#### Scenario: Cross-project drop
- **WHEN** a card of the first project is dropped into a column of the second
- **THEN** nothing changes in either repository and a message says the work item stays in its repository

#### Scenario: Open from another project
- **WHEN** the user clicks a card in the second project's block
- **THEN** the second repository becomes the active one and the card's page opens

### Requirement: Reorder projects
A project SHALL be selected by Shift-clicking (or Ctrl/⌘-clicking) anywhere in its heading row on the board, or its group row in the list (except its buttons), shown as a tinted row and never selecting text, and the up and down arrows SHALL move the selected projects past the next unselected one, as selected cards move. The order SHALL be the tabs' order and SHALL be remembered.

#### Scenario: Move a project up
- **WHEN** the user Shift-clicks the second project's heading and presses the up arrow
- **THEN** it becomes the first project on the board and in the list, and stays first after a restart

### Requirement: Remove a project
The repository menu SHALL end with "Remove a project…" while more than one project is open. It SHALL ask which project to remove and close that project's tab; nothing on disk SHALL change, and the last project SHALL not be removable. It SHALL be the only way to remove a project: the project rows in the menu SHALL have no close button.

#### Scenario: Remove one of two
- **WHEN** the user chooses Remove a project… and picks demo2
- **THEN** demo2 leaves the board and the tabs, its folder stays, and the menu no longer offers Remove a project…

#### Scenario: No close button on a project row
- **WHEN** the user opens the repository menu with two projects open
- **THEN** each project row shows its name and its eye, and no × button

### Requirement: New work item picks its project
With several projects open, creating a work item from the side menu or from the shared header row SHALL first ask which project it goes to; the item SHALL be created there and that project SHALL become active. With one project open it SHALL be created at once, without asking. A column's own "+ Add work item here" SHALL use that column's project.

#### Scenario: Two projects
- **WHEN** two projects are open and the user presses New work item and picks demo2
- **THEN** the item is created in demo2, demo2 becomes the active project, and the item's page opens

### Requirement: Sticky column headers
The board's column headers SHALL stay at the top while the board scrolls: the shared header row with several projects, or each column's own header otherwise.

#### Scenario: Scroll down
- **WHEN** the user scrolls the board down
- **THEN** the column names stay visible at the top

### Requirement: Project selection lets go
Selecting a project SHALL let go of selected cards, and selecting a card SHALL let go of selected projects; a plain click on the board or list SHALL let go of selected projects.

#### Scenario: Move on to a card
- **WHEN** a project is selected and the user Shift-clicks a card
- **THEN** only the card is selected

### Requirement: One selection look
Anything selected with Shift-click — a card, a list row, a project heading, a subtask — SHALL look the same: it keeps its own surface, a solid line in the text colour hugs it and a yellow halo runs round that line, wide enough to be seen at a glance on the light theme. Selection SHALL never be shown as a fill.

#### Scenario: Card and row
- **WHEN** the user Shift-clicks a card on the board and a row in the list
- **THEN** both show the same solid line with a yellow halo round it, and neither changes its fill

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

### Requirement: List cells stay in their column
In the list view every cell SHALL keep its content inside its own column, however narrow the column is. Text that does not fit SHALL be cut with an ellipsis and SHALL show in full as a tooltip; pills and chips SHALL shrink to fit, and a progress bar SHALL shrink so its count stays visible. The Label column SHALL be wide enough by default for every label name.

#### Scenario: Long title in a narrow column
- **WHEN** an item has a long title and long contributor names and the user narrows the columns
- **THEN** each cell ends in an ellipsis inside its own column, nothing spills into the next column, and hovering shows the full text

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
An expanded card SHALL show its planned dates as one period pill built from the item's start and due days, and SHALL show neither the work item's id nor an agent badge. The period always says what it is: "9–16 Nov" inside one month, "28 Oct – 3 Nov" across months, "9 Nov 25 – 3 Jan 26" across years, "from 9 Nov" with only a start, "due 16 Nov" with only a due day, and "due 16 Nov" for a one-day period (start and due on the same day), never a bare "16 Nov". Its tooltip SHALL be the long form ("Started 9 November 2026 – Due 16 November 2026"). The pill SHALL be in the overdue colour (peach, the colour the board gives an overdue item by due date) while the item has a due day that is past and is not completed; a compact card and an item with neither day show no pill. The card's first line SHALL read the time of the item's last change as "updated 15:34. 16 Nov 26" (a muted caption "updated", then time, dot, day, month, two-digit year) with the tooltip "Last changed 15:34, 16 Nov 2026". The card's subtask count SHALL read "11/12 subtasks" (the word muted) with the tooltip "11 of 12 subtasks done". These captions add no frame and no second meta row. The list SHALL have Updated, Started and Due as three separate columns, present by default and in that order, beside "In status since": Updated is when the work item was last changed and reads "15:34. 16 Nov 26" like the card; Started is its planned start day and Due its planned end day, each as "9 Nov". The columns are named by their headers, so the list carries no captions. The list row SHALL carry no agent badge.

#### Scenario: One period pill
- **WHEN** an item started on 9 Nov and is due on 16 Nov
- **THEN** its expanded card's pill reads "9–16 Nov"; started 28 Oct and due 3 Nov reads "28 Oct – 3 Nov"; started 9 Nov 2025 and due 3 Jan 2026 reads "9 Nov 25 – 3 Jan 26"; only a start reads "from 9 Nov"; only a due day reads "due 16 Nov"; started and due on 11 Nov reads "due 11 Nov"

#### Scenario: Overdue card
- **WHEN** #wi123 was due yesterday
- **THEN** its period pill is peach; an item due today or in two days has a plain pill; an item that is only started has a plain pill; an item without dates has none

#### Scenario: No id and no agent badge
- **WHEN** an agent changed #wi123 three minutes ago and the user has not reviewed it
- **THEN** its card shows no "#wi123" pill and no "Claude, 3 min ago" line, and neither does its list row

#### Scenario: Modified with its time
- **WHEN** the newest change of #wi123 was at 15:34 on 16 Nov 2026
- **THEN** the card's first line reads "updated 15:34. 16 Nov 26" and the list's Updated cell reads "15:34. 16 Nov 26"

#### Scenario: Numbers say what they are
- **WHEN** an expanded card has 11 of 12 subtasks done
- **THEN** its meta row reads "11/12 subtasks" with the tooltip "11 of 12 subtasks done", in the same single meta row as the updated stamp and the people

#### Scenario: Three dates in the list
- **WHEN** the user opens the list on a fresh repository
- **THEN** the headers read Updated, Started and Due, in that order, each its own column

### Requirement: Show or hide a project from the project switcher
With several projects open, each project in the side menu's project switcher SHALL have an eye that hides it from the board, list and calendar, or shows it again, without switching to it and without closing the menu (the same setting as "Hide project" in the project's menu, so a hidden one is also listed under Hidden). There SHALL be no second list of projects in the view options.

#### Scenario: Hide from the switcher
- **WHEN** two projects are open and the user opens the switcher and clicks the eye next to the second
- **THEN** the board shows only the first, the first stays the active project, the eye shows crossed out, and clicking it again brings the second back

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
Every card band, list row and roadmap bar SHALL take its colour from the colour field: status, label and priority by their own colours, due and start dates by period, feature and contributor by a stable colour per value, none in a neutral colour. In the list the colour SHALL tint the whole row (a light wash of the colour over every cell, the text keeping the theme's colour so it reads in the light and the dark theme), not a strip at its edge; the ring of a selected row and the mark of the open row SHALL stay visible over it, and with colour none the rows are not tinted. A legend under the colour choice SHALL name the colours shown.

#### Scenario: Colour by label
- **WHEN** colour is by label
- **THEN** bug cards are peach, feature cards sky and enhancement cards sage, and the legend lists them

#### Scenario: Whole row
- **WHEN** the list is coloured by status
- **THEN** every cell of a Planned row has a light blue wash, and a Shift-selected row keeps its ring

#### Scenario: Colour none
- **WHEN** the colour choice is none
- **THEN** no list row is tinted

### Requirement: The same organisation in every view
The list SHALL group its rows by the column field, then by the row field inside each column (a row group only where the column has items), and the roadmap SHALL group its items by the row field, then by the column field (a row header with column subgroups); both SHALL be coloured by the colour field. With rows none they group by the columns alone.

#### Scenario: List and roadmap by feature and status
- **WHEN** rows are by feature and columns by status, and the user opens the list and then the calendar
- **THEN** the list shows a group per status holding a group per feature, and the calendar a group per feature holding a group per status

#### Scenario: Drop into a row group of the list
- **WHEN** rows are by feature and columns by status, and the user drags an item onto the "Notes" group inside "In Review"
- **THEN** the item's status is In Review and its feature is Notes

### Requirement: Merged content by source and date
A merge SHALL head each source's task group "#wi<N> <title> · merged <date>" and SHALL stamp every note it moves in with `mergedFrom` and `mergedAt`. The Subtasks tile SHALL show each task group as a collapsible header with its done and total counts (a fully done group starts collapsed; a single group shows no header). The Change log SHALL list the item's own notes first, then one collapsible section per merged source, newest merge first, headed "From #wi<N> · merged <date>".

#### Scenario: Three items merged
- **WHEN** #wi64 and #wi92 are merged into #wi85 on 9 Nov
- **THEN** #wi85's subtasks show the groups "#wi64 Notes editor · merged 2026-11-09" and "#wi92 Changelog UI issues · merged 2026-11-09", and its change log ends with "From #wi92 · merged 9 Nov" and "From #wi64 · merged 9 Nov"

### Requirement: Distinct colours per value
When colour is by feature or by contributor, each value SHALL take a colour from a palette of twelve in the order the board lists the values, so the first twelve values never share a colour; the legend and the cards SHALL show the same colour for a value, and filtering SHALL NOT change a value's colour.

#### Scenario: Seven features
- **WHEN** colour is by feature and seven features have items
- **THEN** the legend shows seven different colours and each card has its feature's colour

### Requirement: Pages without an outer frame
The calendar, the work item page and the note page SHALL have no outer frame (border, rounded corners or shadow) around their content, like the list view; the tiles inside keep their own borders.

#### Scenario: Open an item
- **WHEN** the user opens a work item
- **THEN** its header and tiles sit directly on the page background with no frame around them

### Requirement: Side menu sections
The expanded side menu's view options SHALL be split by dashed separators into the organise block (columns, rows, colour and its legend), the sort and filters, and the display switches.

#### Scenario: Three sections
- **WHEN** the user expands the side menu on the board
- **THEN** a dashed line follows the colour legend and another follows the Work items / Subtasks switch

### Requirement: Every list column can be hidden
Every column of the list SHALL have a working control in its header that hides it, however narrow the column; a label wider than its column SHALL be shortened instead of covering or pushing out the control.

#### Scenario: A narrow column
- **WHEN** the Updated column is narrower than its label and the user clicks the × in its header
- **THEN** the Updated column is hidden and listed under Hidden in the side menu

### Requirement: The list header says where the rows are
While the list is scrolled, its sticky header SHALL show the project (when several are open), the column and the row value of the group the topmost visible row belongs to, in the look of their group headers.

#### Scenario: Scrolled into a group
- **WHEN** columns are by status, rows by feature, and the user scrolls until the "Completed" and "Maintainability" headers are out of view
- **THEN** the sticky header shows "Completed › Maintainability"

### Requirement: Rows drag from anywhere
In the list, Shift-click SHALL mark a row as selected, and only a selected row SHALL drag. A list row has no drag handle: pressing a selected row anywhere (the title, the cell's padding, a pill, empty cell space, the row's edge) and moving a few pixels SHALL drag it at once, without holding, as cards drag; every selected row SHALL move with it, to the same place and in their current order. A press on a row that is not selected SHALL never drag: moving selects cells, as in a spreadsheet. Releasing a press without moving SHALL act as a click: a plain click opens the item, Shift-click toggles its selection, and a click on a pill opens its dropdown. Dropping with the pointer over a group's own header row SHALL put the item at the end of that group, not of the group above. Titles SHALL line up under the Title header whether or not their row has subtasks. With one or more rows selected, the Up and Down arrow keys SHALL move them as described under Move rows with the keyboard.

#### Scenario: Shift-click selects
- **WHEN** the user Shift-clicks a row in the list
- **THEN** the row is marked selected, and Shift-clicking it again unselects it

#### Scenario: Drag a selected row
- **WHEN** the user presses a selected Backlog row, anywhere on it, and moves it onto the Planned group
- **THEN** the item's status becomes Planned

#### Scenario: Drag several selected rows
- **WHEN** the user has two Planned rows selected, presses one of them and drags it onto the Confirmed group
- **THEN** both items' status becomes Confirmed, and they land in the order they had

#### Scenario: Press on an unselected row
- **WHEN** the user presses a row that is not selected, on its text or off it, and moves to another cell
- **THEN** cells are selected and no row drags

#### Scenario: Hold and drag
- **WHEN** the user presses a selected row and holds without moving
- **THEN** nothing lifts, and the drag starts only once the pointer moves

#### Scenario: Click
- **WHEN** the user clicks a row's title without moving, or a pill
- **THEN** the item opens, or the pill's dropdown opens

### Requirement: Project name opens its options
With several projects open, clicking a project's name on the board or in the list SHALL open a menu with Collapse (or Expand when collapsed) and Hide project, and Make it the active project for a project that is not the active one. The header SHALL show no "active" label and no options icon. A Shift-click SHALL select the project without opening the menu.

#### Scenario: Collapse from the name
- **WHEN** the user clicks a project's name and picks Collapse
- **THEN** its columns close and the menu then offers Expand

#### Scenario: Shift-click
- **WHEN** the user Shift-clicks the project's name
- **THEN** the project is selected and no menu opens

### Requirement: No agent-review filter
The side menu SHALL NOT offer an "agent work to review" filter; settings saved with it SHALL still load.

#### Scenario: Old settings
- **WHEN** the app opens settings that still carry the agent-only option
- **THEN** the board loads and shows every item the other filters leave

### Requirement: Times on the changed period
The changed-period picker SHALL offer an optional from time and to time once a day or range is picked. With times set, only items with an event, note or file write inside the exact window SHALL be shown, and the field and the legend SHALL show the times; without times whole days count. "Show all" SHALL clear dates and times.

#### Scenario: Overnight window
- **WHEN** the user picks 10 Nov to 11 Nov and sets from 20:00 and to 06:00
- **THEN** only items touched between 10 Nov 20:00 and 11 Nov 06:00 are shown and the legend names that window

### Requirement: Move and hide rows
Shift-clicking (or Ctrl/⌘-clicking) a swimlane header SHALL select it. The up and down arrow keys SHALL move the selected rows past the next unselected one, and dragging a header onto another SHALL move it there. A × on the header SHALL hide the row, and the side menu's Hidden list SHALL bring it back. The order and the hidden rows SHALL be remembered per board and row field.

#### Scenario: Move a label row up
- **WHEN** rows are by label and the user Shift-clicks the "feature" header and presses the up arrow
- **THEN** the feature row shows above the row before it, and stays there after a restart

#### Scenario: Hide a row
- **WHEN** the user clicks × on the "bug" row
- **THEN** the row is gone from the board and listed under Hidden in the side menu, whose eye brings it back

### Requirement: The list has its own groups and subgroups
The List view SHALL name its two Organise options "groups" and "subgroups", both offering "none" first and every field the board's columns offer, and both SHALL be none until the user chooses, so a fresh list is flat: no group headers, every item once, in the sort order. The board SHALL keep "columns" (status by default) and "rows" (none by default), and a choice made in one view SHALL NOT change the other.

#### Scenario: A fresh list is flat
- **WHEN** the user opens the List view on a repository with no saved list choice
- **THEN** the options read "groups: none" and "subgroups: none", the grid has no group header rows and shows the Status column

#### Scenario: Choosing a group
- **WHEN** the user picks "status" as the list's groups
- **THEN** the rows are grouped by status, the Status column stays, the choice is saved, and the board's columns are still status

#### Scenario: A saved list keeps its grouping
- **WHEN** a repository's saved settings hold a list view grouped by columns and rows from before the list had its own
- **THEN** the list opens grouped by them

### Requirement: Show only what changed in a period
The side menu SHALL offer a changed-in period (two days). While it is set, only work items created or changed in the period (an event in its log, a note, its creation day or a file write) SHALL be shown on the board, in the list, on the calendar and in Analytics. The choice SHALL be remembered per board.

#### Scenario: A week of changes
- **WHEN** the user picks 1 Nov to 5 Nov under changed
- **THEN** only the items touched in those days remain in every view

### Requirement: Period legend and Show all
While a changed period is set, the legend SHALL name it and say how many items are shown of those the other filters leave, and the picker SHALL offer "Show all", which clears the period.

#### Scenario: Show all
- **WHEN** the legend reads "changed 1 Nov – 5 Nov" and "11 of 34 shown" and the user clicks Show all in the picker
- **THEN** every item is back and the picker reads "any time"

### Requirement: The list survives saved settings from another session
The list SHALL open with the board's current grouping even when its saved list settings were made with a different number of projects open; saved settings SHALL never stop the page from responding.

#### Scenario: A second project opened later
- **WHEN** the list's settings were saved with one project open and the user opens the list with two
- **THEN** the rows are grouped by project and the side menu keeps working

### Requirement: Statuses per project
Each project SHALL keep its own ordered list of statuses in its openspec/config.yaml under x-rtm.statuses, and the board's columns, the list's groups, the status pills and the commands that set a status SHALL use that list. A project without the line SHALL have the built-in list.

#### Scenario: A project with its own list
- **WHEN** a project's config.yaml lists statuses Backlog, Planned, In Progress, Review ready, Completed
- **THEN** its board shows those five columns in that order and a work item with status Review ready is in the fourth

#### Scenario: No list
- **WHEN** a project's config.yaml has no x-rtm.statuses
- **THEN** its board shows Backlog, Planned, Confirmed, In Progress, Blocked, In Review and Completed

### Requirement: Fixed statuses and removing a status
Backlog, In Progress and Completed SHALL always be in a project's list and SHALL NOT be removed or renamed; Completed still means archived. Any other status SHALL be removable, and only when no work item has it: otherwise the Settings page SHALL ask which status the work items move to, move them and then remove it.

#### Scenario: Fixed statuses stay
- **WHEN** the user looks at a project's status list in Settings
- **THEN** Backlog, In Progress and Completed have no remove button, and every status can be moved up and down

#### Scenario: Removing a status that work items have
- **WHEN** the user removes a status that work items have
- **THEN** the app asks which status they move to, moves them there and then removes the status

### Requirement: Merging status columns across projects
With several projects open and the board grouped by status, the board SHALL show one column per distinct status of the projects' lists, and the user SHALL be able to Shift-select two or more status column headers and choose Merge from a header menu. The merged column SHALL hold the work items of every merged status in every project and be named after the first selected column unless another name is typed. Unmerge on its header SHALL show the statuses as separate columns again.

#### Scenario: Merging completed and Completed
- **WHEN** one project has the status completed, another has Completed, and the user Shift-selects both headers and chooses Merge
- **THEN** one column Completed shows the work items of both statuses

#### Scenario: Unmerge
- **WHEN** the user chooses Unmerge on the merged column's header
- **THEN** the two statuses are separate columns again

#### Scenario: Dropping a card into a merged column
- **WHEN** the user drops a card into a merged column and its project has more than one of the merged statuses
- **THEN** the app asks which of them to use; with one it uses that one, and with none it says so and changes nothing

### Requirement: A merge is a view setting
A merge SHALL be saved per user and per board in the user's settings (BoardOptions.Merges), SHALL match statuses by their exact text in each project, and SHALL NOT change any project's files or work items.

#### Scenario: Nothing in the projects changes
- **WHEN** the user merges two status columns
- **THEN** the user's settings file records the merge and no project's config.yaml or work item changes

### Requirement: Look-alikes of the fixed statuses
A status that equals Backlog, In Progress or Completed when case, spaces and dashes are ignored (completed, InProgress, in progress) SHALL be offered in its column header menu as Merge with the fixed status, and the fixed status's column SHALL offer Merge look-alikes, each merging in one click.

#### Scenario: One click
- **WHEN** the user chooses Merge with Completed in the menu of the completed column
- **THEN** completed and Completed become one column called Completed

### Requirement: Adding a status from the board
When the board's columns are statuses, a "+ status" button SHALL follow the last column header, like "+ feature", ask for a name and add the status to the project's list before Completed, so the new empty column appears at once.

#### Scenario: Adding a status
- **WHEN** the board is grouped by status and the user chooses + status and types Review ready
- **THEN** the project's list has Review ready before Completed and the board shows an empty Review ready column before Completed

#### Scenario: Several projects
- **WHEN** the board shows several projects under one shared header row and the user chooses + status
- **THEN** the app asks which project, or All projects, before it asks for the name

#### Scenario: A name that exists
- **WHEN** the user types the name of a status the project has already, spelled exactly
- **THEN** the app refuses it with a message and the list does not change

### Requirement: The menu has a commit selector
The menu's Filters section SHALL have a commit selector whose choices are any (the default), uncommitted, and the newest commits of the repository (short hash, subject, day). The choice SHALL be saved with the board's other options, and the folded Filters header SHALL name it.

#### Scenario: Choosing a commit
- **WHEN** a commit is chosen in the selector
- **THEN** the choice is saved with the board and the Filters header says commit and its short hash

### Requirement: The board shows only the work items of the chosen commit
WHEN a commit is chosen, the board, the list and the calendar SHALL show only the work items whose change folder holds a file that commit changed, an archived change counting under its archived folder. WHEN uncommitted is chosen they SHALL show only the work items whose files are changed or new in the work tree. Any SHALL leave no work item out.

#### Scenario: A commit is chosen
- **WHEN** the selector is set to a commit
- **THEN** only the work items that commit changed are shown, together with the other filters

#### Scenario: Uncommitted is chosen
- **WHEN** the selector is set to uncommitted
- **THEN** only the work items with changed or new files are shown

#### Scenario: Any
- **WHEN** the selector is set to any
- **THEN** no work item is left out for it

### Requirement: Work items with uncommitted changes are marked
WHILE the commit selector is on any, a card of a work item whose files are changed or new in the work tree SHALL carry one small muted dot before its title.

#### Scenario: One item is edited
- **WHEN** a work item's files have uncommitted changes and no commit is chosen
- **THEN** its card shows the dot and the cards of committed items do not

### Requirement: A work item shows its last contributing commit
The work item page header and the examine panel SHALL show the newest commit that changed the item's folder (short hash, with the subject), or that the item has uncommitted changes while its files are changed or new; nothing when git knows nothing of it.

#### Scenario: Opening an item
- **WHEN** a work item's folder was last changed by a commit
- **THEN** its page header shows that commit's short hash and the examine panel shows the hash and subject

#### Scenario: An item is edited since
- **WHEN** a work item has files that are not committed yet
- **THEN** both show that it has uncommitted changes

### Requirement: Updated is the last change in the worklog
A work item's updated time (the list's Updated column, the "date modified" sort, the card's date and the changed-period filter) SHALL be the time of the last change in its worklog: the newest line of its events.log or the newest note's created time. The time its files were written SHALL only stand in for an item with no worklog, because a checkout or sync rewrites every file.

#### Scenario: Files rewritten by a checkout
- **WHEN** a work item's events.log ends on 20 Oct and all its files were written on 1 Nov
- **THEN** its Updated is 20 Oct

#### Scenario: A note after the last event
- **WHEN** a note was created on 25 Oct and the last event is from 6 Oct
- **THEN** its Updated is 25 Oct

#### Scenario: No worklog
- **WHEN** a work item has neither events nor notes
- **THEN** its Updated is the time its files were last written

### Requirement: Agent changes are flagged
An item an agent added or changed is flagged while `x-rtm` has `agentBy` and no `reviewedAt` at or after `agentAt`. Cards and list rows SHALL NOT show an agent chip; the flag is reviewed on the work item page. In the list, each subtask an agent added SHALL carry a chip whose click clears that subtask only. The side menu SHALL offer "agent changes only".

#### Scenario: Chip on a card
- **WHEN** #wi34 has `agentBy: Claude` and `agentAt` and no `reviewedAt`
- **THEN** its card and list row show no agent chip, "agent changes only" still lists it, and its page reviews it

#### Scenario: Flagged again
- **WHEN** the agent later stamps a newer `agentAt`
- **THEN** the item is flagged again: the page shows the review control and the filter lists it

#### Scenario: Agent changes only
- **WHEN** the user ticks "agent changes only"
- **THEN** only items with a flagged item or flagged subtask remain

### Requirement: Groups move like columns
In the list, Shift-click on a group or subgroup header SHALL select it, marked as a selected row is, and Up and Down SHALL then move it among its siblings one place per press, in the same saved order the board's columns (rows, for subgroups) use. The grid's header sort SHALL order only the rows inside each group: groups and subgroups SHALL keep their saved order.

#### Scenario: Select and move a group
- **WHEN** the list is grouped by status, the user Shift-clicks the Planned group header and presses Up
- **THEN** Planned is above the group that was above it and still selected, and the board's columns show Planned in that place too

#### Scenario: A plain click selects nothing
- **WHEN** the user clicks a group header's name without Shift and presses Down
- **THEN** nothing is selected and no group moves, and the chevron still folds the group

#### Scenario: A subgroup moves among its siblings
- **WHEN** the list is grouped by status with subgroups by priority and the user Shift-clicks a Low subgroup header and presses Up
- **THEN** the Low subgroup is above the High one in every group, the groups keep their order, and no row changes group

#### Scenario: A header sort keeps the group order
- **WHEN** the list is grouped by status with Backlog above Planned, and the user clicks the Updated header
- **THEN** the rows inside each group are ordered by Updated, and the groups stay in their saved order, the group of the oldest row not moving to the top

### Requirement: Groups drag like columns
In the list, a selected group or subgroup header SHALL drag with the mouse: pressing it and moving a few pixels lifts the header and shows a drop line between the headers it would land among, and releasing writes the saved order the arrow keys use, so the board's columns (rows, for subgroups) follow. Every selected sibling SHALL move with it, in their current order, and a subgroup SHALL drag only among its own siblings. A header that is not selected SHALL never drag.

#### Scenario: Drag a selected group
- **WHEN** the list is grouped by status, the user Shift-clicks the Planned header, presses it and moves it above the Backlog header
- **THEN** the header lifts, a drop line shows above Backlog, and on release Planned is above Backlog, still selected, and the board's columns show Planned first too

#### Scenario: Drag several selected groups
- **WHEN** the user has two groups selected and drags one of them above another group
- **THEN** both land there, in the order they had

#### Scenario: Press on an unselected group header
- **WHEN** the user presses a group header that is not selected and moves
- **THEN** nothing drags and no group moves, and a plain click still folds the group

#### Scenario: Drag a subgroup
- **WHEN** the list has subgroups by priority and the user drags a selected Low subgroup header above the High one of the same group
- **THEN** Low is above High in every group, and the groups and rows keep their place

### Requirement: A project's section shows only its own statuses
With several projects open and the board grouped by status, the system SHALL show in a project's section only the statuses that project's own openspec/config.yaml lists. The shared header row SHALL list the union of the projects' statuses in the saved order, shown once; under that header a project without a status SHALL show an empty, visibly unavailable cell instead of a working column, and the list SHALL show no group for a status the project lacks.

#### Scenario: A status only one project has
- **WHEN** two projects are open and only one of them has a Research status
- **THEN** the header row shows Research once
- **AND** the other project's Research cell is empty and marked unavailable, and its list has no Research group

### Requirement: Moving a card to a status its project lacks asks to add it
When a card is dropped, or moved with the arrow keys, onto a status its project does not have, the system SHALL ask "Add <status> to <project>?" and SHALL NOT fail with a message. On yes the status is added to that project's openspec/config.yaml before Completed and the card moves; on no nothing changes.

#### Scenario: Yes adds the status and moves the card
- **WHEN** the user drops a card of a project without Research onto the Research cell and answers yes
- **THEN** that project's config.yaml lists Research and the card has the status Research

#### Scenario: No changes nothing
- **WHEN** the user answers no
- **THEN** the project's config.yaml and the card are unchanged

### Requirement: Deleting a status
A status that is not one of the project's default statuses (Backlog, In Progress, Completed) SHALL be deletable from Settings. Open work items that have it SHALL move to the project's first status, after a confirmation that names how many move; archived work items SHALL keep the deleted name in their file and are not rewritten. Open work items SHALL move whatever they wait on: an item waiting on an open dependency does not keep the status in place.

#### Scenario: Open items move to the first status
- **WHEN** the user confirms removing a status that two open work items have
- **THEN** the status leaves the project's list and both items have the project's first status

#### Scenario: An item waiting on a dependency moves too
- **WHEN** the user confirms removing a status that an open work item has while that item waits on an unresolved dependency
- **THEN** the status leaves the project's list and no item of the status is left behind or half moved

#### Scenario: Archived items keep the name
- **WHEN** a status is removed that an archived work item was in when it was archived
- **THEN** the archived item's file still names that status and the item still shows as Completed

#### Scenario: A default status stays
- **WHEN** the user tries to remove Backlog, In Progress or Completed
- **THEN** the status is kept and Settings offers no remove button for it

### Requirement: Deleting a status or a feature from the viewers
The board's column header menu and the list's group header menu SHALL offer "Delete status…" on a custom status and "Delete feature…" on a feature, with the confirmation Settings uses. A default status, a merged column, Unassigned and any other grouping SHALL offer no delete.

#### Scenario: Delete a status from the board column
- **WHEN** the user opens the menu of a custom status column and chooses Delete status…, then confirms
- **THEN** the column's open work items move to the project's first status and the status leaves that project's openspec/config.yaml

#### Scenario: A default status has no delete
- **WHEN** the user opens the menu of the Backlog column or the Backlog group
- **THEN** there is no Delete status… entry

#### Scenario: Delete from the list's group header
- **WHEN** the user opens the menu on a custom status group of the list and confirms Delete status…
- **THEN** the group leaves the list and its items are in the first status's group

#### Scenario: Delete a feature from the list's group header
- **WHEN** the list is grouped by feature and the user chooses Delete feature… on a feature group and confirms
- **THEN** the group leaves the list and its open work items are in Unassigned

#### Scenario: Several projects
- **WHEN** two projects are open and both have the status the user deletes from the shared header row
- **THEN** the user is asked which project, and only that project loses the status

### Requirement: Deleting a feature
Deleting a feature SHALL remove it from its open work items, which become unassigned, and SHALL leave archived work items on it.

#### Scenario: Archived items stay on a deleted feature
- **WHEN** a feature is deleted that an open and an archived work item have
- **THEN** the open item has no feature and the archived item still lists the deleted feature

### Requirement: Merging items whose feature was deleted
Merging work items SHALL accept items that share the same deleted feature, SHALL give the merged item the existing feature when another source has one that still exists, and SHALL refuse to merge items that have two different features that no longer exist, saying which.

#### Scenario: The same deleted feature merges
- **WHEN** two archived items with the same deleted feature are merged
- **THEN** the merged item keeps that feature

#### Scenario: An existing feature takes over
- **WHEN** an item with a deleted feature is merged with an item whose feature exists
- **THEN** the merged item has the existing feature

#### Scenario: Two different deleted features are refused
- **WHEN** two items with different deleted features are merged
- **THEN** the merge is refused with a message naming both features and neither item is changed

### Requirement: The user's column order wins
The system SHALL show every board column in the board's saved order, with one project open or several, under a shared header row or in each project's section, and SHALL save every move of a column (drag, Left and Right on a Shift-clicked header) with the board, so it survives a restart. A status the saved order does not hold yet SHALL take the place the projects' status lists give it.

#### Scenario: Dragging a status of one project in the shared header
- **WHEN** two projects are open, only one has Research, and the user drags the Research header in front of Blocked
- **THEN** the header row shows Research in front of Blocked and the other project's unavailable Research cell is in the same place
- **AND** after the app is closed and opened again the order is the same

#### Scenario: Arrow keys on a selected column
- **WHEN** the user Shift-clicks the Research header and presses Left
- **THEN** Research moves one place left, one more for each further press, with one project open as with several

#### Scenario: A status the saved order lacks
- **WHEN** the saved order was written before a project's status existed
- **THEN** that status shows where the project's list puts it, not after every column the order holds

### Requirement: Adding a project keeps the board arrangement
The board SHALL keep showing the arrangement (column order, hidden columns, grouping, filters) of the project the user was viewing when another project is added to the workspace. Only the first project opened and the user's choice of a project tab SHALL change which project's saved view the board shows.

#### Scenario: A project is added while another is shown with a custom column order
- **WHEN** the user has moved the Research column to the far left of project A's board and then adds project B
- **THEN** the board still shows project A's view with Research first, and B's statuses that A lacks appear where B's own status list puts them

#### Scenario: The user chooses a project's tab
- **WHEN** the user clicks project B's tab
- **THEN** the board shows B's saved view
