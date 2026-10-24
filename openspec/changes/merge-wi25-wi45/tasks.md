# Tasks

## 1. Tasks

- [x] 1.1 Store dependencies as `x-rtm.dependsOn` (ids); add `MoveSubtasksToDependency` and a reverse `GetBlocking` lookup; verify the "Create a dependency" scenario
- [x] 1.2 Gate status changes while any dependency is unresolved; verify the "Refused" and "Unblocked" scenarios
- [x] 1.3 Typing a work item id in the subtask box makes it a dependency (linked without blocking when In Review or Completed); verify the "Wait on an existing item" and "Already in review" scenarios
- [x] 1.4 List dependencies ("waits on", removable) and dependents ("blocks") under Subtasks instead of a banner and a separate panel; verify the "Waits on" and "Blocks" scenarios
- [x] 1.5 "Waiting on" banner and "You are blocking" panel on the work item page; verify the "Waiting on" and "You are blocking" scenarios
- [ ] 1.6 subtasks loose their heading when created
- [ ] 1.7 subtask test1
