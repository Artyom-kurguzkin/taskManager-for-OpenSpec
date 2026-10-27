## Why

A work item number is max + 1 at creation, so two changes created in two places (another checkout, a branch, a copied folder, a change written by hand) can carry the same number. Until now the board only warned ("#wi25 is used by new-work-item and refactor-ui-project") and left it to the user. Git-backed trackers that keep sequential numbers all hit this; Beads moved to hash ids for it. This app keeps the human numbers and resolves collisions deterministically instead.

## What Changes

- `WorkItemsApi.ResolveDuplicateIds()`: for every number carried by more than one change, every change but the keeper takes the next free number. The keeper is an archived change first (its number is history), then the earliest created, then the first folder name — the same answer in every copy of the repository.
- The workspace runs it on every change (opening a repository, a file from outside, the app's own edits), so a duplicate never reaches a page: a toast says which change was renumbered, and that change gets a change-log note saying what happened. References other items hold to the number stay: they most likely meant the keeper.

## Capabilities

### Modified Capabilities

- `workspace`: duplicate numbers are resolved, not reported.