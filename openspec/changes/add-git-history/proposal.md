# Proposal

## Why

Git already records which commits implement a work item and who wrote them. Showing that directly saves looking it up by hand. (The work item's change log is its notes, see add-notes.)

## What Changes

- Linked commits: commits whose message mentions `#wi<id>`, plus commits picked from a list on the work item page (stored in `x-rtm.commits`).
- The current git user becomes the default contributor and note author.
- Optional "Commit" action that commits the app's pending edits to the work item with a generated message containing `#wi<id>`.

## Capabilities

### New Capabilities
- `git-history`: reading repository history for work items and committing the app's edits.

### Modified Capabilities
- none

## Impact

- Uses the `git` CLI on PATH. LibGit2Sharp would add a native library per platform for no extra benefit here.
- Read-only by default. The only write is the explicit Commit action.
