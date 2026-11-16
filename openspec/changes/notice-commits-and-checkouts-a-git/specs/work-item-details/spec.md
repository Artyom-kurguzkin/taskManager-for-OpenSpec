## ADDED Requirements

### Requirement: Git changes made outside the app are noticed
The app SHALL notice a commit, checkout or index change made outside it and refresh the uncommitted marks and the last commit without an openspec edit, and examining an item SHALL NOT walk the changes folder.

#### Scenario: Commit from a shell
- **WHEN** the user commits the edited items from a shell
- **THEN** their uncommitted dots disappear from the board without any openspec change
