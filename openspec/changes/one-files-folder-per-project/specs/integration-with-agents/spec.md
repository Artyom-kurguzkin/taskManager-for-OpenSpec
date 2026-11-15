## ADDED Requirements

### Requirement: Agents find plans in the project's plans folder

The agent guide (openspec/REPO-TASKS.md) and the board tools SHALL use the project's plans folder, which is `<filesDir>/plans` for a project with a files folder and the old plans folder otherwise, and SHALL follow it when it changes.

#### Scenario: Guide after the folder changes

- **WHEN** a project's files folder is changed
- **THEN** the guide names the new plans and images folders

#### Scenario: Linking a plan

- **WHEN** an agent links a plan with board_update plan
- **THEN** the path is accepted under the project's plans folder
