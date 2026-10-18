# Proposal

## Why

The board is the app's home screen: the place to see what is in flight across the repo and to move work along. It replaces scrolling through `openspec/changes/` by hand.

## What Changes

- Board grouped by status, feature (two levels: feature group, then feature) or contributor.
- Sort (including by feature), contributor filter, and two views: Board (optionally compact cards, or subtasks as their own cards) and List (a sortable, grouped table with expandable rows).
- A switcher to show one feature group at a time.
- Drag a card to another column to change its status, feature or contributor. Dropping on Completed archives the change.
- Custom sub-categories (lanes) inside any column, added and removed from the column header.
- Column header menu: add a work item there, select all, add or remove sub-categories.
- Feature groups can get a new sub-group (a new capability).
- Multi-select cards and merge them into one work item.

## Capabilities

### New Capabilities
- `board`: grouping, sorting, filtering and showing work items, and the bulk actions (move, merge) done from that view.

### Modified Capabilities
- none

## Impact

- Depends on `add-workspace` for the index and writes.
- Archiving needs the `openspec` CLI on PATH.
- Merging deletes the source change folders. Git keeps them, and the merged item's change log names the sources.
