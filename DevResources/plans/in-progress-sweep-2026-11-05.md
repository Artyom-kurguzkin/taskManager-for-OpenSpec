# In Progress sweep, 5 Nov 2026

- **Status:** done, in review
- **Date:** 2026-11-05
- **Author:** Claude (agent)
- **Work items:** #wi13, #wi47, #wi54, #wi66, #wi76 (3.5), #wi87, #wi88

## Why

Seven items sit In Progress. Most are small, independent UI and model changes; attachments (#wi66) is the one sizeable piece. They are split so that each chunk owns its files and is testable alone, and the independent ones run in parallel on cheaper models.

## What the industry does

- Card design: trackers (Linear, Jira boards) keep a card to title, one or two coloured tags and a progress figure; icons are dropped in favour of short text. Priority is usually a swimlane or a sort, not a badge on every card.
- Merging duplicates: GitHub, Jira and Linear all pick a **canonical** issue; the others are closed as duplicates and their text is preserved as comments on the canonical one. That is "merge into".
- Attachments outside git: file-based trackers keep metadata in git and large or binary files in an ignored folder next to the issue, referenced by relative path ([git-issue](https://docs.rs/crate/git-issue/latest/source/README.md), [imdone write-up](https://dev.to/imdone/managing-jira-issues-in-markdown-why-i-keep-my-backlog-in-the-repo-4gdk)). Links to external resources are kept as plain URLs in the note.

## Decisions

- #wi54 + #wi13 (cards): meta row loses the dashed icons ("5 Nov · Artyom"), the subtask count comes back as `2/3` (reverses #wi51's "no count"; the user asked for it), the label tag is filled with the label's colour (bug peach, feature sky, enhancement sage, the same as the list pills), the "high priority" tag goes.
- #wi54 1.3 (priority): within a column without custom subgroups, cards are grouped under automatic "High priority" / "Normal" / "Low priority" headings when more than one priority is present; dropping a card on such a heading's lane sets its priority. Custom subgroups win when a column has them.
- #wi47 1.6 (merge into): the user picks the canonical item; the merged change keeps its title and description; every other source's description becomes a note "Merged from #wiN <title>" in the merged change. The folder, task groups, notes, links and specs merge as before.
- #wi87 (delete feature): a feature that open changes still describe in their delta specs is no longer refused; the confirmation names those items and their delta folders for the feature are deleted with it. Archived changes are left alone; nested features still refuse.
- #wi88 (selection): a black 3px solid outline with a yellow halo in light, the same in dark (ink is light there), replacing the dotted yellow.
- #wi76 3.5 (project filter): the expanded side menu lists every open project with a checkbox; unticking hides it (the existing hidden-projects setting), so the hidden list and the eye stay as they are.
- #wi66 (attachments): a note's frontmatter gets `attachments: [<path or URL>, …]`. Files are copied to `local/task-tracking/wi<item>/n<note>/<file>` (the app adds `local/` to `.gitignore` when missing); URLs are stored as typed. The note page lists them: images inline (data URL from the file), files with their name opening in the OS, URLs as links. Adding: a file picker (host-registered like the folder picker) and a URL field. The agent guide tells agents where attachments go.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| 1 | Cards: icons out, count back, coloured label, no priority tag (`WorkItemCard.razor`, css) | sonnet, worktree | `UsingTheBoardTests` card tests |
| 2 | Priority subgroups in columns (`BoardModel`, `BoardPage`) | sonnet, worktree | `ArrangingTheBoardTests`, `UsingTheBoardTests` |
| 3 | Merge into (`WorkItems.Commands.Merge`, `BoardPage.Merge`, Choose dialog) | sonnet, worktree | `EditingWorkItemsTests`, `UsingTheBoardTests` |
| 4 | Delete a feature that open changes still spec (`WorkItems.Commands.DeleteFeature`, dialog) | sonnet, worktree | `EditingWorkItemsTests`, `BrowsingFeaturesTests` |
| 5 | Attachments on notes (`Notes` module, `NotePage`, file picker, `.gitignore`) | sonnet, worktree | `KeepingNotesTests`, `UsingThePagesTests` |
| 6 | Selection look, project filter checkboxes, guide text, OpenSpec updates for every item, integration | main session | `UsingTheBoardTests`, `SwitchingProjectsTests` |

1–5 are independent; 6 integrates them.

## Verification

Each chunk's tests red first, then green; the whole suite green after integration; hidden-desktop screenshots in light and dark for every visible change; every item's spec/tasks/note/tests updated and validated `--strict`; nothing committed.
