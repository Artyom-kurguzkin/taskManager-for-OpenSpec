# Proposal

## Why

A git merge that both branches touched writes conflict markers into a board file (.openspec.yaml, tasks.md, a note), and the merge can be committed with them still in. Sync then pauses ("still has merge conflict markers") until someone edits the file by hand. The app already has a field-wise merge for these files; it should resolve them itself.

## What Changes

- When the app finds conflict markers in a board file, it rebuilds the file from the two sides of the conflict with the merge-base commit as the common ancestor, using the same field-wise merge as sync: lists combined, and for a field both sides changed, the side whose commit is newer wins.
- The repaired file is written to the working tree (never committed), and the item's worklog gets a conflict note naming each field, what was kept and what was dropped.
- Sync then goes ahead instead of pausing. A file that cannot be repaired (no recoverable sides, not a board file, unreadable YAML after the merge) still pauses sync as today.
