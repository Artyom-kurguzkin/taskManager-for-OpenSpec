# Tasks

## 1. Keyboard hints are a setting

- [x] 1.1 `AppSettings.Hints` (default off); a Settings row with the switch
- [x] 1.2 `rtm.setHints` pushed from the layout with the theme; the script ignores F while off and closes open badges
- [x] 1.3 Verify in the real app: F does nothing by default, works after the switch, stops again when switched off

## 2. minor UI patches · bucket view is unusable + Features, not buckets

- [x] 2.1 `BoardModel`: "feature" grouping is one flat section, Unassigned first; the saved "bucket" maps to "feature"
- [x] 2.2 `+ feature` at the end of the row; "Delete feature…" on every feature column
- [x] 2.3 `Features.DeleteFeature` removes a spec-backed feature's folder; refused while an open change has a delta for it or a nested feature lives inside
- [x] 2.4 Verify in the real app, light and dark
- [x] 2.5 `WorkItemsApi.MoveFeature`: any card, open or completed, drags to a feature, which becomes its only one (spec features unlinked, relinked on the way back)
- [x] 2.6 "Rename feature…": `WorkItemsApi.RenameFeature` moves the spec folder (or config entry), every delta folder and the x-rtm links; the prompt starts from the current name
- [x] 2.7 One feature per work item: hand-picked wins over specs, else the first spec; one "feature" dropdown on the item page; a Feature column in the list; SetManualFeatures/UnlinkFeature/RelinkFeature removed (MoveFeature does it all)

## 3. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · Do not repeat what the view is grouped by

- [x] 3.1 `BoardModel.GroupedColumn`; the list's `Shown` leaves it out
- [x] 3.2 Cards drop the label / contributors when the column already says it
- [x] 3.3 Verify in the real app under every grouping

## 4. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Cards

- [x] 4.1 Features as chips on expanded cards; status instead when grouped by bucket
- [x] 4.2 Drop the subtask count from expanded cards

## 5. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Spec features

- [x] 5.1 `x-rtm.unlinkedFeatures`: unlink and relink commands; an unlinked feature leaves the item's features
- [x] 5.2 Remove cross on every feature chip; "+ add" relinks an unlinked one
- [x] 5.3 Verify in the real app

## 6. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · List

- [x] 6.1 Features of a list row on one line: the first as a dashed chip, the rest as "+N", all in the tooltip

## 7. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · Feature chips on cards, and removable spec features · Hidden columns

- [x] 7.1 Show how many items each hidden column holds in the side menu, following the filters and the work items / subtasks switch

## 8. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List rows drag like cards; cells select like a spreadsheet · Rows

- [x] 8.1 Grip per row; drag in front of a row or onto a group, with the board's handlers and animation
- [x] 8.2 Arrow keys move the selected rows (Shift-click selects, as on the board; no tick boxes)
- [x] 8.3 Arrow keys reach the page whatever has the focus
- [x] 8.4 Group chevrons collapse one group each; the compact switch shows or hides every subtask in the list

## 9. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List rows drag like cards; cells select like a spreadsheet · Cells

- [x] 9.1 Drag across cells or Shift-click to select a rectangle; Esc or a click outside clears it
- [x] 9.2 Ctrl+C copies the rectangle as tab-separated text
- [x] 9.3 Verify row drag, arrow keys, cell selection and copy in the real app

## 10. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · List view UX · List view UX · List view on a data grid

- [x] 10.1 Radzen.Blazor package, services and script (its stylesheet is not loaded); GridRow model with flat sort/filter properties
- [x] 10.2 RadzenDataGrid with the app's cell templates, grouped by board column, empty columns kept
- [x] 10.3 Sorting, filtering from the header funnel, column resize and reorder by the header grip; settings remembered per repository
- [x] 10.4 Task-list look: frameless, 40px one-line rows, quiet sticky header, hover-revealed row controls, colour pills, one-line features, compact groups, resize handle on the column edge
- [x] 10.5 Hide a column from its header × into the side menu's Hidden list; no toolbar, filter row or frame
- [x] 10.6 Row drag, arrow keys, cell copy, subtask detail, subtasks button re-attached to the grid's rows
- [x] 10.7 Style the grid's markup with the app's own stylesheet (Radzen's not loaded), both themes
- [x] 10.8 Verify everything in the real app
- [x] 10.9 Group and project chevrons collapse (Expanded is only ever set to false; collapsed groups kept per project; projects share the board's setting)
- [x] 10.10 Subtasks button: subtasks start folded, selected shows them all (`BoardOptions.ShowSubtasks`); board compact cards unchanged

## 11. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Shift + hint letters selects the item

- [x] 11.1 Shift + badge letters dispatches a Shift-click and keeps the badges up
- [x] 11.2 Hint subtask texts
- [x] 11.3 Verify Shift-selecting two cards and a subtask in the real app

## 12. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Keyboard hints open with Ctrl+Alt + Keyboard hints open with F · Keyboard hints open with Ctrl+Alt

- [x] 12.1 Toggle hints on Ctrl+Alt instead of Alt; verify the "Toggle hints" and "Alt alone" scenarios

## 13. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · navigation hotkeys patches · Keyboard hints open with Ctrl+Alt + Keyboard hints open with F · Keyboard hints open with F

- [x] 13.1 Toggle hints with F outside text fields; drop Ctrl+Alt; take `f` out of the badge letters
- [x] 13.2 Verify in the real app: F on and off, F in a text field, badge letters

## 14. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · Saved toast after every write

- [x] 14.1 `UiService.Saved()`: skipped after a more specific toast, never twice at once
- [x] 14.2 `RepoPage.Run` and the note autosave call it
- [x] 14.3 Verify in the real app

## 15. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Reorder cards

- [x] 15.1 Place a dragged card at the target card's position and add the "my order" sort
- [x] 15.2 Send a card dropped on a column's empty space to the end when sorting by my order
- [x] 15.3 Verify dragging within a column in the real app

## 16. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Drop preview

- [x] 16.1 Insert at the gap (in front of the next card or column, or at the end) instead of taking the target's place
- [x] 16.2 Move every selected card when a selected card is dragged
- [x] 16.3 Open a sliding gap at the insertion point while dragging cards and column headers; dim what is dragged
- [x] 16.4 Click a card to open it; Shift-click (or Ctrl/⌘-click) to add or remove from the selection; remove the card tick boxes
- [x] 16.5 Verify click selection, the gap, card drops, multi-card drops and column drops in the real app
- [x] 16.6 Holding Shift shows the select-mode bar and marks cards as pickable before any click

## 17. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Arrow keys and smooth drag

- [x] 17.1 Up/down step the selection inside its column, gathering a scattered selection first; left/right move it to the neighbouring column
- [x] 17.2 Drag with pointer events: the real card follows, neighbours slide with transforms, Escape cancels; FLIP-animate the board after drops and arrow moves
- [x] 17.3 Verify arrow moves and the drag animation in the real app

## 18. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Reusing an archived folder name

- [x] 18.1 A file read earlier but since moved or deleted no longer blocks a write there (new "New work item" after archiving one)

## 19. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · board UI patches · Reorder cards within a column · Archive feedback

- [x] 19.1 Run the OpenSpec CLI from npx's cached copy with node when present (saves ~0.85 s), else npx
- [x] 19.2 Show a busy panel with a spinner while archiving, from the board and the item page
- [x] 19.3 Verify in the real app: panel appears immediately, archive completes

## 20. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · add contributors does not list other git users + Contributors fully editable

- [x] 20.1 Saved `x-rtm.contributors` (kept even when empty) replaces git commit authors; verify the "Commit author stays" and "Remove yourself" scenarios
- [x] 20.2 "+ add" accepts a typed name; verify the "Add someone new" scenario
- [x] 20.3 "+ add" lists every commit author in the repo (git log --all); verify the "Offer the repo's committers" scenario
- [x] 20.4 Merge name variants and case-different emails of one committer; verify the "Same person once" scenario

## 21. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Declutter the work item and note page chrome + Work item page layout and folding

- [x] 21.1 Replace the breadcrumb bar with a `←` back button on the work item and note pages; drop the file path; verify the "Close a page" scenario
- [x] 21.2 Remove "Delete (hide)" from the work item page, keeping "Restore" for already-hidden items; verify the "Hide only from the board" scenario
- [x] 21.3 Stop drawing the icon and title in the window's title bar (taskbar keeps both); verify the "Bare title bar" scenario
- [x] 21.4 Compact-cards button in the collapsed side menu; verify the "Compact from the collapsed menu" scenario
- [x] 21.5 One tile per list: Description, Subtasks (with waits-on), Waiting on this (only when non-empty), Linked commits, Linked tests, Change log
- [x] 21.6 Focused tile in the wide left column, fully editable; description focused by default
- [x] 21.7 Compact tiles stacked in the right column: count plus the last three entries; clicking focuses it
- [x] 21.8 Slide tiles to their new places when focus changes (FLIP)
- [x] 21.9 Locked feature chips readable in the dark theme
- [x] 21.10 Verify the layout, focus switching and both themes in the real app
- [x] 21.11 Number, title and back button on one row at every width; smaller type and spacing at 760px and below; no pre-wrap blank lines in the description
- [x] 21.12 Verify at 1280, 600 and 420 pixels wide in the real app
- [x] 21.13 Narrow windows open with every tile minimised; a tap expands one, a tap on its header minimises it; desktop always keeps one open

## 22. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Title

- [x] 22.1 Use a single moved subtask's text as the new item's title; keep "Split from #wi<id>" for several
- [x] 22.2 Cover "Make it a work item" and "Make it a dependency" with tests

## 23. minor UI patches · Do not repeat what the view is grouped by + List view UX + UI patches · List view UX + UI patches · UI patches · UI patches · UI patches · item details ui patches · work item details ui patches · Edit subtask text; a split subtask keeps it as the title · Edit subtask text

- [x] 23.1 Rename a subtask in tasks.md, keeping its indent, box and number; refuse blank text
- [x] 23.2 Click a subtask's text to edit it in place: Enter or blur saves, Esc cancels (the global Esc handler leaves this1 field alone)
- [x] 23.3 Select subtasks with Shift/Ctrl/⌘-click instead of a plain click
- [x] 23.4 Verify edit, Enter, Esc, blur and Shift-click in the real app
