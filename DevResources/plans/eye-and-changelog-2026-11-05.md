# Project eye bug, agent in title, changelog UI — 5 Nov 2026

- **Status:** done, in review
- **Date:** 2026-11-05
- **Author:** Claude (agent)
- **Work items:** #wi76 (project eye bug), #wi91, #wi92

## Why

The eye in the project switcher does nothing for the user. Two new items ask for the agent chip to leave the work item title (the agent becomes a contributor instead) and for the change-log notes to be usable: editable titles, edit by clicking in, images pasted from the clipboard.

## What is known about the eye bug

- The click reaches Blazor: the project leaves `HiddenProjects` in the settings file.
- Nothing re-renders afterwards: the switcher still shows "Show project …", the board stays single-project, 40 s later still, no busy spinner.
- With non-git copies of the same `openspec/` folders it works; in bUnit against the real git repositories it also works. So the failure is in the Photino host path, likely in a `Settings.Changed` subscriber (Workspace guide refresh, MainLayout, BoardPage reload, SideNav redraw) when a git-backed project becomes visible.

## Decisions

- #wi91: no agent chip in the item title row. An agent that stamped the item shows among the contributors as a lilac chip; while unreviewed that chip is the review control (click → reviewed), afterwards a plain lilac chip. The guide tells agents to add their name to `contributors` when they create a work item or subtask. Cards keep their small agent tag.
- #wi92: a note's title is its first `# ` heading and is editable in the note header (saved on blur); the Edit button goes: clicking the rendered note opens the editor, clicking outside saves and returns to reading, like the description; an image pasted while the note page has focus becomes an attachment (`local/task-tracking/…`), like a file picked with "Attach file…".

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | Eye bug: find the host-side cause, fix, regression test | sonnet, worktree (CDP port 9341) | `SwitchingProjectsTests` + hidden-desktop run |
| B | Agent as contributor, no title chip, guide v6 | sonnet, worktree | `UsingThePagesTests`, `SwitchingProjectsTests` |
| C | Note title editing, click-in editing, paste image | sonnet, worktree | `KeepingNotesTests`, `UsingThePagesTests` |
| D | Integration, hidden-desktop check in both themes, board updates | main session | full suite |

## Verification

Tests red first, suite green after integration, screenshots in light and dark, every item's spec/tasks/note/tests updated and validated `--strict`, nothing committed.
