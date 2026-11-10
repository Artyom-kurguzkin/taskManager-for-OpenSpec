# Tasks

## 1. #wi34 see work item deltas · merged 2026-11-10

- [ ] 1.1 highlight uncommited changes
- [ ] 1.2 highlight chages for specific commit
- [ ] 1.3 diff between two selected commits

## 2. #wi92 Changelog UI issues · merged 2026-11-10

- [x] 2.1 Editable note title (`NotesApi.SetTitle`), blank refused
- [x] 2.2 Click in to edit, click outside to read; Edit/Done buttons removed
- [x] 2.3 Paste an image from the clipboard as an attachment (`NotesApi.AttachBytes`)

## 3. work item details rework · work item details rework · Events and documents (chunk A)

- [x] 3.1 `events.log` per work item, written by the commands (status, links, subtasks, dependencies, created), with the actor
- [x] 3.2 Linked documents in `x-rtm.docs`; notes can be created with a date of their own

## 4. work item details rework · work item details rework · The page (chunks C1, C2)

- [x] 4.1 Three views under the header; Description with the Related list; Linked tasks in three columns; tiles removed
- [x] 4.2 Worklog feed: days, notes and events, times, "Linked earlier" for links older than the log
- [x] 4.3 Shift-click menus: add note in place, link document / test / commit, Edit on a note, Unlink on a link
- [x] 4.4 Checked in the real app, light and dark, wide and narrow

## 5. work item details rework · work item details rework · Agents (chunk B)

- [x] 5.1 MCP: events carry the agent's name, `board_get` returns the worklog, `board_update` links documents, `board_note` takes an optional title
- [x] 5.2 Server instructions and REPO-TASKS.md v13: keep the worklog current, `events.log` format as the hand fallback

## 6. work item details rework · work item details rework · Corrections after review (9 Nov)

- [x] 6.1 The plan is shown, linked and unlinked in the Related list only; the header no longer has it; linked documents are in the list with a ×
- [x] 6.2 No frames around the description, the Related list, the linked-tasks columns and rows, or the worklog's notes; a line between entries

## 7. work item details rework · work item details rework · Description into the worklog, Related into Linked tasks (10 Nov)

- [x] 7.1 Description view removed: the description is the worklog's first entry, Shift-click → Edit changes it in place, paste still attaches images
- [x] 7.2 Worklog block in the side menu (filters, who, the five actions) while a worklog is open, first in split view
- [x] 7.3 Related moved into the Linked tasks view as its fourth column; two per row, then one, in a narrow window
- [x] 7.4 Checked in the real app, light and dark, wide and narrow
- [ ] 7.5 6.5 The description is the worklog's last entry (the item's first post) with no hover effect; Shift-click → Edit stays
- [ ] 7.6 6.6 Shift-click Unlink on a test: the menu is keyed to its row, so a feed change under an open menu no longer unlinks another test; Linked earlier rows de-duplicated
- [ ] 7.7 6.7 Days list in the WORKLOG block (newest first, entry counts, Linked earlier); a click unfolds the day and scrolls it into view
- [ ] 7.8 Still need to rework the Linked tasks
- [ ] 7.9 I want to be able to convert logs btw note - Decision - Subtask by selecting log entry (extra option on top of edit)
- [ ] 7.10 Still need to rework the Linked tasks (created subtask) - layout should be nicer
- [ ] 7.11 editing already created subtask within a feed created yet another subtask -bug
