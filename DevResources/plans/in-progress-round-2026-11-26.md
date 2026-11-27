# In-progress round: view parity, status rename, list header, Ayu editor, pane shortcuts (#wi226 #wi227 #wi229 #wi230 #wi231)

Status: done 2026-11-26 — chunks A–D staged; regression 1473/1475 passed (1 unrelated sync failure, 1 skipped); checked in the hidden app; In Review

## Why

The owner's five In Progress items of 2026-11-26:
- #226 any action available from the board view must be available in the other views (list, calendar); e.g. hiding a status in the list.
- #227 Ayu Mirage: the description editor's text is black on a dark background.
- #229 rename any status except the core ones (Backlog, In Progress, Completed), from any view.
- #230 list column header: the cursor shows drag on plain hover; filter/sort/hide controls are crammed; hide the column name on hover and spread the controls; show icons only on hover. Group rows: drop the `...` icon, open the options on a click of the group row header.
- #231 Ctrl+Shift+< toggles the left pane (side menu); Ctrl+Shift+> toggles a right pane (empty for now).

## What others do

- Linear / Notion / Jira: one action set per entity, reachable from every view's header context menu; group headers in lists open the same menu as board columns (right-click or click on the header label).
- AG Grid / Notion tables: header controls appear on hover, the label yields its room to them; the drag cursor shows only on the drag handle, not the whole header.
- VS Code: Ctrl+B toggles the primary side bar, Ctrl+Alt+B the secondary side bar; an empty secondary side bar is allowed.

## Decisions

- One group-options menu for board columns, list groups and calendar groups (same items: add item, select all, hide, merge, rename status, rename/delete feature, delete status); list group rows open it on a click of the group label, no `...` button.
- Status rename: core statuses Backlog, In Progress, Completed refused; renames the status in every item of the project, the project's status list and the board's saved options (hidden, merged, order keys).
- Ctrl+Shift+< is `Ctrl+Shift+Comma` (e.key "<"), Ctrl+Shift+> is `Ctrl+Shift+Period`; the right pane is an empty panel, collapsed by default, kept per window, not saved.

## Chunks

| Chunk | Items | Owns | Tests |
|---|---|---|---|
| A menus | #226, #229, #230 group row | BoardPage.razor / .razor.cs menus, BoardModel, WorkItems/Commands + WorkItemsApi (RenameStatus) | RenamingAStatusTests (new; data), menu parity in it; KeepingProjectStatusesTests |
| B column header | #230 header | list header markup in BoardPage.razor, its CSS, header drag cursor in app.js | none: a look, checked in the hidden app |
| C editor colour | #227 | description editor styles | none: a colour, checked in the hidden app in Ayu Mirage |
| D shortcuts | #231 | app.js keys, SideNav / MainLayout toggle, right panel | none: keys and layout, checked in the hidden app |

## Verification

Hidden desktop, CDP, 1600px, on the merged tree: list group menu items, rename of a custom status, list header hover, description editor in Ayu Mirage, both shortcuts; light, dark and Ayu Mirage shots.
