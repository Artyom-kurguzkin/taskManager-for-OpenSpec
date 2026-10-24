# Proposal

## Why

An existing work item could only become a dependency by typing its number into the "Add a subtask" box, which nothing on the page says. The Subtasks tile had no way to browse the other items.

## What Changes

- The Subtasks tile gets "+ wait on another work item": a filterable list of active work items (number, title, status). Choosing one makes it a dependency, with the same result as typing its number: this item is Blocked until that one is in review or done.
- The list leaves out the item itself, items it already waits on, and items that wait on it (that would be a loop).

## Capabilities

### Modified Capabilities
- `work-item-editor`: a picker for dependencies.

## Impact

- `TaskManager/Components/Pages/WorkItemPage.razor`.
