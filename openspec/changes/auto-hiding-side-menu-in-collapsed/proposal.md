# Proposal

## Why

In collapsed view the icon rail still takes a permanent gutter. The owner wants the collapsed menu to hide away completely and appear when the pointer goes near the left edge, so the page gets the full width.

## What Changes

- Collapsed: the rail slides off the left edge and the page takes the whole width.
- A thin hot zone along the left edge (about 8 px) reveals the rail as an overlay; it hides again about 400 ms after the pointer leaves.
- The rail stays shown while it is in use: focus inside, an open popup, a drag in progress.
- Expanded view is unchanged.
