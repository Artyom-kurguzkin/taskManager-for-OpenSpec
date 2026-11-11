# In Progress items, round 3, 10 Nov 2026: description editor, table helper, feed conversions, subtask bug

- **Status:** in progress (delegated 2026-11-10)
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work items:** #wi149, #wi133, #wi146

## Why

"work on the current in-progress items". New since the last look, read from the worklogs:

- #wi149 (new): "make a ui reusable scrollbar and use it everywhere. The one i currently see does not match the style. The editor area should cover the entire rest of the screen downwards, not just a small fragment. I also dont want the "why" & "what changes" headers - i dont use them."
- #wi133, note n157: "the markdown editor should include option to generate or edit tables easily. like, in this editor here i should be able to type `/table` that triggers the helper. the experience should be similar to table editor in word."
- #wi146: 7.9 "convert logs btw note - Decision - Subtask by selecting log entry (extra option on top of edit)"; 7.11 "editing already created subtask within a feed created yet another subtask - bug"; 7.8 / 7.10 "rework the Linked tasks - layout should be nicer" (asked, not delegated: no design given). 7.5–7.7 were done earlier and lost their ticks in the merge.

## What the industry does

- Markdown table editing: Obsidian's Advanced Tables adds, removes and moves rows and columns, sets alignment and re-formats the table as you type, with a controls panel ([tgrosinger/advanced-tables-obsidian](https://GitHub.com/tgrosinger/advanced-tables-obsidian), [help](https://github.com/tgrosinger/advanced-tables-obsidian/blob/main/docs/help.md)); Table Generator inserts a table from a size grid "like Typora" ([obsidian-table-generator](https://community.obsidian.md/plugins/obsidian-table-generator)); Word's Insert Table is the same hover grid of rows × columns.

## Decisions

- **Scrollbar**: one thin scrollbar style in the base layer, applied to every scroll area (tokens only, both themes); areas that hide the bar today keep their fade edges and get the thin bar on hover.
- **Description editor fills the page** below its header, down to the bottom of the window.
- **No Why / What Changes in the app**: the description is the user's text. The proposal file keeps whatever headers the OpenSpec validator needs, but the app neither shows nor asks for them, and new items get none the user sees.
- **Table helper**: `/table` at the start of a line opens a rows × columns grid (Word-style hover grid) and inserts a formatted table; with the caret inside a table, a small bar offers add row above/below, add column left/right, delete row/column, and Tab / Shift+Tab move between cells with the table re-aligned. In every markdown editor of the app.
- **Feed conversions**: a note's menu gets "Make it a decision" / "Make it a subtask"; a decision's "Make it a note" / "Make it a subtask"; a subtask-added entry "Make it a note" / "Make it a decision". Converting removes the original and logs a `converted` event.
- **Bug 7.11**: a line already marked "(created subtask)" is never promoted again, wherever the marker sits; when the text was edited, the existing subtask is retexted instead.

## Chunks

| Chunk | What | Owner | Test |
|---|---|---|---|
| A | #wi149 scrollbar, full-height editor, no Why/What Changes | sonnet, worktree, port 9431 | page tests; shots |
| B | #wi133 table helper | sonnet, worktree, port 9432 | JS logic tests + page tests; shots |
| C | #wi146 7.11 subtask re-promotion bug | sonnet, worktree | KeepingNotesTests |
| D | #wi146 7.9 convert entries | sonnet, worktree, port 9434 | UsingTheWorklogTests; shots |

## Verification

- Full fast suite and CLI suite green after integration.
- Hidden desktop, light and dark: the editor full height with the new scrollbar; `/table` grid and the table bar; a conversion in the feed.
- Items kept current through the tools; In Review when done.
