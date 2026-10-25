# Proposal

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
