# In Progress items, second batch, 7 Nov 2026

- **Status:** done
- **Date:** 2026-11-07
- **Author:** Claude (agent)
- **Work items:** #wi98 tests fail on a Windows checkout, #wi106 transition date, #wi107 calendar UI patch, #wi109 swimlanes, #wi93 split screen view

## Why

Five items are In Progress. #wi98: 18 tests fail on a fresh checkout because the fixtures come out with CRLF. #wi106: an item should carry the date it last moved to its status column. #wi107: the calendar should look like GitHub Projects' roadmap (screenshot on note n81). #wi109: GitHub-style swimlanes instead of per-column subgroups, on the board, the list and the roadmap. #wi93: on a wide window an open item sits beside the list.

## What the industry does

- GitHub Projects roadmap: items listed on the left, a timeline on the right with bars from start to target date, group headers spanning the group's dates, zoom month / quarter / year, a Today button, vertical markers. Source: [Customizing the roadmap layout](https://docs.github.com/en/enterprise-server@3.17/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-roadmap-layout).
- GitHub Projects swimlanes: "Group by" a field puts rows across the columns; dragging a card moves it between columns and groups. Source: [GitHub Issues & Projects, July 27th 2023 update](https://github.blog/changelog/2023-07-27-github-issues-projects-july-27th-update/); Linear does the same ([Linear swimlanes](https://linear.app/changelog/2024-04-03-swimlanes)).
- Line endings: a `.gitattributes` `eol=lf` rule on fixture files makes every checkout identical whatever `core.autocrlf` says.

## Decisions

- `x-rtm.moved` (yyyy-MM-dd): stamped on creation and whenever the status changes; absent means the creation date. Shown on the item page as "in <status> since" and as a list column.
- The calendar view keeps its key and menu entry but becomes a roadmap: left pane the items in the board's groups (group by), right pane a day (month zoom) or week (quarter zoom) timeline, bars start→due coloured by status, group bars spanning the group's dates, a today line, Today / ‹ / › controls. No drag to set dates yet.
- Swimlanes: a view option (none / priority / label; single-valued fields only, so a drop into a row can set the value). They replace custom subgroups and the automatic priority subgroups on the board. The list groups by swimlane above the column. The roadmap groups by the swimlane field when one is set. The WorkItems lane storage stays for now (unused by the board); a later item removes it.
- Split screen: on a window at least 1400px wide, an open item renders beside the list (list left, item right); the open item's row is marked; a toggle on the item page hides the list and is remembered. Below that width only the item shows (CSS media query, no script).

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | #wi98 fixtures LF on any checkout | sonnet, worktree | `ReadingWorkItemsTests` + the 18 go green in a fresh worktree |
| B | #wi106 transition date | sonnet, worktree, CDP 9341 | `EditingWorkItemsTests`, `UsingThePagesTests`, `UsingTheBoardTests` |
| C | #wi107 roadmap | sonnet, worktree, CDP 9342 | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| D | #wi109 swimlanes | sonnet, worktree, CDP 9343 | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| E | #wi93 split screen | sonnet, worktree, CDP 9344 | `UsingThePagesTests` |
| F | integration, roadmap ← swimlane wiring, full suite, board | main session | full suite |

## Verification

Each chunk red then green; the fast suite green after integration; every UI change checked on the hidden desktop in light and dark; nothing committed.
