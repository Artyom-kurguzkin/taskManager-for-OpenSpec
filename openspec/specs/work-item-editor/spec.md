# work-item-editor Specification

## Purpose
Shows one work item with all of its parts and lets the user edit its header fields, description and subtasks in place, writing straight to the change's files.

## Requirements

### Requirement: Work item page
Opening a work item SHALL show its number as `#wi<id>`, title, linked features, contributors, status, priority, the change folder path, and the panels Description, Change log, Subtasks, Linked tests, Linked commits and Notes.

#### Scenario: Open from board
- **WHEN** the user clicks card #wi123
- **THEN** the work item page for #wi123 opens with all panels, and a panel without data shows a one-line empty state

### Requirement: Edit header fields
Title, features, contributors, status, priority and label SHALL be editable in place. Each edit SHALL be saved when the control loses focus or a value is picked. The title SHALL be stored in `x-rtm.title` and the label in `x-rtm.label` (values: bug, feature, enhancement, or none). Contributors who are commit authors of the change SHALL be shown without a remove control; assigned people SHALL be removable, and "+ add" SHALL offer the current user's name so a newcomer can assign themselves.

#### Scenario: Change status
- **WHEN** the user picks Blocked in the status control
- **THEN** `x-rtm.status` becomes Blocked and returning to the board shows the card under Blocked

#### Scenario: Remove a manual feature
- **WHEN** the user removes Security, which is listed in `x-rtm.features`
- **THEN** it is removed from `x-rtm.features`, while features from delta specs show no remove control

#### Scenario: Commit author stays
- **WHEN** Alice has commits in #wi123's folder and Tom is only assigned
- **THEN** Tom shows a remove control and Alice does not

### Requirement: Edit description
Clicking the description SHALL switch it to a markdown editor over the body of proposal.md (everything after the title heading). Leaving the editor SHALL save it.

#### Scenario: Save description
- **WHEN** the user edits the description and clicks elsewhere
- **THEN** proposal.md contains the new text and its title heading is unchanged

### Requirement: Manage subtasks
The subtasks panel SHALL list tasks.md checkboxes with their numbers and group headings. The user SHALL be able to tick or untick a subtask and to add one at the end of the last group, numbered after the last task.

#### Scenario: Add a subtask
- **WHEN** the last task is `- [ ] 2.3 …` and the user adds "Write docs"
- **THEN** tasks.md gains the line `- [ ] 2.4 Write docs` under group 2

### Requirement: Move subtasks to a new work item
Selecting one or more subtasks by clicking their text SHALL offer to move them into a new work item ("Make it a work item" for one, "Move into a separate work item" for several). This SHALL create a new change holding those tasks (renumbered from 1.1), with the same features and contributors, remove them from the source, and renumber the tasks left in the source.

#### Scenario: Split out
- **WHEN** the user selects subtasks 1.4 and 1.5 of #wi123 and moves them
- **THEN** a new work item with those two subtasks exists, #wi123 keeps 1.1–1.3, and both change logs mention the split

#### Scenario: One subtask
- **WHEN** the user selects only subtask 1.3 of #wi123 and picks "Make it a work item"
- **THEN** a new work item holds that subtask as 1.1 and #wi123 keeps the rest

### Requirement: Navigation and affordances
Esc and the Back button SHALL return to the previous screen, unless a text field has focus, in which case Esc only leaves the field. Every editable element SHALL show a dashed outline on hover and on keyboard focus.

#### Scenario: Esc from page
- **WHEN** the user presses Esc on a work item page opened from the board
- **THEN** the board shows again with its previous grouping and scroll position
