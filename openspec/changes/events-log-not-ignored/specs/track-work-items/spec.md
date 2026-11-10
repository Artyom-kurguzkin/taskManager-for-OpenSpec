## ADDED Requirements

### Requirement: The worklog is tracked by git
When the app opens a repository it SHALL ask git whether a work item's `events.log` would be ignored and, when it would, append `!openspec/**/events.log` under a "Repo Tasks" comment to the repository's root `.gitignore`, once, and tell the user to add the files. It SHALL write nothing when the file is not ignored, when the exception is already there, or when the folder is not a git repository.

#### Scenario: A repository that ignores log files
- **WHEN** the user opens a repository whose `.gitignore` has `*.log`
- **THEN** `.gitignore` gains the exception, git lists the events files as untracked, and a toast says to run `git add openspec`

#### Scenario: Still ignored
- **WHEN** a parent folder of the events files is itself ignored
- **THEN** the app says the worklog is still ignored and does not add the line again on later opens
