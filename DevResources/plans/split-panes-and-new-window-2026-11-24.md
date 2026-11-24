# Split panes in a window and a new full window (#wi223)

Status: done 2026-11-24 — A, C and A2 staged; regression 1467/1468 passed (1 skipped); In Review

## Why

The only split is a work item beside the list (`Settings.Split`, the item page's "Beside the list / Item only" menu). The owner wants:
Alt+\ splits the active window into panes, Alt+Win+\ opens a new window; same process, one MCP endpoint.

## What others do

- VS Code editor groups: a split opens the same editor beside the current one; the active group has the focus and takes commands; Ctrl+W closes. https://code.visualstudio.com/docs/getstarted/userinterface#_side-by-side-editing
- Windows Terminal panes: Alt+Shift+= / Alt+Shift+- split the focused pane, the new pane gets the focus, Ctrl+Shift+W closes it. https://learn.microsoft.com/windows/terminal/panes
- VS Code "New Window" (Ctrl+Shift+N): one more window of the same process family, its own layout.

## Decisions

- A pane is an address (`""`, `item/5`, `note/3`, `settings`, `doc/...`) rendered by its own route lookup, not the window's router.
- Alt+\ splits the active pane to the right; the new pane shows the same address and becomes active. Up to 4 panes, equal widths. Ctrl+W closes the active pane (owner); with one pane it does nothing (never closes the window).
- One side menu per window, not one per pane (owner): it acts on the active pane and shows which one it is in ("Tab 1", "Tab 2", ...). A separate window has its own full side menu.
- The window's address (NavigationManager) follows the active pane, so the side menu and existing code that reads `Nav.Uri` keep working; a navigation through `Nav` lands in the active pane. A pointer-down in a pane makes it active first.
- Opening a work item from a board/list pane while split opens it in the neighbouring pane (the right one, or the left from the last pane): the old list-beside-item use stays one click.
- Board view options stay shared (settings); two board panes show the same view. Pane layout is not kept across restarts.
- Alt+Win+\ opens a full window (not the detached item window) through `MainWindowLauncher` (contract in `AppState/MainWindowLauncher.cs`).

## Chunks

| Chunk | Files | Tests |
|---|---|---|
| A panes | AppState/Panes.cs (new), AppState/UiService.cs, Components/Layout/MainLayout.razor, Components/Layout/PaneHost.razor (new), Components/Layout/SideNav.razor, SideNavOptions.razor, Pages/Board/BoardPage.razor.cs, Pages/WorkItem/WorkItemPage.razor, AppState/SettingsStore.cs (Split goes), wwwroot/js/app.js, wwwroot/css/app.css | SplittingPanesTests (new) + touched classes |
| C window | AppState/AppMode.cs, Program.cs | OpeningANewWindowTests (new) |

## Verification

Hidden desktop, CDP: Alt+\ twice, open an item from the left pane, close a pane, light and dark shots; Alt+Win+\ opens a second window (CDP target list), an agent call still answers on the one port.
