# workspace Specification

## Purpose
Lets the app use a repository's openspec/ folder as its only data store: it reads changes, capabilities and tasks as work items, features and subtasks, and writes edits back without damaging the files.

## Requirements

### Requirement: Open a repository
The system SHALL let the user pick a folder and SHALL accept it when it contains an `openspec/` directory. The last opened folder SHALL reopen on the next start.

#### Scenario: Folder with OpenSpec
- **WHEN** the user picks a folder that contains `openspec/`
- **THEN** the board shows the work items found in it

#### Scenario: Folder without OpenSpec
- **WHEN** the user picks a folder with no `openspec/` directory
- **THEN** the system shows "No openspec/ folder here. Run `openspec init` in this repo first." and keeps the previous workspace open

#### Scenario: Reopen on start
- **WHEN** the app starts and the last opened folder still exists
- **THEN** that folder opens without asking

### Requirement: Changes are work items
The system SHALL treat every folder under `openspec/changes/` (excluding `archive/`) as an active work item, and every folder under `openspec/changes/archive/` as a Completed work item.

#### Scenario: Active change
- **WHEN** `openspec/changes/add-health-check/` exists
- **THEN** a work item titled from `x-rtm.title` (else a proposal heading other than the template's "# Proposal", else the folder name) appears with the status from its metadata

#### Scenario: Archived change
- **WHEN** `openspec/changes/archive/2026-09-18-session-timeout/` exists
- **THEN** a work item with status Completed appears for it

### Requirement: Work item metadata
The system SHALL read and write work item metadata in an `x-rtm:` mapping inside the change's `.openspec.yaml`, with the keys `id`, `title`, `status`, `priority`, `contributors`, `features`, `lanes` and `hidden`. `contributors` holds the people assigned in the app. A missing key SHALL fall back to a default: status Backlog, priority low, contributors empty.

#### Scenario: Change created outside the app
- **WHEN** a change folder has a `.openspec.yaml` without an `x-rtm:` block
- **THEN** the work item shows as Backlog with lower priority, and it gets an `id` written on its first edit in the app

#### Scenario: Unknown keys survive
- **WHEN** the app updates `x-rtm.status` in a file that also has other keys and comments
- **THEN** every other key, comment and line in that file stays byte-for-byte unchanged

### Requirement: Status values
The system SHALL support the statuses Backlog, Planned, Confirmed, In Progress, Blocked and Completed. Completed SHALL be derived only from the change being archived and SHALL NOT be stored in metadata.

#### Scenario: Stored Completed is ignored
- **WHEN** an active change's metadata says `status: Completed`
- **THEN** the work item shows as Blocked with the warning "Marked Completed but not archived"

### Requirement: Capabilities are features
The system SHALL treat every capability under `openspec/specs/`, plus every path listed in `openspec/config.yaml` under `x-rtm.features`, as a feature. When a path has two segments, the first segment SHALL be its side (for example `non-functional/availability` is Availability on side Non-Functional); a single-segment path SHALL be on side "Ungrouped". Features from `x-rtm.features` without a spec SHALL be marked as created in the app.

#### Scenario: Grouped feature
- **WHEN** `openspec/specs/functional/remittances/spec.md` exists
- **THEN** feature Remittances is listed on side Functional

#### Scenario: Feature created in the app
- **WHEN** `x-rtm.features` lists `functional/payments` and no spec exists for it
- **THEN** feature Payments is listed on side Functional and can be deleted from the app

### Requirement: Linked features
A work item's features SHALL be the capabilities it has delta specs for (`specs/<capability>/` inside the change) plus any listed in `x-rtm.features`. Features derived from delta specs SHALL NOT be removable in the app.

#### Scenario: Derived and manual features
- **WHEN** a change has `specs/non-functional/availability/spec.md` and `x-rtm.features: [non-functional/security]`
- **THEN** the work item lists Availability (locked) and Security (removable)

### Requirement: Tasks are subtasks
The system SHALL read each checkbox line in a change's `tasks.md` as a subtask, keeping its number, text, done state and group heading. A box holding only `x` (any case, any spacing) SHALL count as done.

#### Scenario: Mixed checkboxes
- **WHEN** tasks.md contains `- [x] 1.1 Add route`, `- [ x] 1.2 Hash` and `- [~] 1.3 Test`
- **THEN** subtasks 1.1 and 1.2 are done and 1.3 is not

### Requirement: Live sync with disk
The system SHALL update the index within 2 seconds when files under `openspec/` change on disk. An edit the app is writing SHALL NOT be reported back to the user as an outside change.

#### Scenario: Edited in another editor
- **WHEN** the user ticks a checkbox in tasks.md in their code editor
- **THEN** the subtask shows as done in the app within 2 seconds

#### Scenario: Conflicting edit
- **WHEN** a file changed on disk after the app read it and before the app writes it
- **THEN** the app does not overwrite it, reloads the file, and tells the user "This file changed on disk; your edit was not saved"

### Requirement: Work item numbers
The system SHALL give each work item a unique positive `id`, allocated as one more than the highest id among active and archived changes. Duplicate ids found on disk SHALL be reported and not changed automatically.

#### Scenario: New number
- **WHEN** the highest existing id is 124 (in an archived change) and a work item is created
- **THEN** the new work item gets id 125

#### Scenario: Duplicate on disk
- **WHEN** two changes both have `x-rtm.id: 108`
- **THEN** both still show, and a warning names the two folders

### Requirement: Contributors from git
In a git repository, a work item's contributors SHALL be the authors of commits that changed its change folder (following it into the archive), newest first, followed by the people assigned in `x-rtm.contributors`, without duplicates. Outside git, contributors SHALL be the assigned people only. Commit authors SHALL be read with one `git log` over `openspec/changes` and re-read only when the branch head moves, so a new commit shows without reopening the repository.

#### Scenario: Same person once
- **WHEN** Artyom-kurguzkin committed to a change and is also assigned in `x-rtm.contributors`
- **THEN** the work item lists Artyom-kurguzkin once

#### Scenario: Newcomer
- **WHEN** someone who just cloned the repository (no commits yet) assigns themselves to #wi12
- **THEN** their git `user.name` is added to `x-rtm.contributors` and #wi12 shows them as a contributor
