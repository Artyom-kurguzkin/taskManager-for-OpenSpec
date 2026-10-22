# Proposal

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
