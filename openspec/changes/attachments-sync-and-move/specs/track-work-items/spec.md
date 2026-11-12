## ADDED Requirements

### Requirement: Attachments sync through the connected storage, not through git

For a project with an approved connected storage the app SHALL sync the attachments folder with its own area of that storage in the same pass as the board, as whole files that are never merged. The attachments folder SHALL stay out of git. A switch per project, on by default, SHALL turn this off without deleting anything.

#### Scenario: A picture reaches the other computer

- **WHEN** a picture is attached on one computer and both computers sync
- **THEN** the same file is in the other computer's attachments folder

#### Scenario: Both computers changed one attachment

- **WHEN** the same attachment was changed on two computers
- **THEN** the newer file is kept and the other is in the trash

#### Scenario: Not approved

- **WHEN** the storage has not been approved on this computer
- **THEN** no attachment is sent or received

#### Scenario: Switched off

- **WHEN** Sync attachments too is turned off
- **THEN** attachments stay on the computer and nothing is deleted in the storage

### Requirement: The attachments folder belongs to the project and moves with its files

The attachments folder SHALL be a setting saved with the project. Changing it in Settings while the old folder holds files SHALL ask first; on Move the app SHALL move every file without overwriting and rewrite the paths in notes and descriptions. A change of the folder that arrives from another computer SHALL be followed by moving this computer's files.

#### Scenario: Move

- **WHEN** the user changes the folder and chooses Move
- **THEN** the files are in the new folder and notes and descriptions show their images

#### Scenario: Keep

- **WHEN** the user chooses Keep them where they are
- **THEN** only new attachments go to the new folder

#### Scenario: A name already taken

- **WHEN** a different file with the same name is already in the new folder
- **THEN** both are kept and the note points to the moved one

### Requirement: A file too large to sync is skipped and reported

A file over the size limit SHALL NOT be uploaded or downloaded and SHALL NOT stop the rest of the sync; the settings SHALL list such files.

#### Scenario: One large file

- **WHEN** one attachment is over the limit
- **THEN** the other files sync and the settings name the large one
