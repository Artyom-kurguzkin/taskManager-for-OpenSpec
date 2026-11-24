# Proposal

## Why

The only split today is a work item beside the list, chosen from the item's menu. The owner wants to split any view: Alt+\ splits the active window into panes, Alt+Win+\ opens a new window, still in the same process and served by the single MCP endpoint.

## What Changes

- Alt+\ splits the active pane of the window; each pane shows any page with its own address (up to 4, side by side); Alt+Shift+\ or the pane's close button closes it
- The side menu acts on the active pane; opening a work item from a list in a split opens it in the neighbouring pane
- The work item's "Beside the list / Item only" choice and the Split setting go; "Separate window" stays
- Alt+Win+\ opens a new full window of the app (side menu, its own panes) in the same process; agents keep the one MCP endpoint
