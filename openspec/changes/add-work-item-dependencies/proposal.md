# Proposal

## Why

Splitting a subtask into its own work item ("Make it a work item") assumes the two items are now independent. Sometimes they aren't: a subtask turns out to need its own effort *before* the rest of the item can continue. There was no way to say "this item can't move forward until that other one is done."

## What Changes

- Selecting a subtask offers a second option, "Make it a dependency", alongside the existing "Make it a work item". It creates the new item the same way, but also marks the source as depending on it and moves the source to Blocked.
- Related work items are listed under Subtasks: "waits on" rows (highlighted while unresolved, struck through once done, removable) and "blocks" rows. Each opens that item.
- While any dependency is unresolved (not yet In Review or archived), the source item's status can only be set to Blocked — any other choice is refused with a message naming the dependency. Once every dependency is In Review or archived, status changes are allowed again.
- Typing another item's id in the subtask box (#wi12) links it as a dependency; it blocks only if that item isn't already In Review or Completed.
