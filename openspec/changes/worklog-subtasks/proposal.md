# Proposal

## Why

Subtasks come up while writing in the worklog; they had to be retyped in Linked tasks.

## What Changes

- A day's Shift-click menu in the worklog has "Add subtask", which adds subtasks one after another from an input in the feed.
- When a note's editor closes, each unticked checkbox line (`- [ ] text`) not yet marked becomes a subtask, shown in the feed as added; the line stays in the note with "(created subtask)" appended, so it is made once. The note page and `board_note` do the same.
