# In Progress items, 10 Nov 2026: period highlight, Worklog in the side menu, description editor

- **Status:** done 2026-11-10 — #wi137 In Review; #wi34 In Progress (its git-based subtasks wait for the user); chunk C stopped, #wi140 cancelled
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work items:** #wi34 see work item deltas, #wi137 work item details rework (change request of 10 Nov). #wi140 (description editor) was cancelled by the user during the work: #wi137 removes the Description view, so there is nothing left to fix there.
- **Also:** the board is driven through the MCP tools this time; what did not work or was missing is logged on a work item of its own.

## Why

- #wi34: "in the menu, let me pick two date/time … Show which work items have been modified/created during selected period … on the board, list, calendar views - show by coloring the items (the rest should have no colors to them when this feature is activated)".
- #wi137, note "Change Request": "Make Description the very first entry in the Worklog. Within Worklog, add navigation (filters, content menu) - they should show on the main menu bar (the collapsible one to the left) when Worklog is open. When split view is active, these settings should be first; the other filters, groupings for list view are available, but go under the Worklog context settings."
- #wi140: "the editor still feels off. The scrollbar doesn't match the style. The Related section can easily live at the bottom instead of squeezing description for no reason. I want shift+confirmation thing just like in the Worklog."

## What the industry does

- Jira boards ship a "Recently Updated" quick filter (`updatedDate >= -1d`) and let filters stack; it filters rather than highlights ([Jira quick filters](https://support.atlassian.com/jira-software-cloud/docs/show-or-hide-issues-on-your-board), [Configuring Quick Filters](https://confluence.atlassian.com/jirasoftwareserver0901/configuring-quick-filters-1157490379.html)). The user asked for a highlight (colour kept on the matching items, the rest grey), which keeps the board's layout stable while showing the period.
- Jira's filters moved from buttons above the board into a dropdown and users asked for the buttons back ([JSWCLOUD-16468](https://jira.atlassian.com/browse/JSWCLOUD-16468)): controls for the current view belong where the user looks, which is why the Worklog's filters and actions go into the side menu while the Worklog is open.

## Decisions

1. **#wi34 period highlight.** A "changed" control in the side menu's filters: a from/to picker (the `DateRange` control of the item page, days; the item's events carry times, the picker picks days). While set, items created or changed in the period keep their colour and every other card, list row stripe and roadmap bar goes neutral (`--stone`); the legend says "changed <from> – <to>". "Changed" = any event in `events.log`, any note created, the item's `created`, or its files' last write, in the period. The setting is per board like the other filters and clears with a ×. The git-based subtasks (uncommitted, one commit, between commits) stay open on the item.
2. **#wi137 Worklog in the side menu.** The description is the first entry of the feed, pinned above today's group, rendered, Shift-click → Edit as a note. While the Worklog is open (the item page's Worklog view), the side menu shows a "Worklog" block at the top: filters (notes · events · links · subtasks; who) and the day menu's actions (Add note, Link document, Link test, Link commit, Add subtask). In split view that block comes first and the list's organise / sort / filter blocks follow it. The block is gone when the Worklog closes. The Worklog publishes its state through a small service the layout and the side menu read; the item page itself is not changed.
3. **The Description view is gone** (user, 15:30): the item page has two views, Worklog (default) and Linked tasks. The description is the Worklog's first entry, edited in place (Shift-click, Edit; image paste kept). The Related list (plan, linked documents, attachments, images) moves into the Linked tasks pane as a fourth column, with its × and "+ link plan".
4. **MCP:** plan links, notes, subtasks and status go through `board_update` / `board_note`; every failure or missing feature is a subtask on a new item "MCP in daily use: what was missing".

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | #wi34 period highlight | sonnet, worktree, CDP 9368 | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| B | #wi137 description first; Worklog block in the side menu; split-view order | sonnet, worktree, CDP 9369 | `UsingTheWorklogTests`, `UsingTheBoardTests` |
| C | #wi140: stopped when the user cancelled the item; B took over `WorkItemPage.razor` | — | — |
| D | Board over MCP, friction log, integration, real-app check | main session | full suite |

## Verification

Red then green per chunk; the full suite after integration; hidden desktop in light and dark; nothing committed.
