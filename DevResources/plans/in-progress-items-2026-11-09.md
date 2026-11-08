# In Progress items, 9 Nov 2026

- **Status:** done
- **Date:** 2026-11-09
- **Author:** Claude (agent)
- **Work items:** #wi47 merging keeps everything, #wi114 swimlanes (merged with #wi113 work item organiser), #wi93 split screen view, #wi100 tracking assets for multiple projects, #wi64 notes editor

## Why

Each item came back to In Progress with a new note from the user:

- #wi47 (n92): after merges, a work item's subtasks are one flat list of 95 lines; it is hard to tell what came from which item and when. Organise merged content by source item and date.
- #wi114 (n93, from #wi113): columns, colours and swimlanes group work differently. They should work uniformly: any of them can group by any field, the same way in the board, the list and the roadmap. Rethink the interface.
- #wi93 (n94): besides the in-window split, the split button should offer moving the open item into a separate window.
- #wi100 (n88): Settings shows only the first open project's location; every open project should list its own.
- #wi64 (n95): the description and note editors are small inside large tiles; a second border shows around the editor; the note's title and its first heading are linked and should not be.

## What the industry does

- GitHub Projects separates the column field (board columns: any single-select or iteration field), "Group by" (rows / table groups / roadmap groups) and "Slice by" (a side filter), each set independently per view. Sources: [Customizing the board layout](https://docs.github.com/en/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-board-layout), [Customizing the table layout](https://docs.github.com/en/enterprise-server@3.17/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-table-layout), [GitHub Issues & Projects, August 10th 2023 update](https://github.blog/changelog/2023-08-10-github-issues-projects-august-10th-update/), [July 27th 2023 update](https://github.blog/changelog/2023-07-27-github-issues-projects-july-27th-update/).
- Multiple windows in Photino.Blazor need a second app builder per window and careful shared state ([photino.Blazor issue #150](https://github.aipraa306.workers.dev/tryphotino/photino.Blazor/issues/150), [PhotinoX.Blazor](https://www.nuget.org/packages/PhotinoX.Blazor) Multi-Window sample). The app's modules hold static per-repository state, so a second process that opens the same repository and the item is simpler and safer; both processes already pick up each other's file edits through the file watcher.

## Decisions

- **#wi47:** a merge writes each source's task group heading as `#wi<N> <title> · merged <yyyy-MM-dd>`; the Subtasks tile shows every group as a collapsible header with its done/total; notes moved in by a merge get `mergedFrom: <N>` and `mergedAt: <yyyy-MM-dd>` in their frontmatter, and the Change log tile groups them under "From #wi<N> · merged <date>" after the item's own notes. Old merged items (no date) show their headings as they are.
- **#wi114:** three independent view options in one "Organise" block of the side menu: **columns** (the old group by), **rows** (the old swimlanes, now any field or none) and **colour** (status by default; label, priority, feature, contributor, due, none). Fields: status, feature, label, priority, contributor, due, started. Board: columns × rows, cards banded by colour. List: rows then columns as group levels, each row striped by colour. Roadmap: rows then columns as group levels, bars coloured by colour. Dropping into a cell sets both the column's and the row's field, using the same move logic columns already use. A small legend explains the colours.
- **#wi93:** the split button becomes a menu: "Beside the list", "Item only", "Separate window". "Separate window" starts a second app process with `--repo <root> --item <id>`; that process shows only the item page (no side menu, no split), uses the same settings file read-only, and is titled "#wi<N> <title> — Repo tasks". The main window goes back to the board.
- **#wi100:** the top "Repository" row becomes "Open another repository" (path box, Browse, Open). Each open project's row is headed by its name and shows its location (read-only, with "Show in folder"), then its Plans and Attachments folders.
- **#wi64:** the description and note text boxes fill their tile's height (no fixed rows), with no border of their own while editing (the tile is the frame). A note's title is stored in its frontmatter (`title:`); editing it leaves the body alone. A note without `title:` still reads its title from its first heading.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | #wi47 merged content by source and date | sonnet, worktree, CDP 9341 | `EditingWorkItemsTests`, `UsingThePagesTests` |
| B | #wi114 columns, rows, colour everywhere | sonnet, worktree, CDP 9342 | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| C | #wi93 separate window | sonnet, worktree, CDP 9343 | `UsingThePagesTests` + a second process in the real app |
| D | #wi100 project locations in Settings | sonnet, worktree, CDP 9344 | `SwitchingProjectsTests` |
| E | #wi64 editors fill the tile, note title separate | sonnet, worktree, CDP 9345 | `UsingThePagesTests`, `KeepingNotesTests` |
| F | integration, full suite, board | main session | full suite |

## Verification

Each chunk red then green; the fast suite green after integration; every UI change checked on the hidden desktop in light and dark (emulate 1920px width where a layout needs it); nothing committed.
