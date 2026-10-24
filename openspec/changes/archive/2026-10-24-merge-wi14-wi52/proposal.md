# Proposal

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
