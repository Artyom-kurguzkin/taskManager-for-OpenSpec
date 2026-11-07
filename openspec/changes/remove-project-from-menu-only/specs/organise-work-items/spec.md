## MODIFIED Requirements

### Requirement: Remove a project
The repository menu SHALL end with "Remove a project…" while more than one project is open. It SHALL ask which project to remove and close that project's tab; nothing on disk SHALL change, and the last project SHALL not be removable. It SHALL be the only way to remove a project: the project rows in the menu SHALL have no close button.

#### Scenario: Remove one of two
- **WHEN** the user chooses Remove a project… and picks demo2
- **THEN** demo2 leaves the board and the tabs, its folder stays, and the menu no longer offers Remove a project…

#### Scenario: No close button on a project row
- **WHEN** the user opens the repository menu with two projects open
- **THEN** each project row shows its name and its eye, and no × button
