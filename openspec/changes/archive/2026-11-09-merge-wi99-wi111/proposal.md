# Proposal

<!-- merged from #wi68 Keyboard hints are a setting -->
## Why

Pressing F anywhere outside a text field covered the screen in letter badges. For anyone who doesn't use the keyboard hints that is a surprise, not a feature.

## What Changes

- Keyboard hints are a setting, off by default: Settings → "Keyboard hints". Off, F is just a letter and any open badges close; on, F works as before.
- The layout pushes the switch to the page script with the theme (`rtm.setHints`), so it applies at once and on every start.

## Capabilities

### Modified Capabilities

- `app-shell`: the Keyboard hints requirement gains the setting.

<!-- merged from #wi82 minor UI patches -->
<!-- merged from #wi75 bucket view is unusable + Features, not buckets -->
<!-- merged from #wi30 bucket view is unusable and #wi71 Features, not buckets -->
## Why

The bucket view was unusable: "bucket" was a second name for the same thing and the "Ungrouped / Functional" heading above the columns was confusing. Every feature should be removable, not only the ones added from the board.

## What Changes

- Use tags instead of bucket supergroups.
- "Bucket" is called **feature** everywhere (the group-by choice, hints, dialogs). A board saved as "bucket" opens as "feature".
- Grouped by feature, the board is one flat row of feature columns (Unassigned first), with no supergroup headings. "+ feature" sits at the end of the row.
- A work item has one feature at most. The item page picks it in one dropdown; the list shows it in a Feature column.
- Any item, open or completed, drags to a feature, which becomes its feature; features from its own spec are unlinked, not deleted.
- Any feature can be renamed from its column menu; its spec folder, the changes' delta folders for it and the work items' links follow.
- Any feature can be deleted from its column menu. A spec-backed feature is deleted with its folder under `openspec/specs/` (git can bring it back); it is refused while an open change still has a delta spec for it, or when its folder holds other features.

## Capabilities

### Modified Capabilities

- `board`: feature grouping and deleting features.

<!-- merged from #wi80 Do not repeat what the view is grouped by + List view UX + UI patches -->
<!-- merged from #wi70 Do not repeat what the view is grouped by -->
## Why

Grouped by status, every row of the Backlog group says "Backlog" again in a Status column. The grouping already says it.

## What Changes

- The list hides the column the rows are grouped by: Status (by status), Label (by label), Feature (by feature), Contributors (by contributor). It comes back under another grouping.
- A card leaves out its label when grouped by label and its contributors when grouped by contributor (grouped by feature it already shows its status instead of features).

## Capabilities

### Modified Capabilities

- `board`: no repeated grouping field.

<!-- merged from #wi79 List view UX + UI patches -->
<!-- merged from #wi67 List view UX -->
<!-- merged from #wi51 Feature chips on cards, and removable spec features -->
## Why

- Sorting the board by feature gave no sign of which feature a card belongs to: expanded cards showed the features as one line of faint grey text.
- The expanded card's done/total subtask count was noise next to the date and contributors.
- A feature that comes from a change's specs/ folder, which is every feature of a merged or archived item, had no remove control, so a wrong link could not be undone.

## What Changes

- Expanded cards show each feature as a dashed chip in the normal text colour. Grouped by bucket they show the status instead, as before.
- Expanded cards no longer show the done/total subtask count.
- Hidden columns listed in the side menu show how many items they hold.
- In the list view an item's features stack in a column in their cell instead of stretching the table.
- Every feature chip on the item page has a remove cross. Removing one that comes from specs/ records it in `x-rtm.unlinkedFeatures`, so the item stops listing it and the board stops placing it in that feature's column. The spec files are untouched. "+ add" offers the feature again, and adding it links it back.

## Capabilities

### Modified Capabilities
- `board`: expanded cards show feature chips and no subtask count.
- `work-item-editor`: features from specs/ can be unlinked and linked again.

## Impact

- `WorkItems/` (`Commands.cs`, `Queries.cs`, `Infrastructure/Metadata.cs`, `PublicDTOs/WorkItem.cs`, `WorkItemsApi.cs`), `TaskManager/Components/Pages/WorkItemCard.razor`, `WorkItemPage.razor`, `wwwroot/css/app.css`.

<!-- merged from #wi59 List view UX -->
<!-- merged from #wi57 List rows drag like cards; cells select like a spreadsheet -->
## Why

The list view could not reorder or move rows: everything the board does by drag and drop and arrow keys was missing there. Dragging the mouse across the table also selected text row by row, so a column of ids could not be picked out and copied.

## What Changes

- Each row has a grip in its first cell. Dragging it works as on the board: in front of another row reorders (switching to "my order"), onto another group changes the status (or feature, label, contributor); a checked row carries every checked row. The other rows slide aside and settle with the same animation.
- With rows checked, the arrow keys move them as on the board, whatever has the focus.
- The header chevron moves to the right end and collapses or expands every group; the side menu's compact switch shows or hides every subtask row in the list.
- Dragging across cells selects the rectangle between them, as in a spreadsheet; Shift-click extends it; Esc or a click outside the table clears it. Ctrl+C copies the rectangle as tab-separated text, one line per row, with a row's stacked features joined by commas. Plain text selection in the table is off.

## Capabilities

### Modified Capabilities
- `board`: list rows drag and move with the keyboard; list cells select and copy.

## Impact

- `TaskManager/Components/Pages/BoardPage.razor`, `Components/Ui.cs` (grip icon), `wwwroot/js/app.js`, `wwwroot/css/app.css`.

<!-- merged from #wi58 List view on a data grid -->
## Why

The list view was a hand-built table. Every common grid feature (filtering, column resize, reorder, show/hide, multi-sort) had to be written from scratch, one request at a time.

## What Changes

- The list view is a Radzen DataGrid (MIT, .NET 10) over the same rows the board shows: one row per work item per column, grouped by column in the board's order. An empty column keeps a group with a "No items" row.
- From the grid: sorting by any column (several at once), a filter popup behind a funnel in each header, column resize, and column reorder by dragging the grip at the left of a header. A column is hidden from the × in its header and restored from the side menu's Hidden list, like a board column; the side menu's compact switch collapses every group and row. No toolbar or filter row, no new switches.
- Kept from the app, re-attached to the grid's rows: the status, label and priority dropdowns, feature chips, row drag by the grip (in front of a row or onto a group row), arrow keys, checkbox selection, spreadsheet-style cell selection and copy, subtask detail under a row, and the collapse-all-groups chevron, now in the grid's header bar beside the column picker.
- Radzen's stylesheet is not loaded: the grid's markup is dressed by the app's own stylesheet in the app's tokens, so it keeps the list's previous look in both themes. Its script and icon font ship with the app (no network).

## Capabilities

### Modified Capabilities
- `board`: the list table's features and the sort / filter / column settings it remembers.

## Impact

- `TaskManager/TaskManager.csproj` (Radzen.Blazor), `Program.cs`, `_Imports.razor`, `wwwroot/index.html` (script only), `Board/BoardModel.cs` (GridRow, Rows; BoardOptions.ListSettings), `Components/Pages/BoardPage.razor` and `.cs`, `wwwroot/js/app.js`, `wwwroot/css/app.css`.

<!-- merged from #wi78 UI patches -->
<!-- merged from #wi49 navigation hotkeys patches -->
<!-- merged from #wi44 Shift + hint letters selects the item -->
## Why

Keyboard hints (F) could only click an element. Selecting cards or subtasks, which is a Shift-click with the mouse, had no keyboard path, and subtask texts had no badges at all.

## What Changes

- With the badges showing, typing a badge's letters with Shift held Shift-clicks that element: a card or subtask is selected instead of opened or edited.
- After a Shift-selection the badges stay up, so several items can be picked in a row; F or Esc hides them.
- Subtask texts get badges: plain letters edit the subtask, Shift + letters selects it.

## Capabilities

### Modified Capabilities
- `app-shell`: Shift + hint letters selects; subtask texts are hinted.

## Impact

- `TaskManager/wwwroot/js/app.js`.

<!-- merged from #wi48 Keyboard hints open with Ctrl+Alt + Keyboard hints open with F -->
<!-- merged from #wi32 Keyboard hints open with Ctrl+Alt -->
## Why

Keyboard hints toggled on a bare Alt press, which fires by accident (Alt-Tab, Alt shortcuts in other apps, brushing the key).

## What Changes

- Ctrl+Alt (pressed in either order) toggles keyboard hints; Alt on its own no longer does.

## Capabilities

### Modified Capabilities
- `app-shell`: keyboard hints are toggled with Ctrl+Alt.

## Impact

- `TaskManager/wwwroot/js/app.js`.

<!-- merged from #wi43 Keyboard hints open with F -->
## Why

Keyboard hints opened with Ctrl+Alt, a two-key chord that is awkward to press and easy to forget. Vimium-style hint modes use a single key.

## What Changes

- F toggles keyboard hints, on and off, when the cursor is not in a text field (a text box, text area, dropdown or editable area). In a field, F types as usual.
- `f` is no longer a badge letter, so F while the badges are showing always hides them.
- Ctrl+Alt no longer toggles hints.

## Capabilities

### Modified Capabilities
- `app-shell`: keyboard hints are toggled with F.

## Impact

- `TaskManager/wwwroot/js/app.js`, a comment in `wwwroot/css/app.css`.

<!-- merged from #wi77 UI patches -->
<!-- merged from #wi63 Saved toast after every write -->
## Why

Edits are written to the repository at once, silently. A short "Saved" confirms it, as editors do.

## What Changes

- Every successful module command run from a page (title, status, subtasks, links, notes…) shows a "Saved" toast; the note's autosave shows it once the text is on disk.
- A more specific toast (Created, Merged, Linked…) replaces it, and it never shows twice at once.

## Capabilities

### Modified Capabilities

- `app-shell`: the Saved toast.

<!-- merged from #wi74 UI patches -->
<!-- merged from #wi50 board UI patches -->
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

<!-- merged from #wi53 item details ui patches -->
<!-- merged from #wi14 responsive design -->
support half and quarter screen sizes

## Why



## What Changes

-

<!-- merged from #wi52 work item details ui patches -->
<!-- merged from #wi37 add contributors does not list other git users + Contributors fully editable -->
<!-- merged from #wi28 add contributors does not list other git users -->
## Why



## What Changes

-

<!-- merged from #wi33 Contributors fully editable -->
## Why

Anyone who had committed to a change's folder was pinned as a contributor with no way to remove them, so you couldn't take yourself off your own item. "+ add" also only listed people already on other items, so in a repo where you were alone there was nobody to add and no way to type a new name.

## What Changes

- Contributors are a plain editable list. Every contributor can be removed, including commit authors.
- Commit authors only fill in the list for an item nobody has edited yet; once it's edited, the saved `x-rtm.contributors` list (possibly `[]`) is what shows everywhere.
- "+ add" lists everyone who has ever committed to the repo (from git, all branches), plus people on other items and you; you can also type any name and press Enter.
- Dragging a card between contributor columns on the board moves that person, commit author or not.

## Capabilities

### Modified Capabilities
- `work-item-editor`: contributors are fully editable and new names can be typed in.

## Impact

- `GitHistory` (AllAuthors), `WorkItems` (Queries, Metadata), `TaskManager/Components/Pages/WorkItemPage.razor`, `BoardPage.razor.cs`.

<!-- merged from #wi38 Declutter the work item and note page chrome + Work item page layout and folding -->
<!-- merged from #wi31 Declutter the work item and note page chrome -->
## Why

The work item and note pages opened with a breadcrumb bar above the card ("← back · esc goes to previous page · <path>") that duplicated the Esc shortcut in words, and repeated "Delete (hide)" in the item header even though the board's selection bar already hides items in bulk. Both read as clutter rather than information.

## What Changes

- The breadcrumb bar is gone. Going back is now a `←` button in the top-right corner of the card; like Esc, it returns to the previous page (e.g. the item you came from), not always the board.
- The repository-relative file path is no longer shown at all; it wasn't useful chrome.
- The work item page no longer offers "Delete (hide)" — hiding an item is now only done from the board's selection bar. A hidden item's page still offers "Restore".

## Capabilities

### Modified Capabilities
- `work-item-editor`: close affordance and file path move from a breadcrumb bar into the card; hiding is no longer offered from the item page.
- `app-shell`: the title bar no longer draws the icon or "Repo tasks".
- `board`: a compact-cards button in the collapsed side menu; the selection bar's "Delete (hide)" no longer references a per-item control on the work item page.

## Impact

- `TaskManager/Components/Pages/WorkItemPage.razor`, `NotePage.razor`, `wwwroot/css/app.css`, `Services/WindowFrame.cs`, `Program.cs`.

<!-- merged from #wi36 Work item page layout and folding -->
## Why

The work item page had four lists (subtasks with dependencies, linked commits, linked tests, the change log) plus the items waiting on this one, laid out as panels that wrapped below a long description. They had no common hierarchy, and reaching commits and tests meant scrolling past the description. In the dark theme, features that come from the change's specs/ folder were unreadable: muted text on a light-grey fill.

## What Changes

- The page is a bento of equal tiles: Description, Subtasks (with the items it waits on), Waiting on this (only when something does), Linked commits, Linked tests, Change log.
- One tile is focused: it fills the wide left column and is fully editable. The description is focused by default.
- The other tiles stack in the right column as compact previews: a count and their last three entries (next open subtasks, newest commits and notes, failing tests first). Clicking one focuses it, and the tiles slide to their new places.
- Small windows keep the number, title and back button on one row and use a smaller type scale; the description no longer shows blank lines between Markdown blocks.
- In narrow windows the page opens with every tile minimised; a tap expands one and a tap on its header minimises it.
- Locked feature chips have a dashed outline on a transparent background with normal text colour, readable in both themes.

## Capabilities

### Modified Capabilities
- `work-item-editor`: bento page layout with one focused tile; readable locked feature chips.

## Impact

- `TaskManager/Components/Pages/WorkItemPage.razor`, `Components/Ui.cs` (expand icon), `wwwroot/css/app.css`, `wwwroot/js/app.js` (tile FLIP).

<!-- merged from #wi41 Edit subtask text; a split subtask keeps it as the title -->
## Why

A subtask's text could not be changed once added: clicking it only selected it. Making a subtask into a work item, or into a dependency, also titled the new item "Split from #wi26", so the subtask's own text, which says what the work is, was only kept inside the new item's tasks.md and the board showed a card that said nothing about its content.

## What Changes

- Clicking a subtask's text edits it in place. Enter or clicking away saves it to tasks.md; Esc cancels. Only the text changes: the tick and number stay.
- Selecting subtasks for a split is Shift-click (or Ctrl/⌘-click), as cards on the board.
- One subtask made into a work item or a dependency: its text becomes the new item's title (and so its folder name).
- Several subtasks moved together keep the "Split from #wi<id>" title.

## Capabilities

### Modified Capabilities
- `work-item-editor`: subtask text is editable; subtask selection is Shift-click; the title of a work item split out of a single subtask.

## Impact

- `WorkItems/Commands.cs`, `WorkItems/WorkItemsApi.cs`, `WorkItems/BusinessRules/TasksFile.cs`, `TaskManager/Components/Pages/WorkItemPage.razor`, `wwwroot/js/app.js` (Esc), `wwwroot/css/app.css`.
