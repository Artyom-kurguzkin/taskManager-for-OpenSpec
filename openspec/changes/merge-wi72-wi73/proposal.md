# Proposal

<!-- merged from #wi72 Show what an agent added or changed -->
## Why

Agents edit the openspec files directly, so work they add or change looks the same as work the user made in the app. The user needs to see it and acknowledge it.

## What Changes

- An agent stamps what it touched: `agentBy` and `agentAt` in the item's `x-rtm` block and in a note's frontmatter, and a trailing `<!-- agent: Name -->` on a subtask line in tasks.md.
- The app shows an **agent** chip on cards, list rows, the work item page (item, subtasks, notes) and the note page until the user clicks it. Clicking writes `reviewedAt` (or removes the subtask comment); the chip returns when the agent stamps a later time.
- The side menu has an "agent changes only" filter.
- Repo CLAUDE.md tells agents to stamp their work.

## Capabilities

### Modified Capabilities

- `board`: chip on cards and rows, filter.
- `work-item-editor`: chips on the page, review by click.

<!-- merged from #wi73 Agent conventions file for every project -->
## Why

Agents only follow the app's conventions (ids, notes, stamps, tests) if they read them. Each project should carry a short file that says so.

## What Changes

- Opening a project creates `openspec/REPO-TASKS.md` with the conventions, if missing, and says so in a notice asking the user to point CLAUDE.md / AGENTS.md at it.
- A copy carrying the app's version marker is refreshed when the app's text is newer; a file without the marker is never touched.

## Capabilities

### Modified Capabilities

- `workspace`: the conventions file.
