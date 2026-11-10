# Tasks

## 1. #wi92 Changelog UI issues · merged 2026-11-10

- [x] 1.1 Editable note title (`NotesApi.SetTitle`), blank refused
- [x] 1.2 Click in to edit, click outside to read; Edit/Done buttons removed
- [x] 1.3 Paste an image from the clipboard as an attachment (`NotesApi.AttachBytes`)

## 2. work item details rework · Events and documents (chunk A)

- [x] 2.1 `events.log` per work item, written by the commands (status, links, subtasks, dependencies, created), with the actor
- [x] 2.2 Linked documents in `x-rtm.docs`; notes can be created with a date of their own

## 3. work item details rework · The page (chunks C1, C2)

- [x] 3.1 Three views under the header; Description with the Related list; Linked tasks in three columns; tiles removed
- [x] 3.2 Worklog feed: days, notes and events, times, "Linked earlier" for links older than the log
- [x] 3.3 Shift-click menus: add note in place, link document / test / commit, Edit on a note, Unlink on a link
- [x] 3.4 Checked in the real app, light and dark, wide and narrow

## 4. work item details rework · Agents (chunk B)

- [x] 4.1 MCP: events carry the agent's name, `board_get` returns the worklog, `board_update` links documents, `board_note` takes an optional title
- [x] 4.2 Server instructions and REPO-TASKS.md v13: keep the worklog current, `events.log` format as the hand fallback

## 5. work item details rework · Corrections after review (9 Nov)

- [x] 5.1 The plan is shown, linked and unlinked in the Related list only; the header no longer has it; linked documents are in the list with a ×
- [x] 5.2 No frames around the description, the Related list, the linked-tasks columns and rows, or the worklog's notes; a line between entries
