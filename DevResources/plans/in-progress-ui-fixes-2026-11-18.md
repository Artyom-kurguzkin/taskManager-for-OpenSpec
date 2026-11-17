# In Progress items, 18 Nov 2026: the owner's new asks

Status: planned 18 Nov 2026. Five asks across four In Progress items, worked as three chunks in parallel worktrees. No diagram: each chunk is a contained UI fix with no new concept.

## The asks (from the worklogs, all from the owner on 17 Nov)

| Item | Ask | Evidence |
|---|---|---|
| #wi148 patch menu | #n693: the value pills in the side menu (sort by, groups, subgroups, colour, contributor, period, commit) start at different x positions; align them in one column, and everything must fit the menu width | wi148/n693 screenshot, red line through the pills |
| #wi148 patch menu | subtask 1.3 (#n698): the worklog's "+ Add entry" should be a red filled button like "+ New work item" | wi148/n698 screenshot |
| #wi176 git deltas | #n697: the commit selector's dropdown opens page-wide with every commit on one long line; it must be a dropdown sized to the menu, subjects truncated, scrollable | wi176/n697 screenshot |
| #wi153 list moving | #n700: groups in the list cannot be selected and moved; sorting by a column rearranges the groups | note text |
| #wi156 dates display | subtask 1.2 (#n699): the card's "16:15. 12 Nov 26", "11/12" and "11 Nov" carry no meaning; annotate them | wi156/n699 screenshot |

## Chunks

### Chunk menu (#wi148 + #wi176), worktree chunk-p, port 9433
- SideNavOptions.razor and app.css: every "label: pill" row in Organise and Filters is a two-column grid (label column of one fixed width, pill column), so the pills share one left edge; long values ellipsis inside the pill; nothing overflows the 240 px menu. The changed-period pill and the legend fit too.
- SideNav.razor `nav-add` (the worklog's Add entry): the same red filled style as the New work item button (`nav-new` or whatever class it uses), same height.
- The commit selector (SideNavOptions, Dropdown/PickerMenu): popup width equals the pill's or the menu's width, each row "hash subject · day" on one line with ellipsis, max height about 12 rows with a scroll; the chosen label in the pill ellipsised (already) and the popup no longer page-wide.
- Tests: `ListPillsTests`/`UsingTheBoardTests` or the menu's own test class (find `SideNavOptions` tests) for: pills share a grid cell class; Add entry carries the red class; the commit popup carries its width class. CDP: menu at 1280 and 1920 wide, light and dark, the commit popup open.

### Chunk groups (#wi153), worktree chunk-q, port 9533
- Group headers in the list (the status/label/feature group rows the grid draws) can be Shift-clicked to select, like swimlane headers on the board (`selectedRows`, `MoveRows`, `StepKeys`); Up/Down moves the selected group among its siblings; the order is saved as the board's column order (`BoardModel.ColumnKey`/`Order`), the same store the board's columns use, so the list and the board agree.
- Sorting by a column (the grid's header sort) sorts rows inside each group only; the groups keep their saved order. Today Radzen re-sorts the groups when a sort is on; find where the group descriptors or the group key ordering are built in BoardPage (`groups`, `groupedSwim`, `Rows`) and keep the group sequence from BoardModel regardless of the row sort.
- Tests first in `MovingRowsWithArrowKeysTests` or `UsingTheBoardTests`: Shift-click a group header selects it; ArrowUp swaps it with the group above and the board's column order follows; a header sort leaves the group order unchanged. CDP on the used-board preset: select "Planned", press Up, screenshot; click the Updated header, group order unchanged.

### Chunk card (#wi156), worktree chunk-r, port 9633
- WorkItemCard.razor meta row: each number gets its meaning without a frame: the updated stamp prefixed by a small muted "updated" (or a clock glyph with a title), the subtask count as "11/12 subtasks" or "11 of 12", the due/period pill prefixed "due" when it is only a due day (it already reads "due 16 Nov" for due-only; the screenshot shows a bare "11 Nov", which is the period pill for a start day only or a one-day period: make "from 9 Nov" / "due 16 Nov" / "9–16 Nov" always say what they are, a one-day period reads "due 11 Nov"). Tooltips on each (title attributes) with the long form.
- Keep the card compact: one line, muted captions in the small type the stamp uses; no boxes.
- Tests first in `UsingTheBoardTests` (the card tests): the meta row's text reads "updated 16:15. 12 Nov 26 · 11/12 subtasks · due 11 Nov"; a period reads "9–16 Nov" with a title "Started 9 Nov – Due 16 Nov". CDP: expanded card light and dark.

## Verification
Each chunk: its named test classes green, CDP screenshots light and dark. After integration: the full fast suite once, then `tests/exploratory.ps1` once (52 of 52), by a merge agent.

## Not in this plan
- #wi197 (header gap): unreproduced on the current build; waits for the owner to confirm the build.
