# Proposal

## Why

Work items collect context that doesn't belong in a proposal: test instructions for a developer or Playwright, investigation notes, links to docs. That context needs a home next to the change, written in markdown and linked both ways.

## What Changes

- Notes are the work item's change log entries: the Change log panel lists them, newest first, with "+ Add entry".
- Notes as markdown files in `openspec/changes/<name>/notes/n<id>.md`, with a small frontmatter block (`id`, `by`, `created`).
- Note page with a markdown editor, live preview, and metadata (linked work item, created, last modified, author).
- `[[path]]` links to repo docs, `#wi<id>` and `#n<id>` references, all resolved live. Unresolved ones are flagged.
- "Linked docs and commits" panel, filled automatically from those links and the work item's commits, plus manually added entries.

## Capabilities

### New Capabilities
- `notes`: creating, editing and linking markdown notes attached to work items.

### Modified Capabilities
- none

## Impact

- Depends on `add-workspace`. The commit list comes from `add-git-history` when present.
- New files inside change folders. The OpenSpec CLI ignores them (checked on 1.14.3) and they move with the change when it is archived.
