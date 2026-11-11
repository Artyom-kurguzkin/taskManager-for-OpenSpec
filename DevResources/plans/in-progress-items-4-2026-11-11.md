# In Progress items, round 4, 11 Nov 2026: feed conversions and bugs, table grid, note editor size, menu patch, time filter

- **Status:** done 2026-11-11 — all five chunks merged; #wi135, #wi133, #wi149, #wi148, #wi152 In Review; #wi146 stays In Progress for 1.1–1.3; 593 fast + 15 CLI tests green
- **Date:** 2026-11-11
- **Author:** Claude (agent)
- **Work items:** #wi135, #wi146, #wi133, #wi149, #wi148, #wi152

## Why

"work on the current In Progress work items". New since the last look, read from the worklogs:

- #wi135 (n235, bug): "converting note into subtask lost all of the note's contents; the event log overwrote the note log - instead leave the note and just add an event saying that note was promoted to a subtask".
- #wi146 (n236): "in addition to notes, decisions and tasks, add bugs category into the feed. I should be able to convert existing feed items into bug reports".
- #wi133 (n237): "I dont want to be editing tables within raw markdown editor. When cursor is within a table territory - overlay it with nicer editor - should be excel-like … select rows, columns etc. I like the ribbon you have, pretty much extend this further".
- #wi149 (n233): "in note editor … the note should stretch downwards instead of scrolling inside the feed ribbon. If it is taller than one screen height, add an expand icon."
- #wi148 (description, n227, n228): menu UX: no tickboxes (pills, underline when off), sort by first under Organise, "show all hidden" under the Hidden header, first row becomes a collapse icon, first section hidden in the work item context unless split, one "+ Add entry" dropdown.
- #wi152: "in addition to dates, i want to being able to specify changes by time within the day".

## What the industry does

- Conversions between entry types keep the original: GitHub converts a task-list item to an issue and keeps the line as a link; Linear's "create issue from comment" leaves the comment. Nothing is destroyed; a reference is added.
- Spreadsheet-style table editing over markdown: Obsidian's Advanced Tables and Typora's table mode (cell navigation, row/column operations, alignment, sort), with markdown as the stored form ([Advanced Tables](https://GitHub.com/tgrosinger/advanced-tables-obsidian)).
- Filter chips instead of checkboxes for on/off filters (Material's filter chips; Linear's and GitHub's filter pills): selected is filled, unselected is plain.

## Decisions

- **A conversion never destroys anything.** Promoting a note keeps the note and logs the promotion; converting a subtask keeps the subtask. Kind changes (note, decision, bug) are in place.
- **Bugs are a note kind** (`kind: bug`), with their own tag, accent and filter; every entry can become a bug, and an event offers "Report a bug from this".
- **Tables are edited in a grid overlay** laid over the table's lines while the caret is in it; markdown stays the source of truth; the ribbon grows (move, sort, select-based delete, "Edit as text").
- **The note editor grows with its text** up to one screen, then offers Expand to fill the window.
- **The menu has no checkboxes**: pill toggles; one "+ Add entry" menu; the first section shows only on the board and in split view.
- **Changed period takes times**: optional from/to time; null keeps whole days.
- Shared seam done in the main session first: `NoteKinds.Bug`, `WorklogFilters.Bugs`, `WorklogActions.AddBug`.

## Chunks

| Chunk | What | Owner | Test |
|---|---|---|---|
| feed | #wi135 conversions keep the original; #wi146 bugs category | sonnet, worktree, port 9510 | KeepingNotesTests, UsingTheWorklogTests; shots |
| table | #wi133 grid overlay and extended ribbon | sonnet, worktree, port 9511 | node tests; shots |
| editor | #wi149 note editor grows, expand | sonnet, worktree, port 9512 | UsingTheWorklogTests; measurements, shots |
| menu | #wi148 menu patch | sonnet, worktree, port 9513 | UsingTheBoardTests; shots |
| time | #wi152 times on the changed period | sonnet, worktree, port 9514 | ArrangingTheBoardTests, UsingTheBoardTests; shots |

## Verification

- Full fast suite and CLI suite green after integration (baseline 577).
- Hidden desktop, light and dark, each feature in the merged build.
- Items kept current through the tools; In Review when done.
