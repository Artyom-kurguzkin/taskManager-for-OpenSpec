# Tasks

## 1. select mode

- [x] 1.1 Selection: a 3px solid line in the text colour with a 7px yellow halo outside it, cards, subtasks, project headings and list rows alike; checked light and dark on the hidden desktop

## 2. minor UI patches · minor UI patches · Keyboard hints are a setting

- [x] 2.1 `AppSettings.Hints` (default off); a Settings row with the switch
- [x] 2.2 `rtm.setHints` pushed from the layout with the theme; the script ignores F while off and closes open badges
- [x] 2.3 Verify in the real app: F does nothing by default, works after the switch, stops again when switched off

## 3. minor UI patches · minor UI patches · minor UI patches · bucket view is unusable + Features, not buckets

- [x] 3.1 `BoardModel`: "feature" grouping is one flat section, Unassigned first; the saved "bucket" maps to "feature"
- [x] 3.2 `+ feature` at the end of the row; "Delete feature…" on every feature column
- [x] 3.3 `Features.DeleteFeature` removes a spec-backed feature's folder; refused while an open change has a delta for it or a nested feature lives inside
- [x] 3.4 Verify in the real app, light and dark
- [x] 3.5 `WorkItemsApi.MoveFeature`: any card, open or completed, drags to a feature, which becomes its only one (spec features unlinked, relinked on the way back)
- [x] 3.6 "Rename feature…": `WorkItemsApi.RenameFeature` moves the spec folder (or config entry), every delta folder and the x-rtm links; the prompt starts from the current name
- [x] 3.7 One feature per work item: hand-picked wins over specs, else the first spec; one "feature" dropdown on the item page; a Feature column in the list; SetManualFeatures/UnlinkFeature/RelinkFeature removed (MoveFeature does it all)

## 4. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · Do not repeat what the view is grouped by

- [x] 4.1 `BoardModel.GroupedColumn`; the list's `Shown` leaves it out
- [x] 4.2 Cards drop the label / contributors when the column already says it
- [x] 4.3 Verify in the real app under every grouping

## 5. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Cards

- [x] 5.1 Features as chips on expanded cards; status instead when grouped by bucket
- [x] 5.2 Drop the subtask count from expanded cards

## 6. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Spec features

- [x] 6.1 `x-rtm.unlinkedFeatures`: unlink and relink commands; an unlinked feature leaves the item's features
- [x] 6.2 Remove cross on every feature chip; "+ add" relinks an unlinked one
- [x] 6.3 Verify in the real app

## 7. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · List

- [x] 7.1 Features of a list row on one line: the first as a dashed chip, the rest as "+N", all in the tooltip

## 8. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Hidden columns

- [x] 8.1 Show how many items each hidden column holds in the side menu, following the filters and the work items / subtasks switch

## 9. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List rows drag like cards; cells select like a spreadsheet · Rows

- [x] 9.1 Grip per row; drag in front of a row or onto a group, with the board's handlers and animation
- [x] 9.2 Arrow keys move the selected rows (Shift-click selects, as on the board; no tick boxes)
- [x] 9.3 Arrow keys reach the page whatever has the focus
- [x] 9.4 Group chevrons collapse one group each; the compact switch shows or hides every subtask in the list

## 10. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List rows drag like cards; cells select like a spreadsheet · Cells

- [x] 10.1 Drag across cells or Shift-click to select a rectangle; Esc or a click outside clears it
- [x] 10.2 Ctrl+C copies the rectangle as tab-separated text
- [x] 10.3 Verify row drag, arrow keys, cell selection and copy in the real app

## 11. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List view on a data grid

- [x] 11.1 Radzen.Blazor package, services and script (its stylesheet is not loaded); GridRow model with flat sort/filter properties
- [x] 11.2 RadzenDataGrid with the app's cell templates, grouped by board column, empty columns kept
- [x] 11.3 Sorting, filtering from the header funnel, column resize and reorder by the header grip; settings remembered per repository
- [x] 11.4 Task-list look: frameless, 40px one-line rows, quiet sticky header, hover-revealed row controls, colour pills, one-line features, compact groups, resize handle on the column edge
- [x] 11.5 Hide a column from its header × into the side menu's Hidden list; no toolbar, filter row or frame
- [x] 11.6 Row drag, arrow keys, cell copy, subtask detail, subtasks button re-attached to the grid's rows
- [x] 11.7 Style the grid's markup with the app's own stylesheet (Radzen's not loaded), both themes
- [x] 11.8 Verify everything in the real app
- [x] 11.9 Group and project chevrons collapse (Expanded is only ever set to false; collapsed groups kept per project; projects share the board's setting)
- [x] 11.10 Subtasks button: subtasks start folded, selected shows them all (`BoardOptions.ShowSubtasks`); board compact cards unchanged

## 12. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Shift + hint letters selects the item

- [x] 12.1 Shift + badge letters dispatches a Shift-click and keeps the badges up
- [x] 12.2 Hint subtask texts
- [x] 12.3 Verify Shift-selecting two cards and a subtask in the real app

## 13. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Keyboard hints open with Ctrl+Alt + Keyboard hints open with F · Keyboard hints open with Ctrl+Alt

- [x] 13.1 Toggle hints on Ctrl+Alt instead of Alt; verify the "Toggle hints" and "Alt alone" scenarios

## 14. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Keyboard hints open with Ctrl+Alt + Keyboard hints open with F · Keyboard hints open with F

- [x] 14.1 Toggle hints with F outside text fields; drop Ctrl+Alt; take `f` out of the badge letters
- [x] 14.2 Verify in the real app: F on and off, F in a text field, badge letters

## 15. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · Saved toast after every write

- [x] 15.1 `UiService.Saved()`: skipped after a more specific toast, never twice at once
- [x] 15.2 `RepoPage.Run` and the note autosave call it
- [x] 15.3 Verify in the real app

## 16. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Reorder cards

- [x] 16.1 Place a dragged card at the target card's position and add the "my order" sort
- [x] 16.2 Send a card dropped on a column's empty space to the end when sorting by my order
- [x] 16.3 Verify dragging within a column in the real app

## 17. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Drop preview

- [x] 17.1 Insert at the gap (in front of the next card or column, or at the end) instead of taking the target's place
- [x] 17.2 Move every selected card when a selected card is dragged
- [x] 17.3 Open a sliding gap at the insertion point while dragging cards and column headers; dim what is dragged
- [x] 17.4 Click a card to open it; Shift-click (or Ctrl/⌘-click) to add or remove from the selection; remove the card tick boxes
- [x] 17.5 Verify click selection, the gap, card drops, multi-card drops and column drops in the real app
- [x] 17.6 Holding Shift shows the select-mode bar and marks cards as pickable before any click

## 18. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Arrow keys and smooth drag

- [x] 18.1 Up/down step the selection inside its column, gathering a scattered selection first; left/right move it to the neighbouring column
- [x] 18.2 Drag with pointer events: the real card follows, neighbours slide with transforms, Escape cancels; FLIP-animate the board after drops and arrow moves
- [x] 18.3 Verify arrow moves and the drag animation in the real app

## 19. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Reusing an archived folder name

- [x] 19.1 A file read earlier but since moved or deleted no longer blocks a write there (new "New work item" after archiving one)

## 20. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Archive feedback

- [x] 20.1 Run the OpenSpec CLI from npx's cached copy with node when present (saves ~0.85 s), else npx
- [x] 20.2 Show a busy panel with a spinner while archiving, from the board and the item page
- [x] 20.3 Verify in the real app: panel appears immediately, archive completes

## 21. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · add contributors does not list other git users + Contributors fully editable

- [x] 21.1 Saved `x-rtm.contributors` (kept even when empty) replaces git commit authors; verify the "Commit author stays" and "Remove yourself" scenarios
- [x] 21.2 "+ add" accepts a typed name; verify the "Add someone new" scenario
- [x] 21.3 "+ add" lists every commit author in the repo (git log --all); verify the "Offer the repo's committers" scenario
- [x] 21.4 Merge name variants and case-different emails of one committer; verify the "Same person once" scenario

## 22. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Declutter the work item and note page chrome + Work item page layout and folding

- [x] 22.1 Replace the breadcrumb bar with a `←` back button on the work item and note pages; drop the file path; verify the "Close a page" scenario
- [x] 22.2 Remove "Delete (hide)" from the work item page, keeping "Restore" for already-hidden items; verify the "Hide only from the board" scenario
- [x] 22.3 Stop drawing the icon and title in the window's title bar (taskbar keeps both); verify the "Bare title bar" scenario
- [x] 22.4 Compact-cards button in the collapsed side menu; verify the "Compact from the collapsed menu" scenario
- [x] 22.5 One tile per list: Description, Subtasks (with waits-on), Waiting on this (only when non-empty), Linked commits, Linked tests, Change log
- [x] 22.6 Focused tile in the wide left column, fully editable; description focused by default
- [x] 22.7 Compact tiles stacked in the right column: count plus the last three entries; clicking focuses it
- [x] 22.8 Slide tiles to their new places when focus changes (FLIP)
- [x] 22.9 Locked feature chips readable in the dark theme
- [x] 22.10 Verify the layout, focus switching and both themes in the real app
- [x] 22.11 Number, title and back button on one row at every width; smaller type and spacing at 760px and below; no pre-wrap blank lines in the description
- [x] 22.12 Verify at 1280, 600 and 420 pixels wide in the real app
- [x] 22.13 Narrow windows open with every tile minimised; a tap expands one, a tap on its header minimises it; desktop always keeps one open

## 23. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Title

- [x] 23.1 Use a single moved subtask's text as the new item's title; keep "Split from #wi<id>" for several
- [x] 23.2 Cover "Make it a work item" and "Make it a dependency" with tests

## 24. minor UI patches · minor UI patches · minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Edit subtask text

- [x] 24.1 Rename a subtask in tasks.md, keeping its indent, box and number; refuse blank text
- [x] 24.2 Click a subtask's text to edit it in place: Enter or blur saves, Esc cancels (the global Esc handler leaves this1 field alone)
- [x] 24.3 Select subtasks with Shift/Ctrl/⌘-click instead of a plain click
- [x] 24.4 Verify edit, Enter, Esc, blur and Shift-click in the real app

## 25. minor UI patches · agent in work item title

- [x] 25.1 Title row loses the agent chip; the agent is a contributor chip (review control while unreviewed)
- [x] 25.2 Guide: add yourself to contributors when you create something
