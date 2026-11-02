## ADDED Requirements

### Requirement: Agent conventions file
When a project is opened the app SHALL ensure `openspec/REPO-TASKS.md` exists, holding the conventions agents follow here (id allocation, change-log notes, agent stamps, linked tests and commits). A missing file SHALL be created and the user told with a notice suggesting a pointer in CLAUDE.md / AGENTS.md. A file carrying the app's version marker SHALL be refreshed when the app's text is newer; a file without the marker SHALL NOT be changed.

#### Scenario: New project
- **WHEN** a project without `openspec/REPO-TASKS.md` is opened
- **THEN** the file exists afterwards and a notice names it

#### Scenario: The user's own file
- **WHEN** the file exists without the app's marker
- **THEN** it is left exactly as it is
