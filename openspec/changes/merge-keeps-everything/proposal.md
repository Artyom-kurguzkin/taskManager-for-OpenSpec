# Proposal

## Why

Merging work items deleted the source folders after copying only some of their content. The notes (change log), label, linked commits and tests, dependencies, lanes, task groups and any other files in the folder were lost, and so were the links from git commits that mention a source's number.

## What Changes

- Notes move into the merged change as they are, keeping their numbers.
- The label (the first one set), manually linked commits, linked tests (the copy with a note wins), lanes (first source wins per column) and dependencies are kept. Dependencies between the merged items themselves are dropped.
- Items that waited on a source now wait on the merged item.
- Task groups stay groups, headed by the item they came from ("Title · Group" when an item had several).
- Other files in a source's folder are copied to `from-wi<id>/` in the merged change.
- Archived items are always mergeable: their specs are already in `openspec/specs`, so they never conflict. With only archived sources the result is archived and keeps their specs; with an active source the archived specs go under `from-wi<id>/specs/` so archiving the result cannot apply them twice.
- The merged change records the numbers it came from (`x-rtm.mergedFrom`), and its page lists commits that mention any of them.

## Capabilities

### Modified Capabilities
- `board`: merging keeps the notes, links, task groups and other files of every source.

## Impact

- `WorkItems/Commands.cs`, `Infrastructure/Metadata.cs`, `Queries.cs`, `BusinessRules/TasksFile.cs`, `PublicDTOs/WorkItem.cs`, `TaskManager/Components/Pages/WorkItemPage.razor`.
