# In Progress items, 7 Nov 2026

- **Status:** done
- **Date:** 2026-11-07
- **Author:** Claude (agent)
- **Work items:** #wi99 list view column cut, #wi100 tracking assets for multiple projects, #wi64 notes editor, #wi96 no collisions

## Why

Four items are In Progress. #wi99: list cells spill into the next column. #wi100: Settings sets the plans folder only for the active project, and the attachments folder is fixed. #wi64: the note editor (Radzen markdown editor) feels busy and slow next to the work item description, which is a plain text box. #wi96: agent C's id fixes wait for integration. Also: the screenshot attached to #wi99's note n69 is missing from disk.

## What the industry does

- Grid cells clip: `overflow: hidden; text-overflow: ellipsis; white-space: nowrap`, with a known table width (`table-layout: fixed` needs one) and the full text in a tooltip. Sources: [MDN table-layout](https://developer.mozilla.org/en/docs/Web/CSS/table-layout), [W3C wiki](https://w3.org/wiki/CSS/Properties/table-layout), [WebReference](https://webreference.com/css/properties/table-layout/).
- Per-project settings (VS Code workspace settings, JetBrains project settings) are listed per project, each row named by its project.

## Decisions

- List cells never overflow: every cell clips with an ellipsis and a title tooltip; a chip row clips too.
- Settings lists every open project with its own plans folder and attachments folder (relative to that repository; empty = default). Attachments of a note go to its project's folder.
- The note editor becomes the description editor: rendered text, click to edit in a plain text box, autosave while typing, a click outside saves and renders. Paste of images keeps working.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | #wi99 list cells clip | sonnet, worktree, CDP 9341 | `UsingTheBoardTests` + measured overflow in the real app |
| B | #wi100 per-project folders + lost paste investigation | sonnet, worktree, CDP 9342 | `SwitchingProjectsTests`, `KeepingNotesTests` + real app |
| C | #wi64 note editor as the description editor | sonnet, worktree, CDP 9343 | `UsingThePagesTests` + real app |
| D | #wi96 integrate id fixes, full suite, board | main session | full suite |

## Verification

Each chunk red then green; the whole fast suite green after integration; every UI change checked on the hidden desktop in light and dark; nothing committed.
