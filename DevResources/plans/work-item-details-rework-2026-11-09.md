# Work item details rework: Description, Worklog, Linked tasks — 9 Nov 2026

- **Status:** done (2026-11-09), uncommitted, #wi94 In Review
- **Date:** 2026-11-09
- **Author:** Claude (agent)
- **Work item:** #wi94 work item details rework

## Why

The item page is a bento of six tiles (description, subtasks, waiting on this, commits, tests, change log) where only the focused tile is usable and the rest are three-line previews. The user's wireframe replaces it with three full views under the unchanged header, and turns the change log into a dated feed that also shows what happened to the item (links, status changes), so that the history of an item reads top to bottom in one place and agents can keep it current.

## What the industry does

- GitHub keeps one timeline per issue in which comments and typed events (labeled, referenced, commented, …) are items of the same list; clients can exclude event types ([timeline REST API](https://docs.github.com/en/rest/issues/timeline), [issue event types](https://docs.github.com/en/enterprise-server@3.0/developers/webhooks-and-events/events/issue-event-types)).
- Linear has a single activity stream and reduces noise by grouping consecutive similar events and collapsing old history between comments ([collapsed issue history](https://linear.app/changelog/2025-04-03-collapsed-issue-history)).
- Jira splits activity into Comments / History / Work log tabs with an "All" view that merges them; users complained when the tabs were hidden behind a dropdown ([types of activity](https://support.atlassian.com/jira-software-cloud/docs/what-are-the-different-types-of-activity-on-an-issue/), [JIRAAUTOSERVER-1217](https://jira.atlassian.com/browse/JIRAAUTOSERVER-1217)).

Taken from them: one merged feed (GitHub, Linear), typed events stored as data rather than prose (GitHub), view buttons always visible (the Jira complaint).

## Decisions (the user's answers, 9 Nov, are marked ✔)

1. **Three views** under the header, which stays as it is: **Description**, **Worklog**, **Linked tasks**. ✔ All three buttons always shown, the current one highlighted. Description is the default.
2. **Description view:** the description (click to edit, paste images, as today) and, to its right, a **Related** list of everything attached to the item: the plan, linked documents, `[[doc]]` links and attachments of its notes, images in the description and in notes. One click opens each.
3. **Linked tasks view:** three columns: **Subtasks** (groups, tick, edit, Shift-select and move out, add), **Waiting on** (dependencies, with the picker), **Waiting on this** (items this one blocks). They stack in a narrow window. The bento, tile focus and "minimised tiles" go away.
4. **Worklog view:** one feed, newest day first, each day a collapsible group with its date, each entry with the time it was first created on the right. Today's group is always there.
   - Entries are **notes** (title, markdown body, images) and **events**.
   - ✔ **Events are recorded from now on**: created, status changed, commit / test / document linked or unlinked, subtask ticked or reopened, dependency added or removed. Stored per item in `openspec/changes/<change>/events.log`, one line each: `yyyy-MM-ddTHH:mm:ss | <who> | <kind> | <text>`. The file travels with the change (merge concatenates, archive moves it).
   - Links that predate the log still show: a commit at its own commit time (also commits that only mention `#wiN`), tests and documents in a last group "Linked earlier".
   - ✔ **Linked tests live in the feed only** ("linked a test: Suite / name ✓" with the current outcome); there is no separate tests list. `x-rtm.tests` stays the stored list.
   - ✔ **Shift-click a date** (or Shift+Enter on it) opens a menu: **Add note** (markdown editor in place), **Link document**, **Link test**, **Link commit**. A note added under an older day is dated that day.
   - ✔ **Shift-click a note** opens a one-option menu: **Edit** (in place: title, text, paste an image; click outside saves). Added by me: on a commit / test / document entry that is still linked the one option is **Unlink**, because nothing else on the page can unlink now. Status and subtask events have no menu.
5. **Documents** become a stored list like commits and tests: `x-rtm.docs: [<repo-relative path>]`, `WorkItemsApi.SetLinkedDocs`.
6. **Who did it:** events carry the actor. The desktop app sets it to the user's name; the MCP tools set it to the calling agent.
7. ✔ **MCP and the guide:** events are written by the same commands the tools call, so an agent's status change or link is logged without extra work. `board_get` returns the `worklog` (notes and events merged), `board_update` gains `docs`, `board_note` takes an optional title (a worklog entry in one call). Server instructions and REPO-TASKS.md v13 gain "keep the worklog current", with the `events.log` line format as the hand fallback.
8. **Not doing:** a separate tests list, per-test notes in the UI (the data stays), remembering the last view per item, deleting notes, editing or deleting events.

## Chunks

Phase 1 starts from HEAD (`3b7432c`, which has the MCP work). Phase 2 depends on phase 1, which is uncommitted by then: its agents first apply `tests/out/base-1109d.patch` (phase 1 as integrated).

| # | Phase | Chunk | Owns | Test | Black-box |
|---|-------|-------|------|------|-----------|
| A | 1 | Events and documents in the modules: `events.log`, events from the commands, actor, `GetEvents`, `x-rtm.docs`, notes dated on creation | `WorkItems/*`, `Notes/*` | `EditingWorkItemsTests`, `KeepingNotesTests` | — (through C2) |
| C1 | 1 | Page shell: three views, Description + Related list, Linked tasks columns; a stub `Worklog.razor` | `WorkItemPage.razor`, `app.css`, item-page tests | `UsingThePagesTests` | hidden desktop, CDP 9351, light and dark |
| C2 | 2 | The feed: `Worklog.razor` with days, entries, menus, in-place note editor, link pickers, unlink | `Worklog.razor(.cs)`, its CSS block | new `UsingTheWorklogTests` | hidden desktop, CDP 9352, light and dark |
| B | 2 | MCP and guide: `worklog` in `board_get`, `docs`, optional note title, actor; instructions; guide v13; host sets the actor | `TaskManager.Mcp/*`, `AgentGuide.cs`, `Workspace.cs` | `UsingTheBoardOverMcpTests`, `GuidingAgentsTests` | shim call on the hidden desktop (main session) |
| I | — | Integration, full suite, real-app check, board | main session | full suite | hidden desktop |

## Verification

Red then green per chunk; the fast suite and the `Server` category green after each integration; the page checked on the hidden desktop in light and dark at 1280 and 1920 wide: each view, add a note under today and under an older day, link and unlink a commit, a test and a document, a status change appearing as an event, an item written over MCP showing the agent's events. Nothing committed; #wi94 updated.

## What was built, and where it differs from the plan (9 Nov 2026)

All four chunks are in the working tree. Fast suite 447 pass, OpenSpec CLI suite 14 pass; checked in the real app on the hidden desktop in light and dark, including an agent writing through the MCP entry.

- **Order of work:** B (MCP and guide) started as soon as A was in, in parallel with C1; C2 followed C1. Agent worktrees start from `origin/main`, which was one commit behind the local branch, so A and C1 were merged three-way and B and C2 were told to reset to the local HEAD first.
- **Unlink in the feed** is offered for any hand-linked commit, test or document, also one linked before the log existed. A commit that only mentions the item has no menu.
- **A link is dated when it is made**, so "Link commit" from an older day's menu shows under today. Only "Add note" dates the entry to the day whose menu was used.
- **Merged-in notes** are no longer grouped under "From #wiN"; each says "from #wiN" on its entry.
- **`board_note` without a title** uses the first line as the title and does not repeat it in the text.
- **Related list** has a fifth kind word, "file", for attachments that are neither images nor markdown.
- **Not done:** Link document through the native file dialog was not driven in the real app (it cannot be reached on the hidden desktop); tests cover it through the typed-path fallback. Documents and plans of an item in another open repository open against the active repository, as the plan chip already did.
- **Found on the way:** a hand-edited `.openspec.yaml` with mixed LF and CRLF line endings made the Debug app fail at start with a YamlDotNet error. Not investigated.

## Corrections after the user's review (9 Nov, later)

- **The plan was shown twice** (header chip and Related list). The header no longer has it: the Related list is the one place for the plan and the linked documents, with "+ link plan" and a × per stored link.
- **No frames.** The views' tiles, their heading pills, the subtask rows and groups, and the worklog's note cards lost their borders, fills and shadows; rows and entries are divided by a soft line, as on the wireframe.
