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
