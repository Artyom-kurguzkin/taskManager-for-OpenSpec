# In Progress items, 18 Nov 2026 (third pass): four asks, four chunks

Status: planned 18 Nov 2026 after the afternoon's eight items went to In Review and the owner reviewed them in the 10:43 build of the app. Each chunk is independent, in its own worktree on its own CDP port. No diagram: contained UI changes.

## The asks

| Item | Ask | Evidence |
|---|---|---|
| #wi12 hider works wrong | "show all hidden" must unhide everything: the Hidden list clears; no grey-out; it overrides any other filter and shows everything | description, wi12 screenshot (a dashed hidden card in Confirmed, the "show all hidden" pill) |
| #wi160 (note #n793) | Settings: split into tabs General, Proj1, Proj2…; lay the settings out like a table | note text |
| #wi207 custom status | #n795 "I still can't delete statuses that I don't need" (on the 10:43 build, which holds today's work); subtask 1.4 "delete stuff from the viewers, not just from Settings" | notes, subtask |
| #wi205 merge (note #n798) | "I should be able to tell exactly where the subtasks and bugs are from, which merged work items (attachments too). Same for the feed" | wi205/n798 screenshot: the subtask group is headed "#wi203 delete metadata · merged 2026-11-18", but Related lists an image with no source and the feed's merged-in entries carry no source |

## Decisions (each also a decision note on its item)

1. **"Show all hidden" is an action, not a view** (#wi12). Pressing it restores every hidden item, column and project of the repository (the settings lists empty; legacy `x-rtm.hidden` flags without a resolution are cleared) and resets the Filters to their defaults (contributor everyone, period any time, commit any, changed any time), so the board shows everything. The dashed "(hidden)" look and the `ShowHidden` toggle go. Resolution-hidden items stay hidden: they are board state, restored from their own page.
2. **Settings are tabs** (#wi160 #n793): General (name, theme, plans/attachments defaults, agent guide, anything not per project) and one tab per open project (location, Plans, Images, Board storage, Statuses). Inside a tab every setting is one row of a two-column table: label left, control right, no cards and no frames; the tab choice is remembered per window.
3. **Categories are deleted where they are seen** (#wi207): the board's column header menu and the list's group header menu offer "Delete status…" for a non-default status and "Delete feature…" for a feature (the latter exists on the board already), with the same confirm Settings uses. And first: find why the owner still cannot delete a status in the real board.
4. **Merged-in content names its source** (#wi205 #n798): a note, a feed event and a Related file that came from a merged item carry "from #wiN" the way the subtask group heading does; Related groups its files under the source item when there is more than one source.

## Chunks

### hider-3 (#wi12), worktree chunk-ac, port 9433
Files: SideNavOptions.razor and SideNav.razor (the Hidden section, the "show all hidden" pill, line ~249), BoardModel.cs (`ShowHidden`), BoardPage.razor(.cs) (`ShowHidden` reads, the `is-hidden` marks), WorkItemCard.razor (`AppHidden`), app.css (`.is-hidden`, dashed rules), SettingsStore (`HiddenItems`, `HiddenProjects`, board `Hidden`), WorkItemsApi.Unhide for legacy flags, HidingInTheAppTests.
- The pill becomes "restore all hidden" (or keeps its label, the owner's words were "show all hidden"; keep "show all hidden" as the label, make it the action). One click: clear `HiddenItems`, the board's `Hidden` columns, `HiddenProjects` for the repository; clear legacy flags through `Unhide`; reset the filter options; one `Changed`. Remove `BoardOptions.ShowHidden` and every dashed/"(hidden)" rendering; the Hidden section lists what is hidden and the pill empties it.
- Tests first in `HidingInTheAppTests`: after hiding an item, a column and a project, one press shows all three again and the Hidden list is empty; filters are back at their defaults; a resolution-hidden item stays hidden; nothing is drawn dashed. Run only `HidingInTheAppTests|UsingTheBoardTests|SwitchingProjectsTests|SideMenuRenderBudgetTests|KeepingMyArrangementsTests`.
- CDP: hide a column, a project and an item, press the pill, screenshots before and after, light and dark `wi12-*`.

### settings-tabs (#wi160 #n793), worktree chunk-ad, port 9533
Files: TaskManager/Components/Pages/Settings/SettingsPage.razor, StatusesSection.razor, BoardStorageSection.razor, app.css; a `Seg`/tab component exists (TaskManager/Components/Shared/Seg.razor) and the item page's Worklog/Linked tasks switch shows the style to reuse; UiService for the remembered tab.
- Tabs across the top: General, then each open project by name. General holds everything not per project. A project tab holds: Location (path + Show in folder), Plans, Images, Board storage, Statuses. Each setting is a table row (label cell, control cell); the section descriptions become one short line under the tab title, not paragraphs beside each card.
- Tests first in `UsingThePagesTests`/`RememberingSettingsTests`: the page renders a General tab and one per project; choosing a project tab shows its rows; the chosen tab is remembered in the window; existing settings tests (ReadingProjectSettingsFromConfigTests, MovingAttachmentsTests, KeepingFilesInOneFolderTests, KeepingProjectStatusesTests UI tests) keep passing with selectors updated. Run only those classes.
- CDP: both tabs at 1280 and 1920, light and dark `wi160-settings-*`.

### delete-viewers (#wi207), worktree chunk-ae, port 9633
Files: BoardPage.razor (column header menu ~line 252 and 339 "Delete feature…", the list group header menu in `GroupRowRender`), BoardPage.razor.cs (`DeleteFeature`, a new `DeleteStatus` with the same confirm as StatusesSection line ~86), StatusesSection.razor, WorkItems/Commands.cs `RemoveStatus`.
- First the fact: `New-DemoRepo` copies the owner's real board; open Settings and try to delete each non-default status; find which one the owner cannot delete and why (a status with archived items only? a status named in another project's config? the confirm not appearing? the button missing for a custom status that is also in the defaults list?). Write the mechanism in a board note before fixing.
- Then decision 3: "Delete status…" on the board's status column header menu and the list's status group header menu, non-default only, same confirm and same `RemoveStatus`; "Delete feature…" also on the list's feature group header menu.
- Tests first in `KeepingProjectStatusesTests` and `UsingTheBoardTests`: the menu shows Delete status on a custom column and not on Backlog; confirming moves the items and removes the status; the list group header offers the same. Run only `KeepingProjectStatusesTests|UsingTheBoardTests|MovingGroupsInTheListTests|BrowsingFeaturesTests`.
- CDP on the real-board copy: delete a status from the board header menu and from the list group header, light and dark `wi207-*`; and the owner's failing case, before and after.

### provenance (#wi205 #n798), worktree chunk-af, port 9733
Files: WorkItemPage.razor (Related ~ the `Related` list, the subtask group heading ~120), Worklog.razor (~426 `wk-from` for notes with `MergedFrom`; events ~464 "merged-from"), WorkItems `CopyExtras`/`EventLog.Prepend` (whether merged-in events and files are tagged; add a source tag where the data has none: events get a trailing "from #wiN" marker the feed renders; attachments are recorded with their source in the note that carried them or in a small `mergedFrom` map), Notes module for `MergedFrom`.
- Every merged-in note (bug or not) shows "from #wiN" in the feed and in the Bugs filter; every merged-in event line shows "from #wiN"; Related lists files under their source ("from #wi200: pasted-….png") when the item has more than one source, flat otherwise. The subtask heading stays as it is.
- Tests first in `UsingThePagesTests` and `EditingWorkItemsTests`: after a merge of two items each with a note, an event and an attachment, the merged item's feed shows "from #wiN" on the source's note and events, Related shows the file under its source. Run only `UsingThePagesTests|EditingWorkItemsTests|UsingTheWorklogTests|KeepingNotesTests`.
- CDP: merge two demo items with notes, events and an image; open Worklog and Linked tasks; light and dark `wi205-prov-*`.

## Verification
Each chunk: named classes green, CDP light and dark. After integration: the full fast suite once and `tests/exploratory.ps1` once (52 of 52) by a merge agent; then the items go to In Review.

## Not in this plan
- #wi197 header gap: unreproduced; waits for the owner.
