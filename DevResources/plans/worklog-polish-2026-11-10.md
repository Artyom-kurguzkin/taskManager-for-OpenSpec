# Worklog polish, 10 Nov 2026: description at the bottom, Unlink a test, jump between days

- **Status:** done 2026-11-10 — integrated into #wi137 (In Review); 505 tests green
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work item:** #wi137 work item details rework (corrections after review)

## Why

After the item page rework (#wi137) the user reviewed the worklog and asked for three things, in their words:

1. "description still has old on hover effect - remove. And it should be at the very bottom - first feed post"
2. "if i want to unlink a test - i should shift click and it should let me unlink - this one is glitchy"
3. "edit navigation in the menu letting me jump btw dates"

## What the industry does

- Feeds that span many days group entries under date dividers so the reader can find the right day ([Cloudscape timeline guidance](https://cloudscape.design/gen-ai/patterns/timeline-overview/)).
- Once a feed grows, people need to query it: date is one of the standard filter dimensions beside actor and object ([SaaS activity feed and audit log patterns](https://www.saasui.design/blog/saas-activity-feed-audit-log-ux-patterns)).
- Accessible timelines offer "skip to latest / oldest" controls as part of keyboard navigation ([timeline component guidance](https://skills.sh/thedaviddias/ux-patterns-for-developers/timeline)).
- No source names a sidebar day list as a pattern; chat clients (Slack "Jump to date", Telegram's calendar) are the everyday precedent. Ours is simpler: the days that exist in the feed, listed in the side menu, one click each.

## Decisions

- The description is the item's first post, so it is the **last** entry of the feed (below "Linked earlier"), with no hover effect. Shift-click → Edit, keyboard Shift+Enter and image paste stay.
- The Unlink glitch is a bug chunk: an agent reproduces it over CDP, finds the cause (menu, row keys, or the key comparison on unlink) and fixes it where every caller routes through.
- Day navigation lives in the side menu's WORKLOG block under the actions: the feed's days, newest first, each with its entry count; a click unfolds the day and scrolls it into view. No scroll-spy, no current-day marker (add when asked).

## Chunks

| Chunk | What | Owner | Test |
|---|---|---|---|
| A | Description at the bottom, no hover | sonnet, worktree, port 9353 | UsingTheWorklogTests: description last, no `hov`; shots light/dark |
| B | Shift-click Unlink on a test | sonnet, worktree, port 9351 | UsingTheWorklogTests: unlink a test; CDP repro; five green runs |
| C | Days list in the WORKLOG block | sonnet, worktree, port 9352 | UsingTheBoardTests + UsingTheWorklogTests: days published and listed, click jumps; shots light/dark |

## Verification

- Full fast suite green after integration (baseline 502).
- Hidden desktop, light and dark: description at the bottom without hover, Unlink on a test row, Days list and jump.
- #wi137 updated through the MCP tools: note, test links, tasks, spec delta.
