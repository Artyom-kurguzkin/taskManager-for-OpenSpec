# Proposal

<!-- merged from #wi62 Numbers used twice resolve themselves -->
## Why

A work item number is max + 1 at creation, so two changes created in two places (another checkout, a branch, a copied folder, a change written by hand) can carry the same number. Until now the board only warned ("#wi25 is used by new-work-item and refactor-ui-project") and left it to the user. Git-backed trackers that keep sequential numbers all hit this; Beads moved to hash ids for it. This app keeps the human numbers and resolves collisions deterministically instead.

## What Changes

- `WorkItemsApi.ResolveDuplicateIds()`: for every number carried by more than one change, every change but the keeper takes the next free number. The keeper is an archived change first (its number is history), then the earliest created, then the first folder name — the same answer in every copy of the repository.
- The workspace runs it on every change (opening a repository, a file from outside, the app's own edits), so a duplicate never reaches a page: a toast says which change was renumbered, and that change gets a change-log note saying what happened. References other items hold to the number stay: they most likely meant the keeper.

## Capabilities

### Modified Capabilities

- `workspace`: duplicate numbers are resolved, not reported.

<!-- merged from #wi81 Archiving refreshes stale delta specs itself -->
## Why

Moving a work item to Completed failed with "MODIFIED failed for header … current spec contains scenario(s) not present in the modified block". Archiving replaces a modified requirement with the change's copy of it, and the CLI refuses to drop scenarios the main spec gained after that copy was written (another item's archive added them). The user had to patch the delta by hand.

## What Changes

- Before archiving, the app refreshes every delta spec of the change: each MODIFIED requirement gets, appended verbatim from the current main spec, the scenarios it lacks. Its own text and scenarios stay; ADDED, REMOVED and RENAMED blocks are untouched; a requirement the main spec no longer has is left alone (the CLI's message then says so).
- Nothing is written when nothing is missing.

## Capabilities

### Modified Capabilities

- `organise-work-items`: complete by archiving.
