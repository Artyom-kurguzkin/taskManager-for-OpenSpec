## MODIFIED Requirements

### Requirement: Live sync with disk
The system SHALL update the index within 2 seconds when files under `openspec/` change on disk. An edit the app is writing SHALL NOT be reported back to the user as an outside change. A file that was read and has since been moved or deleted SHALL NOT count as changed, since writing there overwrites nothing.

#### Scenario: Edited in another editor
- **WHEN** the user ticks a checkbox in tasks.md in their code editor
- **THEN** the subtask shows as done in the app within 2 seconds

#### Scenario: Conflicting edit
- **WHEN** a file changed on disk after the app read it and before the app writes it
- **THEN** the app does not overwrite it, reloads the file, and tells the user "This file changed on disk; your edit was not saved"

#### Scenario: Folder name reused after archiving
- **WHEN** the change folder `new-work-item` was archived and the user creates another "New work item"
- **THEN** the new change is written to `openspec/changes/new-work-item` without a "changed on disk" message
