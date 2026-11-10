# A smooth app: nothing the user does waits for a second — 10 Nov 2026

- **Status:** done (2026-11-10), uncommitted, #wi138 In Review
- **Date:** 2026-11-10
- **Author:** Claude (agent)
- **Work item:** #wi138

## Why

"i click on a task to view details - takes almost a second. can we do shit faster?" "i need in-general smooth experience. like, wtf - we are running this localy!"

Measured on a copy of this board (50 items, 335 files): a subtask tick shows after 600–800 ms, sometimes with a second refresh at ~1.1 s; switching to the Worklog view settles after ~1 s in five render bursts; opening an item takes close to a second. The drawing itself is a few milliseconds; the time goes to re-reading and to git.

| Where | Measured | Why |
|---|---|---|
| Any write → re-parse of every work item | ~200 ms, twice per write | the cache is all-or-nothing on `OpenSpecStore.Version`; `Revision()` reads every file of every folder to hash it (~80 ms of the 200); the write command itself does `Get(id)` after a bump and so rebuilds before writing |
| Notes re-parse | 75–230 ms | same all-or-nothing cache |
| `Workspace.Notify` → `ResolveDuplicateIds` | ~50 ms per change | runs on every change, own writes included |
| Git questions | 75–110 ms each, never cached | `Recent`, `Mentioning`, `Commits` each spawn `git`; the worklog asks 2–4 per render and renders 5 times |
| Item page renders | 5 bursts per view switch | `Load()` on every parent render, module calls inside render, `Changed` + child `OnChanged` double path, watcher echo |

## What the industry does

- Blazor: render only when state changes; avoid extra `StateHasChanged`; compute in lifecycle methods, not in render; `ShouldRender` to skip ([Razor component rendering](https://learn.microsoft.com/aspnet/core/blazor/components/rendering), [Blazor performance best practices](https://learn.microsoft.com/aspnet/core/blazor/performance)).
- File-backed caches invalidate per entry on a cheap fingerprint (name, size, mtime), not whole-store; git tooling runs one `git log` and answers from memory rather than one process per question (the module's own `AuthorsOfChange` already does this per HEAD).

## Decisions

1. **Per-folder cache** in WorkItems and Notes: a folder is re-parsed only when its files' fingerprint (names, sizes, mtimes) changed; the revision comes from the same pass. One write re-parses one folder.
2. **No duplicate work per write:** a command's `Get` costs one folder; the duplicate-id scan runs only when the set of folders changed.
3. **One `git log` per HEAD**, bounded (5000 commits); `Recent`, `Mentioning`, `Commits` answer from memory; a HEAD in `packed-refs` is read too.
4. **Pages load once per navigation and once per change**; nothing expensive in render; `Changed` events within a frame coalesce; the worklog child does not reload on a parent render.
5. **A probe stays in the repo** (`tests/perf-probe.ps1`) that measures open-item, view switches, a tick and back-to-board in the real app, so regressions are visible.
6. **Budget:** first visible change under 150 ms for a tick and a view switch, under 250 ms for opening an item, one render burst each.

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | Per-folder cache in WorkItems and Notes; duplicate scan only on folder changes | sonnet, worktree | `CachingTheRepoTests` |
| B | GitHistory: one `git log` per HEAD, lookups from memory | sonnet, worktree | `ReadingGitHistoryTests` |
| C | Pages load once, no render cascades; `tests/perf-probe.ps1` | sonnet, worktree, CDP 9365 | `UsingThePagesTests`, `UsingTheWorklogTests` |
| D | Integration, before/after numbers in the real app, board | main session | full suite |

## Verification

Red then green per chunk; the full suite after integration; the probe's before/after table on the hidden desktop; the harness numbers before/after; nothing committed.
