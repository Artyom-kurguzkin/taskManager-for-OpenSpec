# Proposal

## Why

Using the first build showed that the app was slow and large, that the header took too much room and broke on small windows, and that the feature view stacked Functional / Non-Functional as a level of hierarchy. That made grouping confusing. Contributor names also drifted from git identities (one person showed up twice), and the app had no icon.

## What Changes

- **Speed and size:** module reads are cached per content version and git HEAD. The layout no longer redraws on every change. MudBlazor is replaced by a few small own components (menus, dialogs, toasts). Publishing writes a compressed self-contained `dist/TaskManager.exe` (~37 MB) and a framework-dependent `dist/TaskManager-small.exe` (~3.5 MB).
- **App shell:**
  - The top header becomes a left side menu: an icon rail with ☰, a Board/List dropdown, "+ new work item" and Settings.
  - ☰ expands the menu to labels, the view options and a list of hidden columns.
  - On narrow windows the same menu becomes a top header bar, and the board starts at the very top.
- **Grouping model:**
  - "Group by" picks a category: status, feature or contributor.
  - Each category's columns are its groups, and subgroups (lanes) divide a column.
  - Functional / Non-Functional only filters which features are shown, one side at a time.
  - Work items without a feature or contributor get their own column.
- **Columns:**
  - Drag a column header to reorder.
  - "Hide column" in the header menu; hidden columns are listed in the side menu to bring back.
  - Features added from the board can be deleted, and their work items become Unassigned.
- **Contributors from git:** a work item's contributors are the commit authors of its change folder plus the people assigned in the app, so a newcomer with no commits can still assign themselves.
- **Look:**
  - An app icon drawn from Artyom's signature, with an inverted version for the dark theme.
  - Thin scrollbars in the theme's colours.
  - Tighter spacing throughout.

## Capabilities

### New Capabilities
- `app-shell`: side menu / header bar, small-window layout, window icon and native colours.

### Modified Capabilities
- `board`: grouping model, feature side filter, column reorder/hide, unassigned columns, feature create/delete.
- `workspace`: contributors from git; features added from the board.
- `work-item-editor`: commit authors are read-only contributors.

## Impact

- Code: TaskManager (layout, side menu, board page, services), Features, WorkItems and GitHistory modules.
- Data: `config.yaml` `x-rtm.features` entries can now be removed from the app. View settings (column order, hidden columns, side) stay in the user's settings file, not the repo.
