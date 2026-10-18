# Proposal

## Why

Git already records who changed a work item and when, and which commits implement it. Showing that history directly avoids keeping a separate changelog by hand.

## What Changes

- Change log on the work item page, built from commits that touched the change folder (archived path included).
- Linked commits: commits whose message mentions `#wi<id>`.
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
