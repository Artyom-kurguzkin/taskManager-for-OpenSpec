## MODIFIED Requirements

### Requirement: One selection look
Anything selected with Shift-click — a card, a list row, a project heading, a subtask — SHALL look the same: it keeps its own surface, a solid line in the text colour hugs it and a yellow halo runs round that line, wide enough to be seen at a glance on the light theme. Selection SHALL never be shown as a fill.

#### Scenario: Card and row
- **WHEN** the user Shift-clicks a card on the board and a row in the list
- **THEN** both show the same solid line with a yellow halo round it, and neither changes its fill

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Unassigned columns
Work items with no feature (when grouped by feature) or no contributor (when grouped by contributor) SHALL appear in a first column of their own, Unassigned or No contributors, shown only when it has items. Items without a label SHALL appear in an Unlabeled column when grouped by label.

#### Scenario: Unassigned
- **WHEN** a work item has no feature and the board is grouped by feature
- **THEN** it shows in the Unassigned column at the front, and dragging a card there removes its manual feature

### Requirement: Create and delete features
When grouped by feature, "+ feature" at the end of the row of columns SHALL add a feature, listed in `openspec/config.yaml` under `x-rtm.features` (OpenSpec only accepts a capability spec once it has requirements). Every feature column SHALL offer "Delete feature…", which asks for confirmation and unlinks the feature from active work items, which then show as Unassigned. A feature listed in `x-rtm.features` is removed from there; a feature with a spec under `openspec/specs/` is deleted with its spec folder. Deleting SHALL be refused, naming the change, while an open change still has a delta spec for the feature, and refused when the feature's folder holds other features.

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
- **WHEN** the user deletes a feature for which an open change has a delta spec
- **THEN** nothing is deleted and the message names that change

## REMOVED Requirements

### Requirement: Bucket grouping
**Reason**: Buckets (supergroups above the feature columns) were confusing; features are one flat row.
**Migration**: A board saved as grouped by "bucket" opens grouped by "feature".

### Requirement: Group work items
**Reason**: Replaced by "Group by category": "bucket" is now "feature", a flat row with no supergroups.
**Migration**: A board saved as grouped by "bucket" opens grouped by "feature".

## ADDED Requirements

### Requirement: Grouping is not repeated
Whatever the board or list is grouped by SHALL NOT be shown again inside a group: the list SHALL hide the Status column when grouped by status, Label by label, Feature by feature and Contributors by contributor; a card SHALL leave out its label when grouped by label and its contributors when grouped by contributor.

#### Scenario: List grouped by status
- **WHEN** the list is grouped by status
- **THEN** it has no Status column; grouped by label it has a Status column and no Label column

#### Scenario: Card grouped by label
- **WHEN** the board is grouped by label
- **THEN** a card shows no label tag; grouped by status it shows it

## ADDED Requirements

### Requirement: Card contents
An expanded card SHALL show its title band, its features as chips (its status instead when grouped by bucket, since the column is the feature), its date, contributors and tags. It SHALL NOT show a done/total subtask count.

#### Scenario: Features on cards
- **WHEN** the board is sorted by feature and a card is linked to Board and Work item editor
- **THEN** its expanded form shows a "Board" chip and a "Work item editor" chip

#### Scenario: No subtask count
- **WHEN** a card's work item has 5 of 7 subtasks done
- **THEN** the expanded card does not show "5/7"

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

## ADDED Requirements

### Requirement: Move rows in the list
Each list row SHALL have a drag grip. Dragging it in front of another row of its group SHALL place it there and switch the sort to "my order"; dragging it onto another group SHALL move it there as a drop on that board column would. A selected row SHALL carry every selected row. While dragging, the rows passed SHALL slide aside, and after the drop the rows SHALL slide to their new places.

#### Scenario: Reorder
- **WHEN** the user drags the third Planned row's grip in front of the first
- **THEN** it becomes the first Planned row and the sort reads "my order"

#### Scenario: Change group
- **WHEN** the user drags a Planned row below the last In Progress row
- **THEN** its status becomes In Progress and it is the last row of that group

### Requirement: Move rows with the keyboard
With rows selected (Shift-click, as on the board; the list has no tick boxes), the arrow keys SHALL move them as on the board, whatever has the keyboard focus, as long as the user is not typing in a field.

#### Scenario: Arrow after selecting a row
- **WHEN** the user Shift-clicks a row and presses the up arrow
- **THEN** the row moves up one place and the page does not scroll

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

## MODIFIED Requirements

### Requirement: List table
The list view SHALL be a data grid with columns ID, Title, Status, Label, Feature, Contributors, Priority, Progress and Updated, over the same rows the board shows, grouped by the current grouping with collapsible group headers showing a count and an "+ Add item" row; an empty column SHALL keep its group with a "No items" row. The user SHALL be able to sort by any column (clicking a header sorts, again reverses, a third click clears; several columns at once), filter a column from the funnel in its header (operator and value, optionally a second pair joined by and/or), resize columns, and reorder them by dragging the grip at the left of a header. A column SHALL be hidden from the × in its header and listed under Hidden in the side menu, where the eye brings it back, exactly as a board column is. The grid SHALL carry no toolbar or filter row of its own. All of this SHALL be remembered per repository.

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

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Sort and filter
The board SHALL sort the cards within each column by priority, feature, contributor, date modified, date created, status, title, id or the user's own order, and SHALL filter them by contributor. The chosen grouping, sort, filter and view SHALL be remembered per repository.

#### Scenario: Contributor board sorted by feature
- **WHEN** the board is grouped by contributor and sorted by feature
- **THEN** each contributor's column lists their work items ordered by their first feature name

#### Scenario: Filter by contributor
- **WHEN** the user filters by Tom
- **THEN** only work items whose contributors include Tom are shown, and column counts update

### Requirement: Reorder columns
The user SHALL reorder columns in any grouping by dragging a column header along its row: the column lands at the gap the other columns open, in front of the column after the gap, or at the end of the row. The order SHALL be remembered per repository and grouping in the user's settings file, not in the repository.

#### Scenario: Move Backlog to the end
- **WHEN** the user drags the Backlog header past the Completed column
- **THEN** the columns read Planned, Confirmed, In Progress, Blocked, In Review, Completed, Backlog, and stay so after a restart

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘/Shift-click) and merge them. The merged work item SHALL combine the proposals, tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. Merging SHALL be refused when two sources change the same requirement.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105 and picks "Merge into one work item"
- **THEN** one new change holds both proposals' text and all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Hide selected work items
The selection action bar SHALL offer "Delete (hide)", enabled with one or more cards selected, which hides every selected item. This is the only way to hide an item; the work item page offers no hide control of its own. Hiding SHALL clear the selection.

#### Scenario: Hide several
- **WHEN** the user selects three cards and picks "Delete (hide)"
- **THEN** all three items gain `x-rtm.hidden: true`, disappear from the board unless "show hidden" is on, and the selection clears

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
