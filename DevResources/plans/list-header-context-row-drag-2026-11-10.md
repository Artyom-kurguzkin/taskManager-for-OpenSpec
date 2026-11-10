# List: where am I, no indent, drag the whole row — 10 Nov 2026

- **Status:** done (2026-11-10), uncommitted, #wi123 In Review
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work item:** #wi123

## Why

The user, on the list view: rows scrolled under the header give no hint of their column and row group; the title column is indented for no reason; the six-dot grip is unwanted: "any place within the row should be clickable and/or dragable", the app telling a click from a hold-drag.

## What the industry does

- Sticky group context: a pinned header that names the section currently under it, like iOS table section headers and spreadsheet frozen rows; here the grid header carries it.
- Click vs drag on the same element: dnd-kit, the common drag library, starts a drag after a delay (touch: 250 ms, 5 px tolerance) or after a distance, whichever comes first ([dnd-kit sensors](https://dndkit.com/legacy/api-documentation/sensors), [pointer sensor](https://mintlify.com/clauderic/dnd-kit/concepts/sensors)). Here press-and-move already selects cells, so only the delay can start a row drag: 250 ms, under 4 px.

## Decisions

- The sticky header shows "project › column › row" of the topmost visible row (project only with several open), updated by a scroll listener in the page script.
- No grip column; a title lines up under "Title" whether or not its row has subtasks.
- Click = open / act. Press and hold 250 ms (moving under 4 px), then move = drag the row (selected rows together). Press and move at once = select cells, as before.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | Header context, indent, drag anywhere | sonnet, worktree, CDP 9362 | `UsingTheBoardTests` |
| B | Plan, item, integration, real-app check | main session | full suite |

## Verification

Red then green; full suite after integration; hidden desktop light and dark: header path while scrolled, aligned titles, a hold-drag that changes a status, a quick drag that selects cells, a click that opens.
