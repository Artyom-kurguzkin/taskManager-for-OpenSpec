# Tasks

## 1. work item details ui patches · add contributors does not list other git users + Contributors fully editable

- [x] 1.1 Saved `x-rtm.contributors` (kept even when empty) replaces git commit authors; verify the "Commit author stays" and "Remove yourself" scenarios
- [x] 1.2 "+ add" accepts a typed name; verify the "Add someone new" scenario
- [x] 1.3 "+ add" lists every commit author in the repo (git log --all); verify the "Offer the repo's committers" scenario
- [x] 1.4 Merge name variants and case-different emails of one committer; verify the "Same person once" scenario

## 2. work item details ui patches · Declutter the work item and note page chrome + Work item page layout and folding

- [x] 2.1 Replace the breadcrumb bar with a `←` back button on the work item and note pages; drop the file path; verify the "Close a page" scenario
- [x] 2.2 Remove "Delete (hide)" from the work item page, keeping "Restore" for already-hidden items; verify the "Hide only from the board" scenario
- [x] 2.3 Stop drawing the icon and title in the window's title bar (taskbar keeps both); verify the "Bare title bar" scenario
- [x] 2.4 Compact-cards button in the collapsed side menu; verify the "Compact from the collapsed menu" scenario
- [x] 2.5 One tile per list: Description, Subtasks (with waits-on), Waiting on this (only when non-empty), Linked commits, Linked tests, Change log
- [x] 2.6 Focused tile in the wide left column, fully editable; description focused by default
- [x] 2.7 Compact tiles stacked in the right column: count plus the last three entries; clicking focuses it
- [x] 2.8 Slide tiles to their new places when focus changes (FLIP)
- [x] 2.9 Locked feature chips readable in the dark theme
- [x] 2.10 Verify the layout, focus switching and both themes in the real app
- [x] 2.11 Number, title and back button on one row at every width; smaller type and spacing at 760px and below; no pre-wrap blank lines in the description
- [x] 2.12 Verify at 1280, 600 and 420 pixels wide in the real app
- [x] 2.13 Narrow windows open with every tile minimised; a tap expands one, a tap on its header minimises it; desktop always keeps one open

## 3. work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Title

- [x] 3.1 Use a single moved subtask's text as the new item's title; keep "Split from #wi<id>" for several
- [x] 3.2 Cover "Make it a work item" and "Make it a dependency" with tests

## 4. work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Edit subtask text

- [x] 4.1 Rename a subtask in tasks.md, keeping its indent, box and number; refuse blank text
- [x] 4.2 Click a subtask's text to edit it in place: Enter or blur saves, Esc cancels (the global Esc handler leaves this1 field alone)
- [x] 4.3 Select subtasks with Shift/Ctrl/⌘-click instead of a plain click
- [x] 4.4 Verify edit, Enter, Esc, blur and Shift-click in the real app
