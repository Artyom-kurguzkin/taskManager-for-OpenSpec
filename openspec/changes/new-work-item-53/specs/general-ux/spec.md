## ADDED Requirements

### Requirement: A status can be renamed from where it is seen
The app SHALL rename a project's custom status from the Rename status menu item of a board column, a list group or a calendar group. The status SHALL keep its place in the project's list, and the board's saved hidden, ordered and merged columns SHALL follow the new name.

#### Scenario: Renaming a custom status
- **WHEN** the user chooses Rename status on a custom status and types a new name
- **THEN** every open work item with that status, hidden ones included, has the new name and its in-status-since day is unchanged
- **AND** the status keeps its place in the project's list and a hidden or merged column of it stays hidden or merged under the new name

#### Scenario: A core status
- **WHEN** a rename of Backlog, In Progress or Completed, in any letter case, is asked for
- **THEN** it is refused and the menu does not offer it

#### Scenario: A name already in use
- **WHEN** the user types an empty name or the name of another status of the project
- **THEN** the rename is refused with the reason and nothing changes

#### Scenario: Several projects have the status
- **WHEN** the user renames a status from the shared header row and more than one open project has it
- **THEN** the app asks which project, and only that project's status is renamed
