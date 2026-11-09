# In Progress items, second batch, 9 Nov 2026

- **Status:** done
- **Date:** 2026-11-09
- **Author:** Claude (agent)
- **Work items:** #wi115 new features from the item page, #wi116 menu organiser patch, #wi117 move and hide selected swimlanes, #wi118 remove the outer border, #wi119 attachments in work item descriptions

## Why

- #wi115: the item page's feature dropdown lists existing features only; it should also offer "add new".
- #wi116 (n102, screenshot): the side menu needs two separators, after the colour legend and after the Work items / Subtasks switch.
- #wi117: Shift-click a swimlane (row) header to select it, then move it above/below its neighbours by drag or arrow keys, or hide it, as columns already can.
- #wi118 (n103, screenshot): the outer rounded border on the calendar, the work item page and the note page is noise and wastes space; drop it, as the list view already has none.
- #wi119: a screenshot in a description needs a note today. Pasting an image into the description should save it with the project's attachments and insert a link that renders in view mode, like Obsidian.

## What the industry does

- Obsidian saves a pasted image as "Pasted image <timestamp>.png" in the configured attachments folder ("Default location for new attachments": vault root, a fixed folder, or a subfolder next to the note) and inserts an embed link at the caret that renders in reading view. Sources: [Obsidian forum: default location for new attachments](https://forum.obsidian.md/t/pasted-images-do-not-get-send-to-my-default-location-for-new-attachments/66786), [Obsidian forum: attachment subfolder](https://forum.obsidian.md/t/set-default-attachment-folder-for-same-folder-as-the-file-is-in/699), [Custom Attachment Location plugin](https://community.obsidian.md/plugins/obsidian-custom-attachment-location).
- GitHub Projects selects and hides board groups from the group header's menu; the app already moves and hides columns and projects this way (Shift-click to select, arrows to move, × to hide, the side menu's Hidden list to bring back).

## Decisions

- **#wi115:** the item page's feature dropdown ends with "+ New feature…"; picking it opens the same dialog the board uses to create a feature (name, and group where the board asks for one), creates it and sets it on the item.
- **#wi116:** two dashed separators in the side menu's view options, the same style as the existing ones: after the colour legend, after the Work items / Subtasks switch.
- **#wi117:** swimlane headers behave like project headings: Shift/Ctrl/⌘-click selects (tinted), the up/down arrow keys move the selected rows past their neighbours, dragging a header moves it, and a × on the header hides the row. The order and the hidden rows are remembered per board like columns (keys `row:<field>:<value>` in the board's Order / Hidden lists), and hidden rows are listed in the side menu's Hidden section to bring back.
- **#wi118:** no outer frame (border, radius, shadow) around the calendar, the work item page and the note page; their content keeps its tiles. Same spacing as the list view.
- **#wi119:** pasting an image into the description editor saves it as `pasted-<yyyyMMdd-HHmmss>.png` under the project's attachments folder in `wi<item>/` (no note), inserts `![](<repo-relative path>)` at the caret, and the description renders it in view mode. Same git-ignore handling as note attachments.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | #wi115 new feature from the item page | sonnet, worktree, CDP 9341 | `UsingThePagesTests` |
| B | #wi117 select, move, hide swimlanes | sonnet, worktree, CDP 9342 | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| C | #wi116 separators + #wi118 no outer border | sonnet, worktree, CDP 9343 | `UsingThePagesTests`, `UsingTheBoardTests` |
| D | #wi119 description attachments | sonnet, worktree, CDP 9344 | `EditingWorkItemsTests`, `UsingThePagesTests` |
| E | integration, full suite, board | main session | full suite |

## Verification

Each chunk red then green; the fast suite green after integration; every UI change checked on the hidden desktop in light and dark; nothing committed.
