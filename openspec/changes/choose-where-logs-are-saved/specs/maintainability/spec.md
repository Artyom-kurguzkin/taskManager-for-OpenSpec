## ADDED Requirements

### Requirement: The logs folder is the user's choice

Settings SHALL offer a Logs folder in the General tab. Run logs, crash notes and freeze dumps SHALL go to the chosen folder at once, and the new run log's first line SHALL name the previous file. The app's own run markers and publish-stop files SHALL stay in the app's folder. A chosen folder that is missing or cannot be written SHALL fall back to the default with a warning.

#### Scenario: Choosing a logs folder

- **WHEN** the user chooses a folder in the Logs folder row
- **THEN** the run log continues in that folder at once, its first line names the previous file
- **AND** later crash notes and freeze dumps are written there, while run markers stay in the app's folder

#### Scenario: The chosen folder is missing

- **WHEN** the app starts and the saved logs folder is missing or cannot be written
- **THEN** the logs go to the default folder
- **AND** a warning says the chosen folder could not be used

#### Scenario: Move existing logs here

- **WHEN** the user presses Move existing logs here
- **THEN** the run logs, crash notes and dumps from the previous folder are moved into the chosen folder
- **AND** nothing else is moved, run markers stay where they are
