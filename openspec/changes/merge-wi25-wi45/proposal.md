# Proposal

<!-- merged from #wi25 Subtask dependencies that block the parent item -->
## Why

Splitting a subtask into its own work item ("Make it a work item") assumes the two items are now independent. Sometimes they aren't: a subtask turns out to need its own effort *before* the rest of the item can continue. There was no way to say "this item can't move forward until that other one is done."

## What Changes

- Selecting a subtask offers a second option, "Make it a dependency", alongside the existing "Make it a work item". It creates the new item the same way, but also marks the source as depending on it and moves the source to Blocked.
- Related work items are listed under Subtasks: "waits on" rows (highlighted while unresolved, struck through once done, removable) and "blocks" rows. Each opens that item.
- While any dependency is unresolved (not yet In Review or archived), the source item's status can only be set to Blocked — any other choice is refused with a message naming the dependency. Once every dependency is In Review or archived, status changes are allowed again.
- Typing another item's id in the subtask box (#wi12) links it as a dependency; it blocks only if that item isn't already In Review or Completed.

<!-- merged from #wi45 subtasks loose their heading when created + Split from #wi26 + Split from #wi39 -->
<!-- merged from #wi26 subtasks loose their heading when created -->
## Why

Split out of #wi25 Subtask dependencies that block the parent item.

## What Changes

- "Waiting on" banner and "You are blocking" panel on the work item page; verify the "Waiting on" and "You are blocking" scenarios
- subtasks loose their heading when created

<!-- merged from #wi42 Split from #wi26 + Split from #wi39 -->
<!-- merged from #wi39 Split from #wi26 -->
## Why

Split out of #wi26 subtasks loose their heading when created.

## What Changes

- subtask test1

<!-- merged from #wi40 Split from #wi39 -->
## Why

Split out of #wi39 Split from #wi26.

## What Changes

- subtask test1
