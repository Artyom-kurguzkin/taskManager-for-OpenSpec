# Spec Delta

## ADDED Requirements

### Requirement: Split a subtask into a dependency
Selecting one or more subtasks SHALL also offer "Make it a dependency" (one) / "Move into a dependency" (several), alongside "Make it a work item". It SHALL create the new work item the same way as "Make it a work item", then add its id to the source's `x-rtm.dependsOn` and set the source's status to Blocked.

#### Scenario: Create a dependency
- **WHEN** the user selects subtask 1.3 of #wi123 and picks "Make it a dependency"
- **THEN** a new work item holds that subtask, #wi123's status becomes Blocked, and `x-rtm.dependsOn` in #wi123's `.openspec.yaml` lists the new item's id

### Requirement: Waiting on and blocking panels
A work item with one or more dependencies SHALL show a "Waiting on" banner naming each one and its current status. A work item named in another item's `x-rtm.dependsOn` SHALL show a "You are blocking" panel naming each of them and its current status. Both SHALL link to the named item's page.

#### Scenario: Waiting on
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi123's page shows a "Waiting on" banner naming #wi124 and its status

#### Scenario: You are blocking
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi124's page shows a "You are blocking" panel naming #wi123 and its status

### Requirement: Status locked while a dependency is unresolved
A work item SHALL refuse to change status away from Blocked while any item in its `x-rtm.dependsOn` is not yet archived; the refusal SHALL name the unresolved dependency. Picking Blocked again SHALL always be allowed. Once every dependency is archived, status changes SHALL be allowed again.

#### Scenario: Refused
- **WHEN** #wi123 depends on #wi124, not yet archived, and the user picks Planned
- **THEN** #wi123's status stays Blocked and the message names #wi124

#### Scenario: Unblocked
- **WHEN** #wi124, the only item in #wi123's `x-rtm.dependsOn`, is archived
- **THEN** #wi123 can change to any status again
