# Spec Delta

## ADDED Requirements

### Requirement: Split a subtask into a dependency
Selecting one or more subtasks SHALL also offer "Make it a dependency" (one) / "Move into a dependency" (several), alongside "Make it a work item". It SHALL create the new work item the same way as "Make it a work item", then add its id to the source's `x-rtm.dependsOn` and set the source's status to Blocked.

#### Scenario: Create a dependency
- **WHEN** the user selects subtask 1.3 of #wi123 and picks "Make it a dependency"
- **THEN** a new work item holds that subtask, #wi123's status becomes Blocked, and `x-rtm.dependsOn` in #wi123's `.openspec.yaml` lists the new item's id

### Requirement: Related work items under Subtasks
The Subtasks panel SHALL list, after the subtasks, each work item this one waits on ("waits on", highlighted while unresolved, struck through once In Review or Completed, with a control to remove it) and each work item waiting on this one ("blocks"), with its current status. Each SHALL open the named item's page.

#### Scenario: Waits on
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi123's Subtasks panel shows a highlighted "waits on" row for #wi124 with its status, and removing it clears it from `x-rtm.dependsOn`

#### Scenario: Blocks
- **WHEN** #wi123's `x-rtm.dependsOn` lists #wi124
- **THEN** #wi124's Subtasks panel shows a "blocks" row for #wi123 with its status

### Requirement: Status locked while a dependency is unresolved
A dependency SHALL count as resolved once it is In Review or Completed (archived). A work item SHALL refuse to change status away from Blocked while any item in its `x-rtm.dependsOn` is unresolved; the refusal SHALL name the unresolved dependency. Picking Blocked again SHALL always be allowed. Once every dependency is resolved, status changes SHALL be allowed again.

#### Scenario: Refused
- **WHEN** #wi123 depends on #wi124, which is In Progress, and the user picks Planned
- **THEN** #wi123's status stays Blocked and the message names #wi124

#### Scenario: Unblocked
- **WHEN** #wi124, the only item in #wi123's `x-rtm.dependsOn`, moves to In Review or is archived
- **THEN** #wi123 can change to any status again

### Requirement: Depend on an existing item
Typing another work item's id (`#wi12`, `wi12` or `12`) in the subtask box SHALL make that item a dependency instead of adding a subtask. This item SHALL move to Blocked only when that dependency is unresolved; a dependency that is already In Review or Completed SHALL be linked without blocking. It SHALL be refused, with a message, when that item doesn't exist, is this item, is already a dependency, or already waits on this item.

#### Scenario: Wait on an existing item
- **WHEN** the user types `#wi123` in the subtask box of #wi107 and adds it
- **THEN** #wi107 gains `x-rtm.dependsOn: [123]`, becomes Blocked, lists #wi123 as "waits on" under Subtasks, and no subtask is added

#### Scenario: Already in review
- **WHEN** the user types the id of an item that is In Review
- **THEN** it is added to `x-rtm.dependsOn`, this item's status is unchanged, and a message says it doesn't block

#### Scenario: Only done dependencies
- **WHEN** every item in #wi107's `x-rtm.dependsOn` is In Review or Completed
- **THEN** their "waits on" rows under Subtasks are struck through, like done subtasks

## ADDED Requirements

### Requirement: Pick an item to wait on
The Subtasks tile of an active work item SHALL offer "+ wait on another work item", a list of the other active work items (number, title, status) with a filter box that matches number, title and status. Choosing one SHALL link it as a dependency exactly as typing its number does. The list SHALL leave out the item itself, items it already waits on, and items that wait on it.

#### Scenario: Choose from the list
- **WHEN** the user opens the picker on #wi107, types "health" and picks #wi123
- **THEN** #wi107 waits on #wi123, shows it under Subtasks, and is Blocked until #wi123 is in review or done

#### Scenario: No loops
- **WHEN** #wi107 waits on #wi123 and the user opens the picker on #wi123
- **THEN** neither #wi123 nor #wi107 is listed
