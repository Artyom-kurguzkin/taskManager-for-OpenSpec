# Proposal

<!-- merged from #wi27 can't rearange work items within the collumn -->
## Why

to signify in which order i am planning to work on the items

## What Changes

- can rarange items ordering within the same subgrup

<!-- merged from #wi35 Reorder cards within a column -->
## Why

Cards in a column follow a fixed sort (priority, date, …), with no way to line them up in the order you plan to address them. Dropping also needed precise aim: a card or header had to land exactly on another one.

## What Changes

- While dragging, the cards (or columns) from the insertion point onwards slide aside to open a gap, and the drop lands in that gap.
- Cards can be reordered within a column and placed at a position in another column. The first such drop switches the sort to a new "my order" option. Cards never placed follow in priority order.
- Dragging a selected card moves every selected card together.
- Clicking a card opens it; Shift-click (or Ctrl/⌘-click) adds it to the selection or removes it. The card tick boxes are gone.
- Arrow keys move the selected cards: up/down inside the column (a scattered selection gathers first), left/right into the neighbouring column.
- The drag is driven by pointer events, not native HTML5 drag-and-drop: the real card follows the pointer, neighbours slide aside with transforms, and after a drop or arrow move the board animates from old to new positions (FLIP).
- Column headers use the same gap: a column lands in front of the column after the gap, or at the end of the row (it used to take the target column's place).
- The card order is per user, in the settings file, like column order. Picking another sort ignores it but keeps it.

## Capabilities

### Modified Capabilities
- `board`: drop preview gap, card reordering, multi-card drag, "my order" sort, click to select; column reorder lands at the gap.
- `workspace`: a file that was moved or deleted since it was read is not a conflicting edit.

- Creating a work item whose folder name an archived change used to have no longer fails with "This file changed on disk".

- Archiving skips npx when it has the CLI cached and shows a busy panel while the CLI runs.

## Impact

- `TaskManager/Board/BoardModel.cs`, `TaskManager/Components/Pages/BoardPage.*`, `WorkItemCard.razor`, `wwwroot/js/app.js`, `wwwroot/css/app.css`.
