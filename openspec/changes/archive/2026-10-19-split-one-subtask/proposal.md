# Proposal

## Why

Splitting subtasks out of a work item only appeared after selecting two or more, so a single subtask could not be turned into its own work item.

## What Changes

- Selecting one subtask on the work item page offers "Make it a work item"; two or more still offer "Move into a separate work item".

## Capabilities

### New Capabilities
- none

### Modified Capabilities
- `work-item-editor`: moving subtasks to a new work item works from one selected subtask.

## Impact

- WorkItemPage only; `MoveSubtasksToNew` already handles one subtask.
