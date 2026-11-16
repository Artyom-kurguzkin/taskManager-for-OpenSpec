# Proposal

Feed view

## Why

The item page showed six tiles of which only the focused one could be used; the rest were three-line previews. The history of an item was spread over the change log, the commits tile and the tests tile, and nothing recorded what happened to it between notes. Agents had no simple way to leave a trace of what they did.

## What Changes

- Under the unchanged header the page has three views, all three buttons always shown: **Description**, **Worklog**, **Linked tasks**.
- **Description** shows the description and, beside it, a list of everything attached to the item: the plan, linked documents, attachments and images.
- **Linked tasks** shows subtasks, what the item waits on and what waits on it in three columns.
- **Worklog** is one feed grouped by day, newest first, of notes and events, each with the time it was created. Shift-clicking a date opens a menu to add a note, link a document, link a test or link a commit; Shift-clicking a note offers Edit, and a linked commit, test or document offers Unlink.
- Events are recorded from now on in `events.log` in the change folder: created, status changed, commit / test / document linked or unlinked, subtask ticked, reopened or added, dependency added or removed, each with who did it.
- Documents are linked to an item in `x-rtm.docs`.
- The MCP tools log the calling agent's changes as events, return the worklog from `board_get`, link documents and write a worklog entry in one call; the generated guide tells agents to keep the worklog current.
- The tiles, tile focus, the linked commits tile and the linked tests tile are gone.

Plan: `DevResources/plans/work-item-details-rework-2026-11-09.md`.
