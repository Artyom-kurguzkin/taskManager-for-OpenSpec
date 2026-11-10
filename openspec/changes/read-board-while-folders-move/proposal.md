# Proposal

## Why

Merging completed items showed an error dialog: a board refresh read a change folder the merge had just removed.

## What Changes

- A folder that is moved, deleted or written while the board is read is left out of that read; the file watcher's next reload picks it up where it went.
