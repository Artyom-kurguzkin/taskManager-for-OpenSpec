# In Progress items, 18 Nov 2026 (afternoon): seven asks, seven chunks

Status: planned 18 Nov 2026 after the morning's items went to In Review. Each chunk is independent, runs in its own worktree on its own CDP port, and is integrated by a three-way merge against HEAD. No diagram: no new concept, each is a contained fix or a small feature.

## The asks

| Item | Ask | Evidence |
|---|---|---|
| #wi177 attachments location | Settings: plans and images each get their own line with a typed path and a folder picker, and a way to move or point elsewhere; the "Put both in one folder" option goes | wi177 screenshot of the project card in Settings |
| #wi199 hider is broken | Hiding a column (Completed) or a project stopped working during a session; a restart fixed it | wi199 screenshot, notes #n712, #n726 |
| #wi201 hider and openspec | The item banner says "nothing was removed from openspec/": hiding must not touch openspec at all | wi201 screenshot of #wi9 |
| #wi200 custom status | With two projects open, a Research column showed for repo-task-manager, which has no such status; a move into it failed with "has no Research status" | wi200 screenshot, note #n721 |
| #wi202 page state | Coming back from an item, settings or analytics must land on the exact scroll spot the user left | description |
| #wi203 delete metadata | Delete non-default statuses and features; open items go to Uncategorised; archived items keep the old name; the merge rules in the description | description |
| #wi204 list moving (merge of #wi153 and #wi198) | A Shift-selected group header still cannot be dragged with the mouse | note #n737 |
| #wi205 merge work items | After a merge, the merged item shows none of the sources' subtasks and bug notes on the Linked tasks page | wi205 screenshot of #wi204 |

## Decisions (each also a decision note on its item)

1. **Hiding is the app's, never the repository's** (#wi201, #wi199). The eye in the side menu and "Delete (hide)" in the selection bar hide an item, a column or a project in the app's settings per repository (a `HiddenItems` list beside `HiddenProjects` and `BoardOptions.Hidden`), and write nothing under openspec/. The `x-rtm.hidden` flag stays only for an agent's `resolution` (cancelled, duplicate, superseded), which is board state, not a view choice; the item banner stops mentioning openspec.
2. **Statuses are per project** (#wi200). A project's section shows only the statuses its own config.yaml lists; the board's header row is the union in the saved order, and a section without a status shows no cell for it. A drop onto a status the project lacks asks "Add Research to <project>?" and adds it to that project's config on yes, instead of failing.
3. **Scroll and view state live in the app** (#wi202). Each scrolling view (board, list, calendar, analytics, settings, item page) remembers its scroll position and open panels per repository in the window's state, and restores them after its first render when the user comes back; not through browser history.
4. **Deleting a category** (#wi203). A non-default status or a feature can be deleted from where it is created (Settings). Open items in it move to the uncategorised value (no feature; the project's first status for a status). Archived items keep the deleted name. Merge rules: two archived items with the same deleted category merge; a deleted category merged with an existing one takes the existing; two different deleted categories refuse.
5. **A selected group drags like a board column** (#wi204). In the list, pressing a Shift-selected group header and moving drags it among the group headers; the drop reorders the board's column order, the same store the arrow keys use.

## Chunks

### attachments (#wi177), worktree chunk-u, port 9433
Files: TaskManager/Components/Pages/Settings/SettingsPage.razor (the project card: Location, Files), TaskManager/AppState/SettingsStore.cs (PlansDir/AttachmentsDir setters), Notes/NotesApi.ProjectFilesDir, the folder picker the app already has (grep `FolderPicker`, `OpenFolder`, Photino's `ShowOpenFolder`).
- Two rows per project: "Plans" and "Images", each a path input (relative to the repository or absolute), a "Choose folder" button opening the OS picker, and a "Move files" action that moves the existing files to the new folder (or "Point only" to just change the setting). Remove "Put both in one folder" and the one-folder `Files` input.
- Tests first: `ReadingProjectSettingsFromConfigTests` / `MovingAttachmentsTests` / `ChoosingWhereUserSettingsLiveTests`: setting each dir separately persists; move copies files and updates the setting; the Settings page renders two rows and no one-folder option. Run only those plus `UsingThePagesTests`.
- CDP: Settings page, light and dark, screenshots `wi177-settings-*`.

### hider (#wi199 + #wi201), worktree chunk-v, port 9533
Files: TaskManager/Components/Layout/SideNavOptions.razor and SideNav.razor (the Hidden section, the eyes, "show all hidden"), BoardPage.razor.cs (`Set`, `OnSettings`, the selection bar's Delete (hide)), BoardModel (`IsHidden`, `ToggleHidden`, `ShowHidden`), TaskManager/AppState/SettingsStore.cs, WorkItemPage.razor line ~42 (the banner), WorkItems hide command (grep `Hide(`/`SetHidden` in WorkItems/Commands.cs and WorkItemsApi).
- First the runtime bug: reproduce over CDP (hide Completed via the eye, hide a project, after a few other actions: arrow moves, a status change, opening an item) and find why the click stops taking effect. Hypotheses: `BoardPage.OnSettings` returns early when `o == O` because the side menu's `Set` and the page share state through `SaveBoardSoon` (chunk 1 of #wi195 debounces and fires `Changed` only when something other than `Order` changed); or `Load` skips re-projection when the reads are unchanged and a hidden-set change is not in the comparison (chunk 1b fixed the same for collapsed projects). Fix the root cause, with a bUnit test that hides a column after an arrow move and asserts it disappears.
- Then decision 1: item hide moves to settings (`HiddenItems` per repo, keyed like `HiddenProjects`); `ShowHidden` reads it; the Delete (hide) command no longer calls the WorkItems hide that writes `x-rtm.hidden`; the banner text becomes "Hidden in this app. Restore it from the side menu's Hidden section." Items with `x-rtm.hidden` from an agent resolution still show the resolution banner. Tests: hiding writes nothing under openspec (assert the yaml unchanged), the item is hidden in the app and listed under Hidden, restore works.
- Run only: `UsingTheBoardTests|SwitchingProjectsTests|KeepingMyArrangementsTests|UsingThePagesTests|RememberingSettingsTests`.
- CDP light and dark: `wi199-hide-{column,project,item}-*`.

### statuses (#wi200), worktree chunk-w, port 9633
Files: BoardModel.cs (`Build`, sections per project, `StatusesFor`), BoardPage.razor(.cs) (sections, `MoveInto`/`SetField` status path, the header row), WorkItems/Commands.cs ~190 (the "Unknown status" refusal), WorkItems/Queries.cs `GetStatuses`, the status editor in Settings (grep `AddStatus`/`statuses` in SettingsPage.razor and WorkItems/Commands.cs ~809-830).
- Decision 2. Tests first in `KeepingProjectStatusesTests` and `SwitchingProjectsTests`: two projects, one with Research: the other's section has no Research cell; the header shows Research once; a drop of the other's card onto Research asks and, on yes, adds Research to that project's config.yaml and moves the card; on no nothing changes. Run only those plus `UsingTheBoardTests|ArrangingTheBoardTests`.
- CDP: two demo repos open (see how OpeningSeparateWindowsTests or the hidden-app script opens a second repo), light and dark, `wi200-*`.

### scroll (#wi202), worktree chunk-x, port 9733
Files: TaskManager/wwwroot/js/app.js (`_reveal`, scroller lookup), TaskManager/Components/RepoPage.cs or a small `ViewState` in TaskManager/AppState (per window: view → scrollTop/scrollLeft, open fold states), BoardPage.razor.cs (`OnAfterRenderAsync` restore), WorkItemPage/Worklog/Settings/Analytics pages (save on leave, restore on return), MainLayout/NavigationManager `LocationChanged`.
- Decision 3. On leaving a view (navigation away) record the scroller's position through JS; on return restore it after the first render of that view, once the content is tall enough (retry on the next render if not). Keys: window + repository + view. Tests first in `UsingThePagesTests`/`PagesRenderBudgetTests` with a JS interop stub: leaving the board at scrollTop 1200 and coming back restores 1200; a different repository restores its own. Run only `UsingThePagesTests|PagesRenderBudgetTests|UsingTheBoardTests|SwitchingProjectsTests`.
- CDP: scroll the list to a row, open an item, go back, read scrollTop before and after; same for settings and analytics; light and dark `wi202-*`.

### delete-meta (#wi203), worktree chunk-y, port 9833
Files: the status editor and feature editor in Settings (SettingsPage.razor; WorkItems/Commands.cs ~809-830 `CountWithStatus`, rename/remove statuses; Features/Commands.cs), WorkItems merge rules (Commands.cs `MergeSteps`: only add a category check at the top; another chunk edits what the merge carries, do not touch CopyExtras or the notes part), WorkItemsApi, the board's Uncategorised handling.
- Decision 4. Tests first in `KeepingProjectStatusesTests`, `EditingWorkItemsTests` (features) and `MergingBoardFilesTests` or the merge tests (grep `Merge(` in TaskManager.Tests): delete a custom status with open items → they get the project's first status (or "Uncategorised" if that is how the board names no-status; follow what `Unassigned` does for features), archived items keep the name and still show under it when ShowHidden/archived view is on; a default status cannot be deleted; delete a feature → open items lose it, archived keep it; merge rules three cases. Run only those classes plus `UsingTheBoardTests`.
- CDP: Settings, delete a custom status and a feature, light and dark `wi203-*`.

### group-drag (#wi204), worktree chunk-z, port 9933
Files: TaskManager/wwwroot/js/app.js (`_startDrag`, `_rowAt`, `_groupSelected`, `_findSpot`, the column drag kind `col`), BoardPage.razor.cs (`draggingColumn`, `DropColumn`, the list's `GroupRowRender` attributes), app.css for the lifted group header.
- Decision 5. Tests first in `MovingGroupsInTheListTests`: a selected group header carries the drag attributes; dropping a group above another reorders the board's column order (drive the Blazor drop handler as the board column drop tests do, grep `DropColumn` in UsingTheBoardTests). Run only `MovingGroupsInTheListTests|MovingRowsWithArrowKeysTests|UsingTheBoardTests`.
- CDP: used-board preset, Shift-click Planned, drag it above Backlog, read the group order; switch to the board, columns follow; light and dark `wi204-group-drag-*`; then `tests/exploratory.ps1 -Port 9933` once, 52 of 52.

### merge (#wi205), worktree chunk-aa, port 10033
Files: WorkItems/Commands.cs `MergeSteps`, `CopyExtras`, `KeepLinks`, `TasksFile.CreateGrouped`; the merge UI path in BoardPage (the selection bar's merge) and the Linked tasks view (WorkItemPage.razor: Subtasks tile, Related).
- First the facts: the merged folder `openspec/changes/merge-wi153-wi198/` in MAIN holds the sources' `events.log` lines (EventLog.Prepend): read it to see what #wi198 had (subtask-added, notes, attachments) and compare with what the folder holds now (tasks.md, notes/). Then fix what was dropped: subtasks of every source as groups headed "#wiN Title · merged date" (the code intends this; find why `subtasksTotal` is 0), bug notes and their attachments under `local/task-tracking/wi<merged>/`, linked tests and docs. The Linked tasks page must list the merged subtasks and the Worklog's Bugs filter the merged bug notes.
- Tests first in the merge tests (grep `Merge(` in TaskManager.Tests; `MergingBoardFilesTests` is board sync, find the work-item merge class): a merge of two items with subtasks, a bug note with an attachment and a linked test yields all of them on the merged item. Run only that class plus `EditingWorkItemsTests|KeepingNotesTests|UsingThePagesTests`.
- CDP: merge two demo items with subtasks and a bug note, open the result's Linked tasks and Worklog, light and dark `wi205-*`.

## Verification
Each chunk: named classes green, CDP screenshots light and dark. After integration: the full fast suite once and `tests/exploratory.ps1` once (52 of 52) by a merge agent; then the items go to In Review.

## Not in this plan
- #wi197 header gap: unreproduced on the current build; waits for the owner.
