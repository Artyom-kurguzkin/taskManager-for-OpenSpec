## ADDED Requirements

### Requirement: Reading survives folders that move
Reading the board SHALL NOT fail when a change folder is moved, deleted or being written while it is read; that folder SHALL be left out of that read and appear again on the next reload.

#### Scenario: Merge archived items
- **WHEN** the user merges completed items and the app refreshes while the merge removes their folders
- **THEN** no error is shown and the merged item appears on the board
