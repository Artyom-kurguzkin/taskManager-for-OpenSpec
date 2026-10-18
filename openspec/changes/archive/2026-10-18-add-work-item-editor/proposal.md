# Proposal

## Why

Opening a card has to show everything about one work item and let the user edit it in place. Today that means jumping between proposal.md, tasks.md, `.openspec.yaml` and git.

## What Changes

- Work item page: number, title, linked features, contributors, status and priority, all editable inline.
- Description that edits proposal.md as markdown.
- Subtasks from tasks.md: tick, add, and move a selection of them into a new work item.
- Panels for change log, linked tests, linked commits and notes. Their data comes from `add-git-history`, `add-test-links` and `add-notes`, and each shows an empty state until those changes land.
- Esc or Back returns to the previous screen. Everything editable gets a dashed outline on hover.

## Capabilities

### New Capabilities
- `work-item-editor`: viewing and editing a single work item and its subtasks.

### Modified Capabilities
- none

## Impact

- Depends on `add-workspace`. Writes to proposal.md, tasks.md and `.openspec.yaml`.
