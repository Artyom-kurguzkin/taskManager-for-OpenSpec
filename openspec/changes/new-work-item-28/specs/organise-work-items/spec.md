## ADDED Requirements

### Requirement: The board keeps the user's arrangement of a project
The app SHALL save what the user arranges for each project (view, grouping, sort, hidden columns, rows and projects, compact cards, show hidden, and the list's column order and widths) in the user's settings file under that project, and the board and the side menu SHALL show the arrangement of the project the user switched to, not of the project that an opened or added work item made active.

#### Scenario: Hidden columns survive adding an item in another project
- **WHEN** the user hides a column, adds a work item in another open project and comes back to the board through the list and the calendar
- **THEN** the column is still hidden and the other project's own arrangement is unchanged

#### Scenario: Switching to a project's tab shows its own arrangement
- **WHEN** the user switches to another project's tab and back
- **THEN** each tab shows the arrangement saved for that project

### Requirement: The user settings file is kept where the user chooses
The app SHALL keep the user's settings in one file on this computer, never in a repository and never synced, and the Settings page SHALL show its folder and move the file to a folder the user chooses, remembering the choice in a pointer file at the default location so a later start finds it.

#### Scenario: Moving the settings file
- **WHEN** the user chooses a folder and moves the settings there
- **THEN** the file is in that folder, is used from then on, and is found there after a restart

#### Scenario: A location that cannot be used
- **WHEN** the pointer cannot be read, or names a folder or file that is gone
- **THEN** the default settings file is used and the Settings page says so

#### Scenario: A folder that cannot take the file
- **WHEN** the chosen folder does not exist or already holds a settings.json
- **THEN** nothing is moved or overwritten and the page says why
