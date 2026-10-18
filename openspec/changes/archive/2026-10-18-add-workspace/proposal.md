# Proposal

## Why

The task manager needs one source of truth that already lives with the code. A repository's `openspec/` folder already holds changes, tasks and specs. This change makes the app read and write that folder directly, so no separate database has to stay in sync with the repo.

## What Changes

- Desktop app shell (Windows, macOS, Linux) that opens a repository folder and remembers the last one opened.
- Indexing of `openspec/` into work items (changes, active and archived), features (capabilities) and subtasks (`tasks.md` checkboxes).
- App-owned metadata (number, status, priority, contributors, extra features) stored in an `x-rtm:` block inside each change's `.openspec.yaml`.
- Live reload when files change on disk, plus write-back that only touches the edited lines or keys.
- Allocation of `#wi` numbers that are unique across active and archived changes.

## Capabilities

### New Capabilities
- `workspace`: opening a repository, reading the OpenSpec folder as work items, features and subtasks, keeping it in sync with disk, and writing changes back safely.

### Modified Capabilities
- none

## Impact

- New .NET 10 solution: Photino.Blazor host, MudBlazor UI, Markdig and YamlDotNet for parsing.
- The app writes inside the user's repository. Each write is a small file edit, and git keeps the history.
- Every later change (board, editor, notes, git history, tests) builds on this index.
