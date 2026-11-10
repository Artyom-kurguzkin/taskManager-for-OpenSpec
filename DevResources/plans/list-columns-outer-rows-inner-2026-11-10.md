# List view: columns outside, rows inside — 10 Nov 2026

- **Status:** done (2026-11-10), uncommitted, #wi122 In Review
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work item:** #wi122

## Why

With columns = status and rows = feature, the list grouped by feature first and status inside it. The user: "columns: the upper grouping, then within them rows: the inner grouping".

## What the industry does

- Jira boards have no nested swimlanes (an Atlassian community answer points to Marketplace apps for grouping by two fields): [Nested swimlanes](https://community.atlassian.com/forums/Jira-questions/Nested-Swimlanes/qaq-p/1960290).
- GitHub Projects groups a table by one field at a time: [Customizing the table layout](https://docs.github.com/en/enterprise-server@3.21/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/customizing-the-table-layout), [Projects views API, group_by takes one field](https://docs.github.com/en/enterprise-server@3.22/rest/projects/views).
- So there is no common convention for which field is outside when two are nested; the user named it: columns outside, rows inside.

## Decisions

- The list groups project (when several) → column → row value → items. Columns in the board's order, row values in the board's row order, a row value only where the column has items, an empty column keeps its "No items" group.
- The calendar (roadmap) keeps rows outside columns: the user asked about the list only.
- The column header counts all its items; a drop on a row-value group lands in that column and that row value; collapse is per column and per row value inside a column.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | Grid grouping order, headers, counts, drops, add, collapse | sonnet, worktree, CDP 9361 | `UsingTheBoardTests` |
| B | Spec, integration, real-app check, board | main session | full suite |

## Verification

Red then green; the fast suite after integration; the list with columns status / rows feature checked on the hidden desktop in light and dark; nothing committed.
