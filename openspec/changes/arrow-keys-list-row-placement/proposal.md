# Proposal

## Why

In the board's list view, pressing ArrowUp on a selected row should swap it with the row shown directly above it. Instead it jumps over two rows and lands in the high-priority group. The step is computed from the column's internal order, not the order the list shows, and the first arrow press switches the sort to manual with an order that covers only the moved keys, so the other rows reshuffle too.

## What Changes

- `BoardModel.Step` steps relative to the order the rows are shown in (grouping and current sort), not the column's internal list.
- Switching to the manual sort seeds the saved order with the whole shown order, so only the moved row changes place.
- bUnit tests for up/down in list and board views with mixed priorities; verified over CDP on the hidden app in light and dark.
